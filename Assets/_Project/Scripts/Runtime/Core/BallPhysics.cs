using UnityEngine;

namespace FirstPersonTennis
{
    public struct BallState
    {
        public Vector3 Position;
        public Vector3 Velocity;
        public Vector3 Spin; // 각속도 (rad/s)

        public BallState(Vector3 position, Vector3 velocity, Vector3 spin)
        {
            Position = position;
            Velocity = velocity;
            Spin = spin;
        }
    }

    public struct StepEvents
    {
        public bool Bounced;
        public bool HitNet;
        public bool CrossedNet;
        public Vector3 BouncePoint;
        public Vector3 NetPoint;
    }

    // 결정적 공 물리. Unity 물리 엔진을 쓰지 않아서, 같은 상태에서 출발하면 예측과 실제 비행이 정확히 같다.
    public static class BallPhysics
    {
        public const float Radius = 0.0335f;
        public const float Step = 1f / 240f;

        const float Mass = 0.057f;
        const float AirDensity = 1.21f;
        const float DragCoefficient = 0.55f;
        const float InertiaFactor = 2f / 3f; // 속이 빈 구: I = 2/3 m r²
        const float RollThreshold = 0.4f;    // 이보다 느리게 닿으면 튀지 않고 구른다
        const float RollingDecay = 0.5f;

        static readonly float AeroK = 0.5f * AirDensity * Mathf.PI * Radius * Radius / Mass;
        static readonly Vector3 Gravity = new Vector3(0f, -9.81f, 0f);

        public static Vector3 Acceleration(Vector3 velocity, Vector3 spin)
        {
            Vector3 a = Gravity;
            float speed = velocity.magnitude;
            if (speed < 1e-4f) return a;

            a -= AeroK * DragCoefficient * speed * velocity;

            float w = spin.magnitude;
            if (w > 1e-3f)
            {
                // 마그누스 양력 계수 Cl = 1 / (2 + v / rω)
                float lift = 1f / (2f + speed / (Radius * w));
                a += AeroK * lift * speed / w * Vector3.Cross(spin, velocity);
            }
            return a;
        }

        public static StepEvents Integrate(ref BallState s, in SurfaceProfile surface)
        {
            var events = new StepEvents();
            Vector3 p0 = s.Position;

            // 중점법(RK2)
            Vector3 a1 = Acceleration(s.Velocity, s.Spin);
            Vector3 vMid = s.Velocity + a1 * (Step * 0.5f);
            Vector3 a2 = Acceleration(vMid, s.Spin);
            s.Position += vMid * Step;
            s.Velocity += a2 * Step;

            float z0 = p0.z, z1 = s.Position.z;
            if ((z0 < 0f && z1 >= 0f) || (z0 > 0f && z1 <= 0f))
            {
                Vector3 c = Vector3.Lerp(p0, s.Position, z0 / (z0 - z1));
                events.NetPoint = c;
                if (Mathf.Abs(c.x) <= Court.NetPostX && c.y - Radius < Court.NetHeightAt(c.x))
                {
                    events.HitNet = true;
                    s.Position = new Vector3(c.x, c.y, Mathf.Sign(z0) * (Radius + 0.01f));
                    s.Velocity = new Vector3(s.Velocity.x * 0.2f, Mathf.Min(s.Velocity.y, 0f) * 0.3f, -s.Velocity.z * 0.1f);
                    s.Spin *= 0.2f;
                    return events;
                }
                events.CrossedNet = true;
            }

            if (s.Position.y < Radius && s.Velocity.y < 0f)
            {
                s.Position.y = Radius;
                if (-s.Velocity.y < RollThreshold)
                {
                    s.Velocity.y = 0f;
                    float k = 1f - RollingDecay * Step;
                    s.Velocity.x *= k;
                    s.Velocity.z *= k;
                }
                else
                {
                    Bounce(ref s, surface);
                    events.Bounced = true;
                    events.BouncePoint = s.Position;
                }
            }
            return events;
        }

        static void Bounce(ref BallState s, in SurfaceProfile surface)
        {
            float vIn = -s.Velocity.y;
            float vOut = surface.restitution * vIn;

            // 접점의 미끄럼 속도 = 수평 속도 + ω × (0, -r, 0)
            Vector3 slip = new Vector3(s.Velocity.x + Radius * s.Spin.z, 0f, s.Velocity.z - Radius * s.Spin.x);
            float slipSpeed = slip.magnitude;
            if (slipSpeed > 1e-5f)
            {
                Vector3 dir = slip / slipSpeed;
                // 미끄럼을 멈추고 굴러가게 만드는 충격량(질량당)과 마찰 한계 중 작은 쪽
                float toRolling = slipSpeed * InertiaFactor / (1f + InertiaFactor);
                float maxFriction = surface.friction * (vIn + vOut);
                float j = Mathf.Min(toRolling, maxFriction);

                s.Velocity.x -= j * dir.x;
                s.Velocity.z -= j * dir.z;
                s.Spin.x += j * dir.z / (InertiaFactor * Radius);
                s.Spin.z -= j * dir.x / (InertiaFactor * Radius);
            }
            s.Velocity.y = vOut;
        }
    }

    // 현재 상태에서 앞으로의 궤적. Positions[k] = k번 적분한 뒤의 위치 (k = 0은 현재).
    public sealed class Trajectory
    {
        public readonly Vector3[] Positions;
        public readonly byte[] Bounces; // 해당 샘플까지 바운드 횟수
        public int Count { get; private set; }
        public int FirstBounceIndex { get; private set; }
        public int SecondBounceIndex { get; private set; }
        public Vector3 FirstBouncePoint { get; private set; }
        public bool HitNet { get; private set; }

        public Trajectory(int capacity)
        {
            Positions = new Vector3[capacity];
            Bounces = new byte[capacity];
        }

        public void Simulate(BallState s, in SurfaceProfile surface, int stopAfterBounces = 2)
        {
            FirstBounceIndex = SecondBounceIndex = -1;
            HitNet = false;
            byte bounces = 0;
            Positions[0] = s.Position;
            Bounces[0] = 0;
            Count = 1;

            while (Count < Positions.Length)
            {
                StepEvents ev = BallPhysics.Integrate(ref s, surface);
                if (ev.HitNet) HitNet = true;
                if (ev.Bounced)
                {
                    bounces++;
                    if (bounces == 1)
                    {
                        FirstBounceIndex = Count;
                        FirstBouncePoint = ev.BouncePoint;
                    }
                    else if (bounces == 2)
                    {
                        SecondBounceIndex = Count;
                    }
                }
                Positions[Count] = s.Position;
                Bounces[Count] = bounces;
                Count++;
                if (bounces >= stopAfterBounces) break;
            }
        }
    }
}
