using System;
using UnityEngine;

namespace FirstPersonTennis
{
    // 테니스 AI: 공을 읽고 타점으로 가서, 상대 위치를 보고 구질과 방향을 고른다. 서브도 넣는다.
    // 실력 차이는 난이도 프로필(반응, 이동, 오차, 샷 선택, 위치 선정)로 만들고, 스테미너·타격 판정은 플레이어와 같은 규칙.
    // 가까운 쪽(side -1)에 두면 AI끼리 대결(관전, 밸런스 측정)도 한다.
    // 복식: 공은 떨어질 자리에 더 가까운 팀원이 맡고, 맡지 않을 때는 내 쪽 절반(네트 앞 또는 베이스라인)을 지킨다.
    // 상대 두 명 사이·바깥의 빈 곳을 노린다.
    public class AiOpponent : MonoBehaviour, IMover, IShotIntentSource
    {
        [SerializeField] TennisConfig config;
        [SerializeField] TennisBall ball;
        [SerializeField] AutoSwing swing;
        [Tooltip("상대 선수 (빈 곳을 노릴 때 본다)")]
        [SerializeField] Transform opponent;
        [SerializeField] int randomSeed = 7;

        // 남은 궤적이 이 높이를 넘으면 스매시를 노린다 (m)
        const float SmashApex = 3.2f;
        // 걸어서 가면 타점에 이만큼 늦어도 뛰지 않는다 (초)
        const float PaceMargin = 0.15f;
        // 복식: 맡은 공은 팀원이 이만큼 더 가까워져야 넘긴다 (m)
        const float HandOver = 1f;
        // 복식: 팀원과 이보다 가까이 붙지 않는다 (m)
        const float MinGap = 1f;

        public AiDifficulty Difficulty { get; private set; }
        public AiProfile Profile => profile;
        public CharacterPreset Character => character;
        public Vector3 Velocity { get; private set; }
        public float Stamina01 { get; private set; } = 1f;
        public bool Exhausted { get; private set; }
        public bool IsSprinting { get; private set; }
        public bool AtNet => atNet;
        public bool SkipRequested { get; private set; }
        public bool Serving => serveState != ServeState.None;
        // 튜토리얼: 공을 주기만 하고 받아치지 않는다 (제자리에 선다)
        public bool FeedOnly { get; set; }
        public Vector3 FeedContact => swing.FeedContact;
        public int Side => swing.Side;
        public bool Doubles => teammate != null;

        enum ServeState { None, Waiting, Tossing }

        AiProfile profile;
        CharacterPreset character = new CharacterPreset();
        PlayerStats stats = PlayerStats.Neutral;
        System.Random rng;
        Trajectory outgoing;
        ShotIntent planned;
        bool hasPlan;
        bool smashDecided;
        bool tacticDecided;      // 이번 공의 상황 판단 (수비 샷, 드롭샷)을 했는지
        bool readsOut;           // 이번 공이 아웃인지 제대로 읽는지
        float reactionTimer;
        Vector3 misjudgeOffset;
        float misjudgeUntil;
        bool atNet;              // 네트로 들어가 발리로 끝내려는 중
        Vector3 recoverSpot;

        // 복식
        Transform teammate;      // 같은 팀 선수 (단식이면 null)
        AiOpponent mateAi;       // 팀원이 AI면 (플레이어면 null)
        Transform opponent2;     // 두 번째 상대
        float lane = 1f;         // 맡은 쪽 (월드 x 부호)
        bool chasing;            // 이번 공을 내가 맡았는지

        ServeState serveState;
        float serveTimer;
        float releaseHeight;
        bool serveDeuce, serveSecond;

        float Facing => Court.Facing(Side);
        float MaxAimX => Mathf.Max(0.5f, Court.PlayHalfWidth - config.aimMargin);
        float Baseline => (Court.HalfLength + 0.8f) * Side;
        float NetDepth => Court.ServiceLine * 0.5f * Side;
        float WalkSpeed => config.walkSpeed * stats.MoveScale;
        float RunSpeed => Exhausted ? WalkSpeed : profile.speed * stats.MoveScale;

