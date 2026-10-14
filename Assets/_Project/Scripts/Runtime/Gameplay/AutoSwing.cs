using System;
using UnityEngine;

namespace FirstPersonTennis
{
    // 한 번 친 공의 타구 품질. 항목별 벌점을 더한 Badness가 클수록 방향 오차가 커진다.
    public struct ShotQuality
    {
        public const float GoodBelow = 0.2f;
        public const float FairBelow = 0.45f;

        public float Badness;   // 0 = 완벽 ~ 1.5
        public float Reach;     // 몸과 공의 좌우 거리
        public bool TooClose;
        public float Height;    // 타점 높이
        public bool TooLow;
        public float Running;   // 뛰면서 침
        public float Tired;     // 스테미너 바닥
        public float Aim;       // 좌우 방향을 크게 줌
        public float BallSpeed; // 빠르게 온 공
        public bool IsServe;
        public float Timing;    // 서브 타이밍 (1 - 타이밍 품질)
        public bool Early;      // 서브: 공이 이상적 높이보다 높을 때 침

        public bool IsGood => Badness < GoodBelow;
        public bool IsFair => Badness >= GoodBelow && Badness < FairBelow;
    }

    // 선입력이 있고 공이 스윙 범위로 들어오면, 임팩트 시각에 맞춰 스윙을 자동 실행한다.
    // 바운드 후 스트로크, 바운드 전 발리, 머리 위 스매시, 서브 타격을 맡는다. 플레이어와 AI가 같이 쓴다.
    [DefaultExecutionOrder(-5)]
    public class AutoSwing : MonoBehaviour
    {
        [SerializeField] TennisConfig config;
        [SerializeField] TennisBall ball;
        [SerializeField] int side = -1;
        [SerializeField] RacketSwingVisual racket;
        [Tooltip("IShotIntentSource를 구현한 컴포넌트")]
        [SerializeField] MonoBehaviour intentSource;
        [SerializeField] float errorScale = 1f;
        [SerializeField] int randomSeed = 1;

        public int Side => side;
        public float ErrorScale { get => errorScale; set => errorScale = value; }
        public bool IsSwinging { get; private set; }
        public bool HasContact { get; private set; }
        public Vector3 ContactPoint { get; private set; }
        public bool ContactInHitZone { get; private set; }
        public bool ContactInSwingZone { get; private set; }
        public bool ContactIsVolley { get; private set; }
        public float TimeToContact { get; private set; }
        public StrokeSide PredictedStroke { get; private set; }
        public bool HasLanding { get; private set; }
        public Vector3 LandingPoint { get; private set; }
        public float PredictedApex { get; private set; } // 남은 궤적의 최고 높이

        // 네트~서비스라인 사이: 바운드 전에 닿으면 발리
        public bool InVolleyZone => transform.position.z * side <= Court.ServiceLine;

        public event Action<bool> SwingResolved; // true = 맞힘, false = 헛스윙
        public event Action<ShotQuality> Struck;  // 친 공의 타구 품질 (서브 포함)

        IShotIntentSource source;
        IMover mover;
        Trajectory trajectory;
        System.Random rng;
        long impactStep;
        ShotIntent lockedIntent;
        bool lockedVolley;
        bool servePrep;

        float Facing => Court.Facing(side);

        // 공 물리 스텝 수 → 실제 시간(초). 공은 자기 템포(공 템포 × 샷 배율)로 진행한다.
        float RealSeconds(long steps) => steps * BallPhysics.Step / ball.TimeScale;
        bool Incoming => ball.IsLive && ball.LastHitterSide == -side;

        void Awake()
        {
            source = intentSource as IShotIntentSource;
            mover = GetComponent<IMover>();
            trajectory = new Trajectory(1500);
            rng = new System.Random(randomSeed);
        }

        void OnEnable()
        {
            ball.BeforeStep += OnBeforeStep;
            ball.Launched += OnLaunched;
        }

        void OnDisable()
        {
            ball.BeforeStep -= OnBeforeStep;
            ball.Launched -= OnLaunched;
        }

