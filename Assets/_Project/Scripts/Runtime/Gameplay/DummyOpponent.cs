using UnityEngine;

namespace FirstPersonTennis
{
    // 1단계용 허수아비: 공을 쫓아가 이상적 타점에 서고, 무작위 구질로 돌려보낸다.
    public class DummyOpponent : MonoBehaviour, IMover, IShotIntentSource
    {
        [SerializeField] TennisConfig config;
        [SerializeField] TennisBall ball;
        [SerializeField] AutoSwing swing;
        [SerializeField] int randomSeed = 7;

        public Vector3 Velocity { get; private set; }
        public bool Exhausted => false;
        public bool SkipRequested => false;
        public Vector3 FeedContact => swing.FeedContact;

        System.Random rng;
        ShotIntent planned;
        bool hasPlan;
        float reactionTimer;

        int Side => swing.Side;
        Vector3 Home => new Vector3(0f, 0f, (Court.HalfLength + 0.8f) * Side);

        void Awake() => rng = new System.Random(randomSeed);

        void Start() => FaceNet();

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            Vector3 target = Home;
            float maxSpeed = config.dummySpeed * 0.6f;
            bool incoming = ball.IsLive && ball.LastHitterSide == -Side;
            if (incoming)
            {
                reactionTimer += dt;
                target = transform.position;
                maxSpeed = config.dummySpeed;
                if (reactionTimer >= config.dummyReactionDelay && swing.TryGetIdealContact(out Vector3 contact))
                    target = swing.SpotFor(contact);
            }
            else
            {
                reactionTimer = 0f;
            }

            Vector3 to = target - transform.position;
            to.y = 0f;
            float distance = to.magnitude;
            Vector3 desired = distance > 0.02f ? to / distance * Mathf.Min(maxSpeed, distance * 6f) : Vector3.zero;
            Velocity = Vector3.MoveTowards(Velocity, desired, config.dummyAcceleration * dt);

            Vector3 p = transform.position + Velocity * dt;
            p.x = Mathf.Clamp(p.x, -9f, 9f);
            p.z = Mathf.Clamp(p.z * Side, 0.5f, 17f) * Side;
            transform.position = p;
        }

        public void Feed()
        {
            var intent = new ShotIntent
            {
                Type = ShotType.Topspin,
                Aim = (float)(rng.NextDouble() * 1.2 - 0.6),
                Depth = 1f,
                SpeedScale = config.feedSpeedScale,
            };
            swing.Feed(intent);
        }

        public void ResetTo(Vector3 position)
        {
            transform.position = position;
            Velocity = Vector3.zero;
            reactionTimer = 0f;
            FaceNet();
        }

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
        }

        ShotIntent PickShot()
        {
            var intent = new ShotIntent
            {
                Type = ShotType.Topspin,
                Aim = (float)(rng.NextDouble() * 1.5 - 0.75),
                Depth = 1f,
            };
            double roll = rng.NextDouble();
            if (roll < config.dummyLobChance)
            {
                intent.Type = ShotType.Lob;
                intent.Depth = (float)rng.NextDouble();
            }
            else if (roll < config.dummyLobChance + config.dummySliceChance)
            {
                intent.Type = ShotType.Slice;
                intent.Depth = 0.6f + 0.4f * (float)rng.NextDouble();
            }
            return intent;
        }

        void FaceNet() => transform.rotation = Quaternion.LookRotation(new Vector3(0f, 0f, Court.Facing(Side)));
    }
}