        void Awake()
        {
            rng = new System.Random(randomSeed);
            outgoing = new Trajectory(1500);
            SetDifficulty(config.defaultDifficulty);
            SetCharacter(config.Character(config.defaultAiCharacter));
            recoverSpot = new Vector3(0f, 0f, Baseline);
        }

        void OnEnable() => swing.Hit += OnOwnShot;
        void OnDisable() => swing.Hit -= OnOwnShot;

        void Start() => FaceNet();

        public void SetDifficulty(AiDifficulty difficulty)
        {
            Difficulty = difficulty;
            profile = config.AiProfileFor(difficulty);
            swing.ErrorScale = profile.errorScale;
        }

        // AI가 쓰는 프리셋 선수: 스탯은 플레이어와 같은 규칙, 샷 성향은 선수 유형에 맞춘다
        public void SetCharacter(CharacterPreset preset)
        {
            character = preset ?? new CharacterPreset();
            stats = config.StatsOf(character);
            swing.Stats = stats;
        }

        public void SetOpponent(Transform target) => SetTeam(null, target, null);

        // 복식: 팀원과 상대 두 명. 단식이면 teammate·opponentB = null
        public void SetTeam(Transform teammateTransform, Transform opponentA, Transform opponentB)
        {
            teammate = teammateTransform;
            mateAi = teammate != null ? teammate.GetComponent<AiOpponent>() : null;
            opponent = opponentA;
            opponent2 = opponentB;
        }

        // 복식 포인트 시작 때 (자리를 잡은 뒤): 지금 선 자리의 좌우를 맡고, 네트 앞 담당인지 정한다
        public void SetDoublesRole(bool netPlayer)
        {
            atNet = netPlayer;
            lane = transform.position.x >= 0f ? 1f : -1f;
        }

        public void ResetTo(Vector3 position)
        {
            CancelServe();
            transform.position = position;
            Velocity = Vector3.zero;
            reactionTimer = 0f;
            hasPlan = false;
            atNet = false;
            recoverSpot = new Vector3(0f, 0f, Baseline);
            Stamina01 = 1f;
            Exhausted = false;
            IsSprinting = false;
            FaceNet();
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            if (serveState != ServeState.None)
            {
                Velocity = Vector3.zero;
                UpdateServe(dt);
                UpdateStamina(dt);
                return;
            }
            if (FeedOnly)
            {
                Velocity = Vector3.zero;
                SkipRequested = true;
                return;
            }

            SkipRequested = false;
            Vector3 target = transform.position;
            float maxSpeed = RunSpeed;
            bool incoming = ball.IsLive && ball.LastHitterSide == -Side;
            if (incoming)
            {
                reactionTimer += dt;
                if (reactionTimer >= profile.reactionDelay)
                {
                    DecideSmash();
                    SkipRequested = readsOut && WillLandOut();
                    bool mateReceives = ball.IsServe && !swing.MayReturnServe; // 복식: 서브는 정해진 리시버만 받는다
                    if (!SkipRequested && !mateReceives && TryGetSpot(out Vector3 spot, out float seconds) && ShouldChase(spot))
                    {
                        AdjustForSituation(spot, seconds);
                        target = spot + (reactionTimer < misjudgeUntil ? misjudgeOffset : Vector3.zero);
                        maxSpeed = PaceFor(target, seconds);
                    }
                    else if (Doubles && !mateReceives)
                    {
                        target = HomeSpot(); // 팀원이 맡은 공: 내 쪽을 지킨다
                    }
                }
            }
            else
            {
                reactionTimer = 0f;
                if (ball.IsLive && ball.LastHitterSide == Side)
                {
                    // 친 뒤 (복식은 팀원이 쳐도): 네트로 들어가면 뛰고, 아니면 걸어서 돌아가 스테미너를 아낀다
                    target = Doubles ? HomeSpot() : recoverSpot;
                    float recovery = profile.recoverySpeed > 0f ? profile.recoverySpeed : 1f;
                    maxSpeed = atNet ? RunSpeed : Mathf.Min(RunSpeed, WalkSpeed * recovery);
                }
            }
            Move(target, maxSpeed, dt);
            UpdateStamina(dt);
        }