        void Update()
        {
            HasContact = HasLanding = ContactInHitZone = ContactInSwingZone = ContactIsVolley = false;
            if (!Incoming)
            {
                PredictedApex = 0f;
                if (racket != null && !IsSwinging)
                    racket.SetPreparation(StrokeSide.Forehand, servePrep ? ShotType.Smash : ShotType.Topspin, servePrep ? 1f : 0f);
                return;
            }

            trajectory.Simulate(ball.State, config.surface);
            if (ball.BouncesSinceHit == 0 && trajectory.FirstBounceIndex >= 0)
            {
                HasLanding = true;
                LandingPoint = trajectory.FirstBouncePoint;
            }
            float apex = 0f;
            for (int k = 0; k < trajectory.Count; k++) apex = Mathf.Max(apex, trajectory.Positions[k].y);
            PredictedApex = apex;

            if (IsSwinging)
            {
                TimeToContact = RealSeconds(impactStep - ball.StepIndex);
                if (ball.StepIndex > impactStep)
                {
                    IsSwinging = false; // 임팩트 스텝을 놓쳤다 (포인트 리셋 등)
                    SwingResolved?.Invoke(false);
                }
                return;
            }

            ShotIntent intent = default;
            bool hasIntent = source != null && source.TryGetIntent(out intent);
            bool skip = source != null && source.SkipRequested;
            if (!hasIntent && !skip && config.autoStrokeWhenNoInput)
            {
                intent = ShotIntent.Default;
                hasIntent = true;
            }
            if (skip) hasIntent = false;
            bool smash = hasIntent && intent.Type == ShotType.Smash;

            if (!FindContact(smash, out int index, out bool volley))
            {
                if (racket != null) racket.SetPreparation(PredictedStroke, ShotType.Topspin, 0f);
                return;
            }

            Vector3 point = trajectory.Positions[index];
            HasContact = true;
            ContactPoint = point;
            ContactIsVolley = volley;
            TimeToContact = RealSeconds(index);
            Evaluate(point, smash, out bool inHit, out bool inSwing, out float lateral);
            ContactInHitZone = inHit;
            ContactInSwingZone = inSwing;
            PredictedStroke = lateral >= 0f ? StrokeSide.Forehand : StrokeSide.Backhand;

            if (racket != null)
            {
                float prep = hasIntent && inSwing ? 1f - TimeToContact / config.takeBackLead : 0f;
                racket.SetPreparation(PredictedStroke, VisualType(intent.Type, volley), prep);
            }

            int windupSteps = Mathf.CeilToInt(config.swingWindup * ball.TimeScale / BallPhysics.Step);
            if (hasIntent && inSwing && index >= 1 && index <= windupSteps)
                StartSwing(intent, volley, ball.StepIndex + index, TimeToContact);
        }

        // ── 서브 ──────────────────────────────────────────────

        // 포인트를 새로 시작할 때: 예약된 스윙을 조용히 취소한다 (헛스윙으로 치지 않음)
        public void CancelSwing()
        {
            IsSwinging = false;
            servePrep = false;
        }

        // 토스하는 동안 라켓을 머리 위로 들어 둔다
        public void PrepareServe(bool on) => servePrep = on;

        public Vector3 ServeHandPosition => transform.position + new Vector3(0.35f * Facing, config.serveHandHeight, 0.3f * Facing);

        // 토스한 공을 지금 위치에서 친다. quality: 타이밍 품질 0~1
        public void HitServe(ServeType type, float aim, float quality, bool secondServe, bool toDeuce)
        {
            servePrep = false;
            Vector3 contact = ball.State.Position;
            ShotSolution solution = ShotPlanner.PlanServe(config, type, aim, contact, side, toDeuce, secondServe);
            Vector3 velocity = solution.Velocity;
            Vector3 spin = solution.Spin;

            float bad = 1f - Mathf.Clamp01(quality);
            float scale = errorScale * (secondServe ? config.secondServeErrorScale : 1f);
            float sigma = (config.serveBaseError + bad * config.serveErrorPerBad) * scale;
            ApplyAimError(ref velocity, ref spin, Gaussian() * sigma, Gaussian() * sigma);
            velocity *= Mathf.Lerp(0.85f, 1f, quality); // 타이밍이 나쁘면 힘도 빠진다

            if (racket != null) racket.PlaySwing(StrokeSide.Forehand, ShotType.Smash, 0.04f, config.followThrough);
            ball.LaunchServe(side, velocity, spin, toDeuce);
            SwingResolved?.Invoke(true);
            Struck?.Invoke(new ShotQuality
            {
                IsServe = true,
                Timing = bad,
                Early = contact.y > config.serveIdealHeight,
                Badness = bad,
            });
        }

