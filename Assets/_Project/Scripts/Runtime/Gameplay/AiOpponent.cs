using System;
using UnityEngine;

namespace FirstPersonTennis
{
    // 단식 AI: 공을 읽고 타점으로 가서, 상대 위치를 보고 구질과 방향을 고른다. 서브도 넣는다.
    // 실력 차이는 난이도 프로필(반응, 이동, 오차, 샷 선택, 위치 선정)로 만들고, 스테미너·타격 판정은 플레이어와 같은 규칙.
    // 가까운 쪽(side -1)에 두면 AI끼리 대결(관전, 밸런스 측정)도 한다.
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
        public Vector3 FeedContact => swing.FeedContact;
        public int Side => swing.Side;

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

        ServeState serveState;
        float serveTimer;
        float releaseHeight;
        bool serveDeuce, serveSecond;

        float Facing => Court.Facing(Side);
        float MaxAimX => Mathf.Max(0.5f, Court.SinglesHalfWidth - config.aimMargin);
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

        void OnEnable() => ball.Launched += OnBallLaunched;
        void OnDisable() => ball.Launched -= OnBallLaunched;

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

        public void SetOpponent(Transform target) => opponent = target;

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
                    if (!SkipRequested && TryGetSpot(out Vector3 spot, out float seconds))
                    {
                        AdjustForSituation(spot, seconds);
                        target = spot + (reactionTimer < misjudgeUntil ? misjudgeOffset : Vector3.zero);
                        maxSpeed = PaceFor(target, seconds);
                    }
                }
            }
            else
            {
                reactionTimer = 0f;
                if (ball.IsLive && ball.LastHitterSide == Side)
                {
                    // 친 뒤: 네트로 들어가면 뛰고, 아니면 걸어서 돌아가 스테미너를 아낀다
                    target = recoverSpot;
                    float recovery = profile.recoverySpeed > 0f ? profile.recoverySpeed : 1f;
                    maxSpeed = atNet ? RunSpeed : Mathf.Min(RunSpeed, WalkSpeed * recovery);
                }
            }
            Move(target, maxSpeed, dt);
            UpdateStamina(dt);
        }

        // 걸어서도 제때 닿으면 걷는다. 늦으면 뛴다 (지치면 걷기가 최고 속도).
        float PaceFor(Vector3 target, float seconds)
        {
            Vector3 to = target - transform.position;
            to.y = 0f;
            float needed = to.magnitude / Mathf.Max(0.05f, seconds - PaceMargin);
            return needed <= WalkSpeed ? WalkSpeed : RunSpeed;
        }

        void Move(Vector3 target, float maxSpeed, float dt)
        {
            Vector3 to = target - transform.position;
            to.y = 0f;
            float distance = to.magnitude;
            // 미끄러운 바닥에서는 멈추는 데 걸리는 거리만큼 일찍 속도를 줄인다
            float stopSpeed = Mathf.Sqrt(2f * Court.BrakeRate(profile.acceleration, stats.SlideScale) * distance);
            Vector3 desired = distance > 0.02f ? to / distance * Mathf.Min(maxSpeed, distance * 6f, stopSpeed) : Vector3.zero;
            Velocity = Vector3.MoveTowards(Velocity, desired, Court.MoveRate(Velocity, desired, profile.acceleration, stats.SlideScale) * dt);

            Vector3 p = transform.position + Velocity * dt;
            p.x = Mathf.Clamp(p.x, -Court.PlayableHalfWidth, Court.PlayableHalfWidth);
            p.z = Mathf.Clamp(p.z * Side, 0.5f, Court.PlayableDepth) * Side;
            p.y = 0f;
            transform.position = p;
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
                    if (to.magnitude > RunSpeed * seconds) continue;
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
            return ball.IsServe ? !Court.IsInServiceBox(p, Side, ball.ServeToDeuce) : !Court.IsInSingles(p, Side);
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

        void OnBallLaunched(int hitter)
        {
            if (hitter != Side) return;
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
            recoverSpot = new Vector3(Mathf.Clamp(shade, -Court.SinglesHalfWidth, Court.SinglesHalfWidth), 0f,
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
            float opponentDepth = opponent != null ? opponent.position.z * -Side : Court.HalfLength;
            bool opponentAtNet = opponentDepth < Court.ServiceLine + 0.5f;
            bool opponentDeep = opponentDepth > Court.HalfLength - 1f;
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
                    intent.Aim = AwayFromOpponent() * Range(0.6f, 0.95f); // 패싱샷
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
            float pressure = to.magnitude / Mathf.Max(0.05f, seconds - PaceMargin) / Mathf.Max(0.1f, RunSpeed);
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
            float opponentDepth = opponent != null ? opponent.position.z * -Side : Court.HalfLength;
            if (contactDepth < Court.HalfLength - 2f && opponentDepth > Court.HalfLength - 1f &&
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
            var intent = new ShotIntent { Type = ShotType.Topspin, Depth = 1f, Aim = AwayFromOpponent() * Range(0.5f, 0.9f) };
            float opponentDepth = opponent != null ? opponent.position.z * -Side : Court.HalfLength;
            double roll = rng.NextDouble();
            if (opponentDepth > Court.HalfLength - 1f && roll < 0.2 * character.dropShotTendency)
                intent.Type = ShotType.Slice;
            else if (roll > 0.9)
                intent.Type = ShotType.Lob;
            return intent;
        }

        // smartAim 확률로 상대가 없는 쪽, 아니면 무작위
        float ChooseAim(float maxAim)
        {
            maxAim = Mathf.Min(0.95f, maxAim * character.aimBoldness);
            if (Roll(profile.smartAim)) return AwayFromOpponent() * Range(Mathf.Min(0.4f, maxAim), maxAim);
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

        public void Feed()
        {
            var intent = new ShotIntent
            {
                Type = ShotType.Topspin,
                Aim = Range(-0.6f, 0.6f),
                Depth = 1f,
                SpeedScale = config.feedSpeedScale,
            };
            swing.Feed(intent);
        }

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