        // 걸어서도 제때 닿으면 걷는다. 늦으면 뛴다 (지치면 걷기가 최고 속도). 뒤로 가는 길이면 그만큼 느리게 본다
        float PaceFor(Vector3 target, float seconds)
        {
            Vector3 to = target - transform.position;
            to.y = 0f;
            float needed = to.magnitude / Mathf.Max(0.05f, seconds - PaceMargin);
            return needed <= Toward(to, WalkSpeed) ? WalkSpeed : RunSpeed;
        }

        // 그 방향으로 낼 수 있는 속도 (뒷걸음은 느리다, 플레이어와 같은 규칙)
        float Toward(Vector3 direction, float speed) => speed * Court.DirectionSpeedScale(direction, Side, config.backwardSpeedScale);

        void Move(Vector3 target, float maxSpeed, float dt)
        {
            Vector3 to = target - transform.position;
            to.y = 0f;
            float distance = to.magnitude;
            maxSpeed = Toward(to, maxSpeed);
            // 미끄러운 바닥에서는 멈추는 데 걸리는 거리만큼 일찍 속도를 줄인다
            float stopSpeed = Mathf.Sqrt(2f * Court.BrakeRate(profile.acceleration, stats.SlideScale) * distance);
            Vector3 desired = distance > 0.02f ? to / distance * Mathf.Min(maxSpeed, distance * 6f, stopSpeed) : Vector3.zero;
            if (swing.IsSwinging && config.swingPlantDeceleration > 0f) // 플레이어와 같은 규칙: 스윙 중에는 발을 멈춘다
                Velocity = Vector3.MoveTowards(Velocity, Vector3.zero, config.swingPlantDeceleration * dt);
            else
                Velocity = Vector3.MoveTowards(Velocity, desired, Court.MoveRate(Velocity, desired, profile.acceleration, stats.SlideScale) * dt);

            Vector3 p = transform.position + Velocity * dt;
            if (Doubles)
            {
                // 팀원과 겹치지 않게 밀려난다 (넘어지거나 실점하지는 않는다)
                Vector3 apart = p - teammate.position;
                apart.y = 0f;
                float gap = apart.magnitude;
                if (gap < MinGap) p = teammate.position + (gap > 1e-3f ? apart / gap : new Vector3(lane, 0f, 0f)) * MinGap;
            }
            p.x = Mathf.Clamp(p.x, -Court.PlayableHalfWidth, Court.PlayableHalfWidth);
            p.z = Mathf.Clamp(p.z * Side, 0.5f, Court.PlayableDepth) * Side;
            p.y = 0f;
            transform.position = p;
        }

        // ── 복식 ──────────────────────────────────────────────

        // 공은 떨어질 자리(같은 궤적이라 팀원 AI도 같은 점을 본다)에 더 가까운 쪽이 맡는다. 맡은 공은 팀원이 HandOver만큼
        // 더 가까워져야 넘기고, 팀원이 맡았으면 내가 그만큼 더 가까울 때만 가져온다. 거리가 같으면 왼쪽 선수(가운데 공을 포핸드로)
        bool ShouldChase(Vector3 spot)
        {
            if (!Doubles) return true;
            Vector3 reference = swing.HasLanding ? swing.LandingPoint : spot;
            float mine = Flat(reference - transform.position), theirs = Flat(reference - teammate.position);
            if (chasing) chasing = mine <= theirs + HandOver;
            else if (mateAi != null && mateAi.chasing) chasing = mine < theirs - HandOver;
            else chasing = mine < theirs || (Mathf.Approximately(mine, theirs) && transform.position.x * Facing < teammate.position.x * Facing);
            return chasing;
        }