        // ── 연습 모드 ─────────────────────────────────────────

        // 연습 모드에서 포인트를 시작할 때: 자기 앞에서 공을 띄워 친다.
        public void Feed(ShotIntent intent)
        {
            Vector3 contact = FeedContact;
            ball.Hold(contact);
            if (racket != null) racket.PlaySwing(StrokeSide.Forehand, intent.Type, 0.05f, config.followThrough);
            Strike(intent, contact, config.idealLateral, false, false);
        }

        public Vector3 FeedContact => transform.position +
            new Vector3(config.idealLateral * Facing, config.idealHeight, config.contactForward * Facing);

        // ── 위치 잡기 (AI가 쓴다) ────────────────────────────

        // 바운드 후 편하게 칠 수 있는 지점 (허리 높이로 내려오는 순간, 낮게 튀면 정점).
        // 높이 튄 깊은 공이라 그 자리가 물러설 수 있는 곳보다 멀면, 닿는 곳에서 칠 수 있는 가장 늦은 타점.
        public bool TryGetIdealContact(out Vector3 point)
        {
            point = default;
            if (!Incoming || !TryGetWindow(out int start, out int end)) return false;

            int apex = start, ideal = -1;
            for (int k = start + 1; k <= end; k++)
            {
                Vector3 p = trajectory.Positions[k];
                if (p.y > trajectory.Positions[apex].y) apex = k;
                else if (k > apex && p.y <= config.idealHeight + 0.15f)
                {
                    ideal = k;
                    break;
                }
            }
            if (ideal < 0) ideal = apex;

            if (!CanStandFor(trajectory.Positions[ideal]))
            {
                for (int k = ideal - 1; k >= start; k--)
                {
                    Vector3 p = trajectory.Positions[k];
                    if (CanStandFor(p) && p.y >= config.minHitHeight && p.y <= config.maxHitHeight)
                    {
                        ideal = k;
                        break;
                    }
                }
            }
            point = trajectory.Positions[ideal];
            return true;
        }

        // 이 타점을 치려고 설 자리가 코트 뒤 이동 한계 안인지
        bool CanStandFor(Vector3 contact) => SpotFor(contact).z * side <= Court.PlayableDepth - 0.05f;

        // 스매시하기 좋은 지점: 공이 머리 위 이상적 높이로 내려오는 순간 (바운드 전 내 코트 위, 아니면 바운드 후)
        public bool TryGetSmashContact(out Vector3 point)
        {
            point = default;
            if (!Incoming || ball.IsServe) return false;
            float height = config.smashIdealHeight;
            if (ball.BouncesSinceHit == 0)
            {
                int beforeBounce = trajectory.FirstBounceIndex >= 0 ? trajectory.FirstBounceIndex : trajectory.Count - 1;
                if (FindDescendingCrossing(1, beforeBounce, height, out point) && Court.SideOf(point.z) == side) return true;
            }
            return TryGetWindow(out int start, out int end) && FindDescendingCrossing(start + 1, end, height, out point);
        }

        // 공이 이상적 타점에 오도록 서야 할 위치
        public Vector3 SpotFor(Vector3 contact) => SpotFor(contact, config.idealLateral);

        public Vector3 SpotFor(Vector3 contact, float lateral) =>
            new Vector3(contact.x - lateral * Facing, 0f, contact.z - config.contactForward * Facing);

        // ── 내부 ──────────────────────────────────────────────

        void OnLaunched(int hitter)
        {
            if (hitter == -side) source?.OnNewIncomingBall();
            if (hitter != side) IsSwinging = false;
        }

        void OnBeforeStep(long step)
        {
            if (!IsSwinging || step != impactStep) return;
            IsSwinging = false;

            Vector3 contact = ball.State.Position;
            Evaluate(contact, lockedIntent.Type == ShotType.Smash, out bool inHit, out _, out float lateral);
            bool hit = Incoming && inHit;
            if (hit) Strike(lockedIntent, contact, lateral, true, lockedVolley);
            SwingResolved?.Invoke(hit);
        }

