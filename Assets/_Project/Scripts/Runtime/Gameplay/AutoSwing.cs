using System;
using UnityEngine;

namespace FirstPersonTennis
{
    // 선입력이 있고 공이 스윙 범위로 들어오면, 임팩트 시각에 맞춰 스윙을 자동 실행한다.
    // 플레이어와 허수아비가 같이 쓴다.
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
        public bool IsSwinging { get; private set; }
        public bool HasContact { get; private set; }
        public Vector3 ContactPoint { get; private set; }
        public bool ContactInHitZone { get; private set; }
        public bool ContactInSwingZone { get; private set; }
        public float TimeToContact { get; private set; }
        public StrokeSide PredictedStroke { get; private set; }
        public bool HasLanding { get; private set; }
        public Vector3 LandingPoint { get; private set; }

        public event Action<bool> SwingResolved; // true = 맞힘, false = 헛스윙

        IShotIntentSource source;
        IMover mover;
        Trajectory trajectory;
        System.Random rng;
        long impactStep;
        ShotIntent lockedIntent;

        float Facing => Court.Facing(side);
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
            HasContact = HasLanding = ContactInHitZone = ContactInSwingZone = false;
            if (!Incoming)
            {
                if (racket != null && !IsSwinging) racket.SetPreparation(PredictedStroke, ShotType.Topspin, 0f);
                return;
            }

            trajectory.Simulate(ball.State, config.surface);
            if (ball.BouncesSinceHit == 0 && trajectory.FirstBounceIndex >= 0)
            {
                HasLanding = true;
                LandingPoint = trajectory.FirstBouncePoint;
            }

            if (IsSwinging)
            {
                TimeToContact = (impactStep - ball.StepIndex) * BallPhysics.Step;
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

            if (!FindContact(smash, out int index))
            {
                if (racket != null) racket.SetPreparation(PredictedStroke, ShotType.Topspin, 0f);
                return;
            }

            Vector3 point = trajectory.Positions[index];
            HasContact = true;
            ContactPoint = point;
            TimeToContact = index * BallPhysics.Step;
            Evaluate(point, smash, out bool inHit, out bool inSwing, out float lateral);
            ContactInHitZone = inHit;
            ContactInSwingZone = inSwing;
            PredictedStroke = lateral >= 0f ? StrokeSide.Forehand : StrokeSide.Backhand;

            if (racket != null)
            {
                float prep = hasIntent && inSwing ? 1f - TimeToContact / config.takeBackLead : 0f;
                racket.SetPreparation(PredictedStroke, intent.Type, prep);
            }

            int windupSteps = Mathf.CeilToInt(config.swingWindup / BallPhysics.Step);
            if (hasIntent && inSwing && index >= 1 && index <= windupSteps)
                StartSwing(intent, ball.StepIndex + index, TimeToContact);
        }

        // 허수아비가 포인트를 시작할 때: 자기 앞에서 공을 띄워 친다.
        public void Feed(ShotIntent intent)
        {
            Vector3 contact = FeedContact;
            ball.Hold(contact);
            if (racket != null) racket.PlaySwing(StrokeSide.Forehand, intent.Type, 0.05f, config.followThrough);
            Strike(intent, contact, config.idealLateral, false);
        }

        public Vector3 FeedContact => transform.position +
            new Vector3(config.idealLateral * Facing, config.idealHeight, config.contactForward * Facing);

        // 바운드 후 편하게 칠 수 있는 지점 (허리 높이로 내려오는 순간, 낮게 튀면 정점)
        public bool TryGetIdealContact(out Vector3 point)
        {
            point = default;
            if (!Incoming || !TryGetWindow(out int start, out int end)) return false;

            int apex = start;
            for (int k = start + 1; k <= end; k++)
            {
                Vector3 p = trajectory.Positions[k];
                if (p.y > trajectory.Positions[apex].y) apex = k;
                else if (k > apex && p.y <= config.idealHeight + 0.15f)
                {
                    point = p;
                    return true;
                }
            }
            point = trajectory.Positions[apex];
            return true;
        }