        // 공을 맡지 않을 때 설 자리: 내 쪽 절반의 가운데쯤, 네트 앞 담당이면 네트 앞. 공이 있는 쪽으로 조금 치우친다
        Vector3 HomeSpot()
        {
            float half = Court.PlayHalfWidth;
            float ballX = Mathf.Clamp(ball.State.Position.x, -half, half);
            float x = lane * half * 0.45f + ballX * 0.25f * profile.anticipation;
            return new Vector3(Mathf.Clamp(x, -half, half), 0f, atNet ? NetDepth : Baseline);
        }

        static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;

        // 상대 중 네트에 가장 가까운 선수의 깊이 (단식이면 상대 한 명). 네트 앞이면 패싱·로브, 모두 뒤면 드롭샷을 고른다
        float ClosestOpponentDepth()
        {
            float depth = opponent != null ? opponent.position.z * -Side : Court.HalfLength;
            if (opponent2 != null) depth = Mathf.Min(depth, opponent2.position.z * -Side);
            return depth;
        }

        // 복식에서 노릴 곳: 상대 두 명과 가장 먼 방향 (사이 또는 바깥 앨리). -maxAim ~ maxAim
        float GapAim(float maxAim)
        {
            if (opponent == null || opponent2 == null) return AwayFromOpponent() * maxAim;
            float best = 0f, bestGap = -1f;
            for (float a = -maxAim; a <= maxAim + 1e-4f; a += 0.05f)
            {
                float x = a * MaxAimX;
                float gap = Mathf.Min(Mathf.Abs(x - opponent.position.x * Facing), Mathf.Abs(x - opponent2.position.x * Facing));
                if (gap > bestGap + 0.01f || (Mathf.Abs(gap - bestGap) <= 0.01f && Roll(0.5f)))
                {
                    best = a;
                    bestGap = gap;
                }
            }
            return best;
        }

        // 플레이어와 같은 규칙: 걷기보다 빠르면 스테미너를 쓰고, 바닥나면 회복될 때까지 걷는다
        void UpdateStamina(float dt)
        {
            IsSprinting = Velocity.magnitude > WalkSpeed + 0.05f;
            if (IsSprinting)
            {
                float capacitySeconds = config.SprintDistance / (config.runSpeed * stats.MoveScale);
                Stamina01 -= dt / capacitySeconds;
                if (Stamina01 <= 0f)
                {
                    Stamina01 = 0f;
                    Exhausted = true;
                }
            }
            else
            {
                Stamina01 = Mathf.Min(1f, Stamina01 + config.staminaRecoveryPerSecond * stats.RecoveryScale * dt);
                if (Exhausted && Stamina01 >= config.staminaResumeThreshold) Exhausted = false;
            }
        }

        // 스매시 > (네트 앞) 바운드 전 발리 > 바운드 후 편한 타점
        bool TryGetSpot(out Vector3 spot, out float seconds)
        {
            spot = default;
            if (planned.Type == ShotType.Smash && swing.TryGetSmashContact(out Vector3 smash, out seconds))
            {
                spot = swing.SpotFor(smash, config.smashIdealLateral);
                return true;
            }
            if (swing.InVolleyZone)
            {
                if (swing.TryGetVolleyContact(transform.position.z, out Vector3 volley, out seconds))
                {
                    spot = swing.SpotFor(volley);
                    return true;
                }
            }
            else if (atNet)
            {
                // 네트로 들어가는 중: 네트 앞 자리까지 못 가면 서비스라인 바로 안쪽에서라도 발리한다
                foreach (float depth in new[] { NetDepth, (Court.ServiceLine - 0.3f) * Side })
                {
                    if (!swing.TryGetVolleyContact(depth, out Vector3 volley, out seconds)) continue;
                    Vector3 volleySpot = swing.SpotFor(volley);
                    Vector3 to = volleySpot - transform.position;
                    to.y = 0f;
                    if (to.magnitude > Toward(to, RunSpeed) * seconds) continue;
                    spot = volleySpot;
                    return true;
                }
            }
            if (swing.TryGetIdealContact(out Vector3 contact, out seconds))
            {
                spot = swing.SpotFor(contact);
                return true;
            }
            return false;
        }