        void StartSwing(ShotIntent intent, bool volley, long step, float timeToImpact)
        {
            IsSwinging = true;
            impactStep = step;
            lockedIntent = intent;
            lockedVolley = volley;
            source?.OnSwingCommitted();
            if (racket != null) racket.PlaySwing(PredictedStroke, VisualType(intent.Type, volley), timeToImpact, config.followThrough);
        }

        static ShotType VisualType(ShotType type, bool volley) =>
            volley && type != ShotType.Smash ? ShotType.Volley : type;

        void Strike(ShotIntent intent, Vector3 contact, float lateral, bool withError, bool volley)
        {
            ShotSolution solution = volley
                ? ShotPlanner.PlanVolley(config, intent, contact, side)
                : ShotPlanner.Plan(config, intent, contact, side);
            Vector3 velocity = solution.Velocity;
            Vector3 spin = solution.Spin;

            ShotQuality quality = default;
            if (withError)
            {
                quality = Assess(contact, lateral, intent);
                float bad = quality.Badness;
                float elevation = Gaussian() * (config.baseElevationError + bad * config.elevationErrorPerBad) * errorScale;
                float azimuth = Gaussian() * (config.baseAzimuthError + bad * config.azimuthErrorPerBad) * errorScale;
                ApplyAimError(ref velocity, ref spin, azimuth, elevation);
            }

            // 머리 위에서 친 스매시는 빠르게 날아간다
            bool overhead = intent.Type == ShotType.Smash && contact.y >= config.smashMinHeight;
            ball.Launch(side, velocity, spin, overhead ? config.smashTempoBoost : 1f);
            if (withError) Struck?.Invoke(quality);
        }

        static void ApplyAimError(ref Vector3 velocity, ref Vector3 spin, float azimuth, float elevation)
        {
            Quaternion yaw = Quaternion.AngleAxis(azimuth, Vector3.up);
            velocity = yaw * velocity;
            spin = yaw * spin;
            Vector3 axis = Vector3.Cross(Vector3.up, new Vector3(velocity.x, 0f, velocity.z));
            if (axis.sqrMagnitude > 1e-6f) velocity = Quaternion.AngleAxis(-elevation, axis.normalized) * velocity;
        }

        // 타구 품질: 항목별 벌점과 합계 (0 = 완벽, 클수록 오차가 커진다)
        ShotQuality Assess(Vector3 contact, float lateral, ShotIntent intent)
        {
            // 스매시는 머리 위가 이상적 타점이다 (낮은 공에 스매시를 입력하면 플랫 스트로크라 허리 높이 기준)
            bool overhead = intent.Type == ShotType.Smash && contact.y >= config.smashMinHeight;
            float idealHeight = overhead ? config.smashIdealHeight : config.idealHeight;
            float idealLateral = overhead ? config.smashIdealLateral : config.idealLateral;
            float lateralError = Mathf.Abs(Mathf.Abs(lateral) - idealLateral) / config.hitReach;
            float heightError = overhead ? Mathf.Abs(contact.y - idealHeight) / 1.5f : StrokeHeightError(contact.y);
            var q = new ShotQuality
            {
                Reach = 0.6f * lateralError,
                TooClose = Mathf.Abs(lateral) < idealLateral,
                Height = 0.6f * heightError,
                TooLow = contact.y < idealHeight,
                Running = mover != null && mover.Velocity.magnitude > config.walkSpeed * 1.1f ? 0.35f : 0f,
                Tired = mover != null && mover.Exhausted ? 0.3f : 0f,
                Aim = config.aimErrorPenalty * Mathf.Abs(intent.Aim),
                BallSpeed = Mathf.Max(0f, ball.State.Velocity.magnitude - 25f) * 0.01f,
            };
            q.Badness = Mathf.Clamp(q.Reach + q.Height + q.Running + q.Tired + q.Aim + q.BallSpeed, 0f, 1.5f);
            return q;
        }