        // 공이 포핸드 쪽 이상적 타점에 오도록 서야 할 위치
        public Vector3 SpotFor(Vector3 contact) =>
            new Vector3(contact.x - config.idealLateral * Facing, 0f, contact.z - config.contactForward * Facing);

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
            if (hit) Strike(lockedIntent, contact, lateral, true);
            SwingResolved?.Invoke(hit);
        }

        void StartSwing(ShotIntent intent, long step, float timeToImpact)
        {
            IsSwinging = true;
            impactStep = step;
            lockedIntent = intent;
            source?.OnSwingCommitted();
            if (racket != null) racket.PlaySwing(PredictedStroke, intent.Type, timeToImpact, config.followThrough);
        }

        void Strike(ShotIntent intent, Vector3 contact, float lateral, bool withError)
        {
            ShotSolution solution = ShotPlanner.Plan(config, intent, contact, side);
            Vector3 velocity = solution.Velocity;
            Vector3 spin = solution.Spin;

            if (withError)
            {
                float bad = Badness(contact, lateral, intent);
                float elevation = Gaussian() * (config.baseElevationError + bad * config.elevationErrorPerBad) * errorScale;
                float azimuth = Gaussian() * (config.baseAzimuthError + bad * config.azimuthErrorPerBad) * errorScale;

                Quaternion yaw = Quaternion.AngleAxis(azimuth, Vector3.up);
                velocity = yaw * velocity;
                spin = yaw * spin;
                Vector3 axis = Vector3.Cross(Vector3.up, new Vector3(velocity.x, 0f, velocity.z));
                if (axis.sqrMagnitude > 1e-6f) velocity = Quaternion.AngleAxis(-elevation, axis.normalized) * velocity;
            }

            ball.Launch(side, velocity, spin);
        }

        // 0 = 완벽, 클수록 오차가 커진다
        float Badness(Vector3 contact, float lateral, ShotIntent intent)
        {
            // 스매시는 머리 위가 이상적 타점이다 (낮은 공에 스매시를 입력하면 플랫 스트로크라 허리 높이 기준)
            bool overhead = intent.Type == ShotType.Smash && contact.y >= config.smashMinHeight;
            float idealHeight = overhead ? config.smashIdealHeight : config.idealHeight;
            float lateralError = Mathf.Abs(Mathf.Abs(lateral) - config.idealLateral) / config.hitReach;
            float heightError = Mathf.Abs(contact.y - idealHeight) / 1.5f;
            float bad = 0.6f * lateralError + 0.6f * heightError;
            if (mover != null)
            {
                if (mover.Velocity.magnitude > config.walkSpeed * 1.1f) bad += 0.35f;
                if (mover.Exhausted) bad += 0.3f;
            }
            bad += 0.25f * Mathf.Abs(intent.Aim);
            bad += Mathf.Max(0f, ball.State.Velocity.magnitude - 25f) * 0.01f;
            return Mathf.Clamp(bad, 0f, 1.5f);
        }

        // 스매시 입력이면 팔과 라켓을 위로 뻗은 높이까지 닿는다
        void Evaluate(Vector3 point, bool smash, out bool inHit, out bool inSwing, out float lateral)
        {
            Vector3 body = transform.position;
            lateral = (point.x - body.x) * Facing;
            float forward = (point.z - body.z) * Facing - config.contactForward;
            float reach = Mathf.Abs(lateral);
            bool depthOk = Mathf.Abs(forward) <= config.contactDepthTolerance;
            float maxHeight = smash ? config.smashReachHeight : config.maxHitHeight;

            inHit = depthOk && reach <= config.hitReach &&
                    point.y >= config.minHitHeight && point.y <= maxHeight;
            inSwing = depthOk && reach <= config.hitReach + config.swingReachExtra &&
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

        bool IsGoodBounce(Vector3 p) => Court.SideOf(p.z) == side && Court.IsInSingles(p, side);

        bool FindContact(bool smash, out int index)
        {
            index = -1;
            // 스매시 입력이면 바운드 전에 머리 위로 오는 공을 공중에서 먼저 노린다
            if (smash && ball.BouncesSinceHit == 0)
            {
                int beforeBounce = trajectory.FirstBounceIndex >= 0 ? trajectory.FirstBounceIndex : trajectory.Count - 1;
                if (FindAirSmashIndex(trajectory, beforeBounce, transform.position.z, Facing, config.contactForward,
                        config.smashMinHeight, config.smashReachHeight, out index))
                    return true;
            }
            return TryGetWindow(out int start, out int end) &&
                   FindContactIndex(trajectory, start, end, transform.position.z, Facing,
                       config.contactForward, config.contactDepthTolerance, config.minHitHeight, out index);
        }

        // 바운드 전 [0, end] 구간에서 공이 타점 평면을 지날 때 머리 위 높이면 그 샘플 (공중 스매시)
        public static bool FindAirSmashIndex(Trajectory t, int end, float bodyZ, float facing, float contactForward,
            float minHeight, float maxHeight, out int index)
        {
            index = -1;
            float planeZ = bodyZ + contactForward * facing;
            if ((t.Positions[0].z - planeZ) * facing <= 0f) return false; // 이미 지나감
            for (int k = 1; k <= end; k++)
            {
                if ((t.Positions[k].z - planeZ) * facing > 0f) continue;
                float y = t.Positions[k].y;
                if (y < minHeight || y > maxHeight) return false;
                index = k;
                return true;
            }
            return false;
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

        float Gaussian()
        {
            double u1 = 1.0 - rng.NextDouble();
            double u2 = rng.NextDouble();
            return (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2));
        }
    }
}