        bool WillLandOut()
        {
            if (!swing.HasLanding) return false;
            Vector3 p = swing.LandingPoint;
            if (Court.SideOf(p.z) != Side) return false;
            return ball.IsServe ? !Court.IsInServiceBox(p, Side, ball.ServeToDeuce) : !Court.IsInPlay(p, Side);
        }

        // 높이 뜬 공(로브)이면 스매시로 바꾼다. 궤적을 읽은 뒤 한 번만 정한다.
        void DecideSmash()
        {
            if (smashDecided || swing.PredictedApex <= 0f) return;
            smashDecided = true;
            if (swing.PredictedApex >= SmashApex && swing.TryGetSmashContact(out _))
            {
                planned.Type = ShotType.Smash;
                planned.Aim = ChooseAim(0.8f);
                planned.Depth = 1f;
            }
        }

        // ── 친 뒤 위치 ────────────────────────────────────────

        // 내가 공을 친 직후 (복식에서 팀원이 친 공은 해당 없음)
        void OnOwnShot()
        {
            Vector3 contact = ball.State.Position;
            SurfaceProfile court = Court.Surface;

            // 짧은 공을 치고 들어가거나, 서브 앤 발리. 네트 앞에서 발리했으면 그대로 네트에 남는다.
            bool volleyed = Mathf.Abs(contact.z) <= Court.ServiceLine;
            if (ball.IsServe)
                atNet = Roll(profile.serveVolleyChance + court.aiServeVolleyBonus);
            else if (volleyed)
                atNet = true;
            else if (Mathf.Abs(contact.z) < Court.HalfLength - 0.5f)
                atNet = Roll(profile.netRushChance + court.aiNetRushBonus);
            else
                atNet = false;

            // 내가 보낸 공이 떨어질 쪽으로 치우쳐, 상대가 칠 수 있는 각도의 가운데에 선다
            outgoing.Simulate(ball.State, Court.Surface);
            float landingX = outgoing.FirstBounceIndex >= 0 ? outgoing.FirstBouncePoint.x : 0f;
            float shade = landingX * 0.4f * profile.anticipation;
            recoverSpot = new Vector3(Mathf.Clamp(shade, -Court.PlayHalfWidth, Court.PlayHalfWidth), 0f,
                atNet ? NetDepth : Baseline);
        }

        // ── IShotIntentSource ─────────────────────────────────

        public bool TryGetIntent(out ShotIntent intent)
        {
            intent = planned;
            return hasPlan;
        }

        public void OnSwingCommitted() => hasPlan = false;

        public void OnNewIncomingBall()
        {
            planned = swing.InVolleyZone ? PickVolley() : PickShot();
            hasPlan = true;
            smashDecided = false;
            tacticDecided = false;
            chasing = false;
            reactionTimer = 0f;

            bool misjudge = Roll(profile.misjudgeChance);
            readsOut = !misjudge;
            misjudgeOffset = Vector3.zero;
            misjudgeUntil = 0f;
            if (misjudge)
            {
                float sign = rng.NextDouble() < 0.5 ? -1f : 1f;
                misjudgeOffset = new Vector3(sign * Range(0.8f, 1.6f), 0f, Range(-1f, 1f));
                misjudgeUntil = profile.reactionDelay + Range(0.35f, 0.7f);
            }
        }