        // 스트로크의 높이 오차: 낮은 공 기준 아래는 줄여서 적용하고, 기준 위 0.5m 동안 원래 값으로 이어 붙인다.
        // 그보다 높은 공은 예전과 같다.
        float StrokeHeightError(float y)
        {
            const float Blend = 0.5f;
            float low = config.lowBallHeight;
            float Full(float h) => Mathf.Abs(h - config.idealHeight) / 1.5f;
            if (y <= low) return Full(y) * config.lowBallErrorScale;
            return Mathf.Lerp(Full(low) * config.lowBallErrorScale, Full(y), Mathf.Clamp01((y - low) / Blend));
        }

        // 스매시 입력이면 판정이 넓다: 더 높이, 더 옆으로, 앞뒤로 더 여유 있게 닿는다
        void Evaluate(Vector3 point, bool smash, out bool inHit, out bool inSwing, out float lateral)
        {
            Vector3 body = transform.position;
            lateral = (point.x - body.x) * Facing;
            float forward = (point.z - body.z) * Facing - config.contactForward;
            float reach = Mathf.Abs(lateral);
            float reachLimit = smash ? config.smashHitReach : config.hitReach;
            float depthTolerance = smash ? config.smashDepthTolerance : config.contactDepthTolerance;
            float maxHeight = smash ? config.smashReachHeight : config.maxHitHeight;
            bool depthOk = Mathf.Abs(forward) <= depthTolerance;

            inHit = depthOk && reach <= reachLimit &&
                    point.y >= config.minHitHeight && point.y <= maxHeight;
            inSwing = depthOk && reach <= reachLimit + config.swingReachExtra &&
                      point.y <= maxHeight + 0.5f;
        }

        // 내 코트에 한 번 바운드한 뒤 두 번째 바운드 전까지의 구간
        bool TryGetWindow(out int start, out int end)
        {
            start = end = -1;
            if (ball.BouncesSinceHit == 0)
            {
                if (trajectory.FirstBounceIndex < 0 || !IsGoodBounce(trajectory.FirstBouncePoint)) return false;
                start = trajectory.FirstBounceIndex;
                end = trajectory.SecondBounceIndex >= 0 ? trajectory.SecondBounceIndex : trajectory.Count - 1;
                return true;
            }
            if (ball.BouncesSinceHit == 1)
            {
                if (!IsGoodBounce(ball.FirstBouncePoint)) return false;
                start = 0;
                end = trajectory.FirstBounceIndex >= 0 ? trajectory.FirstBounceIndex : trajectory.Count - 1;
                return true;
            }
            return false;
        }

        // 서브는 정해진 서비스 박스, 랠리는 단식 코트에 떨어져야 받는다
        bool IsGoodBounce(Vector3 p)
        {
            if (Court.SideOf(p.z) != side) return false;
            return ball.IsServe ? Court.IsInServiceBox(p, side, ball.ServeToDeuce) : Court.IsInSingles(p, side);
        }

        // 우선순위: 공중 스매시 > (네트 앞) 발리 > 바운드 후 스트로크 > (바운드를 기다릴 수 없으면) 발리
        bool FindContact(bool smash, out int index, out bool volley)
        {
            index = -1;
            volley = false;
            bool airborne = ball.BouncesSinceHit == 0 && !ball.IsServe; // 서브는 반드시 바운드 후에 받는다
            int beforeBounce = trajectory.FirstBounceIndex >= 0 ? trajectory.FirstBounceIndex : trajectory.Count - 1;
            float bodyZ = transform.position.z;

            if (smash && airborne && FindAirSmashIndex(trajectory, beforeBounce, bodyZ, Facing, config.contactForward,
                    config.smashDepthTolerance, config.smashAirMinHeight, config.smashReachHeight,
                    config.smashIdealHeight, out index))
                return true;

            if (airborne && InVolleyZone && FindVolleyIndex(trajectory, beforeBounce, bodyZ, Facing,
                    config.contactForward, config.volleyMinHeight, config.maxHitHeight, out index))
            {
                volley = true;
                return true;
            }

            float depthTolerance = smash ? config.smashDepthTolerance : config.contactDepthTolerance;
            if (TryGetWindow(out int start, out int end) &&
                FindContactIndex(trajectory, start, end, bodyZ, Facing, config.contactForward, depthTolerance,
                    config.minHitHeight, out index))
                return true;

            // 뒤쪽에서도, 코트 안에 떨어질 공이 몸 뒤에서 바운드하면 기다릴 수 없으니 발리
            if (airborne && trajectory.FirstBounceIndex >= 0 && IsGoodBounce(trajectory.FirstBouncePoint))
            {
                float bounceAhead = (trajectory.FirstBouncePoint.z - (bodyZ + config.contactForward * Facing)) * Facing;
                if (bounceAhead < -config.contactDepthTolerance &&
                    FindVolleyIndex(trajectory, beforeBounce, bodyZ, Facing, config.contactForward,
                        config.volleyMinHeight, config.maxHitHeight, out index))
                {
                    volley = true;
                    return true;
                }
            }
            return false;
        }

