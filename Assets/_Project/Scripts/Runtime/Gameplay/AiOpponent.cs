using System;
using UnityEngine;

namespace FirstPersonTennis
{
    // 단식 AI: 공을 읽고 타점으로 가서, 상대 위치를 보고 구질과 방향을 고른다. 서브도 넣는다.
    // 실력 차이는 난이도 프로필(반응, 이동, 오차, 샷 선택)로만 만든다. 타격 판정은 플레이어와 같은 AutoSwing.
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

        public AiDifficulty Difficulty { get; private set; }
        public AiProfile Profile => profile;
        public Vector3 Velocity { get; private set; }
        public bool Exhausted => false;
        public bool SkipRequested { get; private set; }
        public bool Serving => serveState != ServeState.None;
        public Vector3 FeedContact => swing.FeedContact;
        public int Side => swing.Side;

        enum ServeState { None, Waiting, Tossing }

        AiProfile profile;
        System.Random rng;
        ShotIntent planned;
        bool hasPlan;
        bool smashDecided;
        bool readsOut;           // 이번 공이 아웃인지 제대로 읽는지
        float reactionTimer;
        Vector3 misjudgeOffset;
        float misjudgeUntil;

        ServeState serveState;
        float serveTimer;
        float releaseHeight;
        bool serveDeuce, serveSecond;

        float Facing => Court.Facing(Side);
        float MaxAimX => Mathf.Max(0.5f, Court.SinglesHalfWidth - config.aimMargin);
        Vector3 Home => new Vector3(0f, 0f, (Court.HalfLength + 0.8f) * Side);

        void Awake()
        {
            rng = new System.Random(randomSeed);
            SetDifficulty(config.defaultDifficulty);
        }

        void Start() => FaceNet();

        public void SetDifficulty(AiDifficulty difficulty)
        {
            Difficulty = difficulty;
            profile = config.AiProfileFor(difficulty);
            swing.ErrorScale = profile.errorScale;
        }

        public void ResetTo(Vector3 position)
        {
            CancelServe();
            transform.position = position;
            Velocity = Vector3.zero;
            reactionTimer = 0f;
            hasPlan = false;
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
                return;
            }

            SkipRequested = false;
            Vector3 target = transform.position;
            float maxSpeed = profile.speed;
            bool incoming = ball.IsLive && ball.LastHitterSide == -Side;
            if (incoming)
            {
                reactionTimer += dt;
                if (reactionTimer >= profile.reactionDelay)
                {
                    DecideSmash();
                    SkipRequested = readsOut && WillLandOut();
                    if (!SkipRequested && TryGetSpot(out Vector3 spot))
                        target = spot + (reactionTimer < misjudgeUntil ? misjudgeOffset : Vector3.zero);
                }
            }
            else
            {
                reactionTimer = 0f;
                if (ball.IsLive && ball.LastHitterSide == Side)
                {
                    target = Home; // 친 뒤에는 베이스라인 가운데로 돌아간다
                    maxSpeed = profile.speed * 0.75f;
                }
            }
            Move(target, maxSpeed, dt);
        }

        void Move(Vector3 target, float maxSpeed, float dt)
        {
            Vector3 to = target - transform.position;
            to.y = 0f;
            float distance = to.magnitude;
            Vector3 desired = distance > 0.02f ? to / distance * Mathf.Min(maxSpeed, distance * 6f) : Vector3.zero;
            Velocity = Vector3.MoveTowards(Velocity, desired, profile.acceleration * dt);

            Vector3 p = transform.position + Velocity * dt;
            p.x = Mathf.Clamp(p.x, -Court.PlayableHalfWidth, Court.PlayableHalfWidth);
            p.z = Mathf.Clamp(p.z * Side, 0.5f, Court.PlayableDepth) * Side;
            p.y = 0f;
            transform.position = p;
        }

        bool TryGetSpot(out Vector3 spot)
        {
            spot = default;
            if (planned.Type == ShotType.Smash && swing.TryGetSmashContact(out Vector3 smash))
            {
                spot = swing.SpotFor(smash, config.smashIdealLateral);
                return true;
            }
            if (swing.TryGetIdealContact(out Vector3 contact))
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

        // ── IShotIntentSource ─────────────────────────────────

        public bool TryGetIntent(out ShotIntent intent)
        {
            intent = planned;
            return hasPlan;
        }

        public void OnSwingCommitted() => hasPlan = false;

        public void OnNewIncomingBall()
        {
            planned = PickShot();
            hasPlan = true;
            smashDecided = false;
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

            if (opponentAtNet)
            {
                if (Roll(profile.lobVsNetChance))
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

            double roll = rng.NextDouble();
            float drop = opponentDeep ? profile.dropShotChance : 0f;
            if (roll < drop)
            {
                intent.Type = ShotType.Slice;
                intent.Depth = 0f;
                intent.Aim = ChooseAim(0.6f);
            }
            else if (roll < drop + profile.sliceChance)
            {
                intent.Type = ShotType.Slice;
                intent.Depth = Range(0.6f, 1f);
            }
            else if (roll < drop + profile.sliceChance + profile.lobChance)
            {
                intent.Type = ShotType.Lob;
                intent.Depth = Range(0.4f, 1f);
            }
            return intent;
        }

        // smartAim 확률로 상대가 없는 쪽, 아니면 무작위
        float ChooseAim(float maxAim)
        {
            if (Roll(profile.smartAim)) return AwayFromOpponent() * Range(0.4f, maxAim);
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
            if (Roll(profile.firstServeFlatChance)) return ServeType.Flat;
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