        ShotIntent PickShot()
        {
            var intent = new ShotIntent { Type = ShotType.Topspin, Depth = 1f, Aim = ChooseAim(0.75f) };
            bool opponentAtNet = ClosestOpponentDepth() < Court.ServiceLine + 0.5f; // 복식은 한 명이라도
            SurfaceProfile court = Court.Surface;

            if (opponentAtNet)
            {
                if (Roll(Mathf.Min(0.9f, profile.lobVsNetChance * character.lobTendency)))
                {
                    intent.Type = ShotType.Lob;
                    intent.Depth = Range(0.5f, 1f);
                    intent.Aim = ChooseAim(0.5f);
                }
                else
                {
                    intent.Aim = Doubles ? GapAim(0.95f) : AwayFromOpponent() * Range(0.6f, 0.95f); // 패싱샷
                }
                return intent;
            }

            // 평범한 랠리: 탑스핀 위주에 가끔 슬라이스. 로브·드롭샷·수비 슬라이스는 공을 읽은 뒤 상황을 보고 정한다 (AdjustForSituation).
            if (Roll(Mathf.Max(0f, (profile.sliceChance * character.sliceTendency + court.aiSliceBonus) * 0.5f)))
            {
                intent.Type = ShotType.Slice;
                intent.Depth = Range(0.7f, 1f);
            }
            return intent;
        }

        // 타점과 남은 시간을 안 뒤 한 번: 몰렸으면 수비 샷, 짧은 공이면 드롭샷
        void AdjustForSituation(Vector3 spot, float seconds)
        {
            if (tacticDecided || !hasPlan || planned.Type == ShotType.Smash || swing.InVolleyZone) return;
            tacticDecided = true;
            SurfaceProfile court = Court.Surface;

            Vector3 to = spot - transform.position;
            to.y = 0f;
            float pressure = to.magnitude / Mathf.Max(0.05f, seconds - PaceMargin) / Mathf.Max(0.1f, Toward(to, RunSpeed));
            if (pressure >= 0.85f)
            {
                // 겨우 닿는 공: 높고 깊은 로브로 시간을 벌거나, 낮은 슬라이스로 버틴다
                if (Roll(Mathf.Clamp01((profile.lobChance * 3f + court.aiLobBonus) * character.lobTendency)))
                {
                    planned.Type = ShotType.Lob;
                    planned.Depth = 1f;
                    planned.Aim = ChooseAim(0.4f);
                }
                else if (Roll(Mathf.Clamp01((profile.sliceChance * 3f + court.aiSliceBonus) * character.sliceTendency)))
                {
                    planned.Type = ShotType.Slice;
                    planned.Depth = 1f;
                }
                return;
            }

            // 베이스라인 안쪽까지 들어온 짧은 공 + 상대가 뒤에 있음: 드롭샷 기회
            float contactDepth = Mathf.Abs(spot.z);
            if (contactDepth < Court.HalfLength - 2f && ClosestOpponentDepth() > Court.HalfLength - 1f && // 상대가 모두 뒤에
                Roll(Mathf.Clamp01(profile.dropShotChance * 4f * character.dropShotTendency + court.aiDropShotBonus)))
            {
                planned.Type = ShotType.Slice;
                planned.Depth = 0f;
                planned.Aim = ChooseAim(0.6f);
            }
        }

        // 네트 앞: 빈 곳으로 기본 발리, 상대가 뒤에 있으면 가끔 드롭 발리, 가끔 깊은 발리
        ShotIntent PickVolley()
        {
            float aim = Doubles ? GapAim(0.9f) : AwayFromOpponent() * Range(0.5f, 0.9f);
            var intent = new ShotIntent { Type = ShotType.Topspin, Depth = 1f, Aim = aim };
            double roll = rng.NextDouble();
            if (ClosestOpponentDepth() > Court.HalfLength - 1f && roll < 0.2 * character.dropShotTendency)
                intent.Type = ShotType.Slice;
            else if (roll > 0.9)
                intent.Type = ShotType.Lob;
            return intent;
        }

        // smartAim 확률로 상대가 없는 쪽, 아니면 무작위
        float ChooseAim(float maxAim)
        {
            maxAim = Mathf.Min(0.95f, maxAim * character.aimBoldness);
            if (Roll(profile.smartAim))
                return Doubles ? GapAim(maxAim) : AwayFromOpponent() * Range(Mathf.Min(0.4f, maxAim), maxAim);
            return Range(-maxAim, maxAim);
        }