        // 발리: 바운드 전 [1, end]에서 공이 몸 앞 타점 평면을 지나는 순간 (높이가 맞을 때)
        public static bool FindVolleyIndex(Trajectory t, int end, float bodyZ, float facing, float contactForward,
            float minHeight, float maxHeight, out int index)
        {
            index = -1;
            float planeZ = bodyZ + contactForward * facing;
            if ((t.Positions[0].z - planeZ) * facing <= 0f) return false; // 이미 지나감
            for (int k = 1; k <= end && k < t.Count; k++)
            {
                if ((t.Positions[k].z - planeZ) * facing > 0f) continue;
                float y = t.Positions[k].y;
                if (y < minHeight || y > maxHeight) return false;
                index = k;
                return true;
            }
            return false;
        }

        // 공중 스매시: 바운드 전 [1, end]에서 공이 타점 평면 앞뒤 depthTolerance 안을 지나는 동안,
        // [minHeight, maxHeight] 높이 중 idealHeight에 가장 가까운 샘플
        public static bool FindAirSmashIndex(Trajectory t, int end, float bodyZ, float facing, float contactForward,
            float depthTolerance, float minHeight, float maxHeight, float idealHeight, out int index)
        {
            index = -1;
            float planeZ = bodyZ + contactForward * facing;
            float best = float.MaxValue;
            for (int k = 1; k <= end && k < t.Count; k++)
            {
                float ahead = (t.Positions[k].z - planeZ) * facing;
                if (ahead > depthTolerance) continue;  // 아직 멀다
                if (ahead < -depthTolerance) break;    // 이미 지나갔다
                float y = t.Positions[k].y;
                if (y < minHeight || y > maxHeight) continue;
                float score = Mathf.Abs(y - idealHeight);
                if (score < best)
                {
                    best = score;
                    index = k;
                }
            }
            return index >= 0;
        }

        // 바운드 후 [start, end] 구간에서 공을 칠 샘플을 찾는다.
        public static bool FindContactIndex(Trajectory t, int start, int end, float bodyZ, float facing,
            float contactForward, float depthTolerance, float minHeight, out int index)
        {
            index = -1;
            float planeZ = bodyZ + contactForward * facing;
            float Ahead(int k) => (t.Positions[k].z - planeZ) * facing; // 타점 평면보다 앞이면 +

            int i = start;
            if (Ahead(i) > 0f)
            {
                // 보통: 바운드 후 몸 앞 타점 평면을 지나는 순간
                while (i < end && Ahead(i) > 0f) i++;
                if (Ahead(i) > 0f) return false; // 평면에 닿기 전에 두 번째 바운드
            }
            else if (Ahead(i) < -depthTolerance)
            {
                return false; // 몸 뒤에서 바운드
            }

            // 발밑 바운드거나 타점에서 너무 낮으면: 몸 범위 안에서 공이 떠오를 때까지 기다렸다 친다 (하프발리)
            while (t.Positions[i].y < minHeight)
            {
                i++;
                if (i > end || Ahead(i) < -depthTolerance) return false;
            }
            index = i;
            return true;
        }

        bool FindDescendingCrossing(int from, int to, float height, out Vector3 point)
        {
            point = default;
            for (int k = Mathf.Max(1, from); k <= to && k < trajectory.Count; k++)
            {
                Vector3 previous = trajectory.Positions[k - 1], p = trajectory.Positions[k];
                if (previous.y >= height && p.y < height)
                {
                    point = p;
                    return true;
                }
            }
            return false;
        }

        float Gaussian()
        {
            double u1 = 1.0 - rng.NextDouble();
            double u2 = rng.NextDouble();
            return (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2));
        }
    }
}