        // 내 기준 왼쪽(-1)/오른쪽(+1) 중 상대가 없는 쪽
        float AwayFromOpponent()
        {
            if (opponent == null) return rng.NextDouble() < 0.5 ? -1f : 1f;
            float opponentAim = opponent.position.x * Facing / MaxAimX;
            if (Mathf.Abs(opponentAim) < 0.1f) return rng.NextDouble() < 0.5 ? -1f : 1f;
            return opponentAim > 0f ? -1f : 1f;
        }

        // ── 서브 ──────────────────────────────────────────────

        public void BeginServe(bool toDeuce, bool secondServe)
        {
            serveState = ServeState.Waiting;
            serveTimer = Range(0.8f, 1.4f);
            serveDeuce = toDeuce;
            serveSecond = secondServe;
            Velocity = Vector3.zero;
            swing.PrepareServe(true);
            ball.Hold(swing.ServeHandPosition);
        }

        public void CancelServe()
        {
            if (serveState == ServeState.None) return;
            serveState = ServeState.None;
            swing.PrepareServe(false);
        }

        void UpdateServe(float dt)
        {
            if (serveState == ServeState.Waiting)
            {
                ball.Hold(swing.ServeHandPosition);
                serveTimer -= dt;
                if (serveTimer > 0f) return;
                serveState = ServeState.Tossing;
                releaseHeight = Mathf.Max(config.serveHitMinHeight + 0.1f,
                    config.serveIdealHeight + Gaussian() * profile.serveTimingError);
                ball.Toss(swing.ServeHandPosition, ServeTiming.TossSpeed(config));
                return;
            }

            if (!ball.IsToss)
            {
                CancelServe(); // 누군가 공을 잡았다 (포인트 리셋)
                return;
            }
            if (ball.State.Velocity.y >= 0f || ball.State.Position.y > releaseHeight) return;

            serveState = ServeState.None;
            float quality = ServeTiming.Quality(config, ball.State.Position.y);
            swing.HitServe(ChooseServeType(), ChooseServeAim(), quality, serveSecond, serveDeuce);
        }

        ServeType ChooseServeType()
        {
            if (serveSecond) return Roll(0.7f) ? ServeType.Kick : ServeType.Slice;
            if (Roll(Mathf.Clamp01(profile.firstServeFlatChance + character.flatServeBias))) return ServeType.Flat;
            return Roll(0.5f) ? ServeType.Slice : ServeType.Kick;
        }

        // 첫 서브는 양 코너와 몸쪽, 두 번째 서브는 박스 가운데 쪽
        float ChooseServeAim()
        {
            if (serveSecond) return Range(-0.4f, 0.4f);
            double roll = rng.NextDouble();
            if (roll < 0.2) return Range(-0.15f, 0.15f);
            float corner = Range(0.65f, 0.95f);
            return roll < 0.6 ? -corner : corner;
        }

        // ── 연습 모드 ─────────────────────────────────────────

        public void Feed() => Feed(new ShotIntent
        {
            Type = ShotType.Topspin,
            Aim = Range(-0.6f, 0.6f),
            Depth = 1f,
            SpeedScale = config.feedSpeedScale,
        });

        // 정해 준 구질로 공을 준다 (튜토리얼)
        public void Feed(ShotIntent intent) => swing.Feed(intent);

        bool Roll(float chance) => rng.NextDouble() < chance;

        float Range(float min, float max) => min + (max - min) * (float)rng.NextDouble();

        float Gaussian()
        {
            double u1 = 1.0 - rng.NextDouble();
            double u2 = rng.NextDouble();
            return (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2));
        }

        void FaceNet() => transform.rotation = Quaternion.LookRotation(new Vector3(0f, 0f, Facing));
    }
}
