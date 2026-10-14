using System;
using UnityEngine;

namespace FirstPersonTennis
{
    // 공 상태를 고정 스텝(1/240초)으로 진행한다. 타격은 BeforeStep에서 정확한 스텝에 적용된다.
    // 에디터에서도 돌아서 설정의 공 지름을 바꾸면 바로 크기가 바뀐다 (물리는 플레이 중에만).
    [ExecuteAlways]
    [DefaultExecutionOrder(-10)]
    public class TennisBall : MonoBehaviour
    {
        [SerializeField] TennisConfig config;

        public BallState State;
        public long StepIndex { get; private set; }
        public bool IsLive { get; private set; }
        public int LastHitterSide { get; private set; } // -1, +1, 0 = 아무도 치지 않음 (토스 포함)
        public int BouncesSinceHit { get; private set; }
        public Vector3 FirstBouncePoint { get; private set; }

        // 서브로 친 공: 리시버는 바운드 후에만 칠 수 있고, 첫 바운드는 이 박스에 들어가야 한다
        public bool IsServe { get; private set; }
        public bool ServeToDeuce { get; private set; }
        // 서버가 토스한 공 (아직 치기 전)
        public bool IsToss { get; private set; }
        // 이번에 친 뒤 네트에 닿았는지 (서브면 렛)
        public bool TouchedNet { get; private set; }

        // 지금 날아가는 공의 시간 배율 (공 템포 × 이번 샷 배율, 예: 스매시)
        public float TimeScale => Mathf.Max(0.05f, config.ballTimeScale * flightTempo);

        public event Action<long> BeforeStep;
        public event Action<int> Launched; // 친 쪽 side, 토스는 0
        public event Action<Vector3> Bounced;
        public event Action NetHit;
        public event Action CrossedNet;

        const int MaxStepsPerFrame = 120;
        float accumulator;
        float flightTempo = 1f;
        Vector3 previousPosition;

        void Awake()
        {
            if (config != null) config.ApplyGlobals(); // 씬 빌더가 막 붙였을 때는 아직 연결 전
            transform.localScale = Vector3.one * (BallPhysics.Radius * 2f);
        }

        public void Hold(Vector3 position)
        {
            IsLive = false;
            LastHitterSide = 0;
            BouncesSinceHit = 0;
            IsServe = IsToss = TouchedNet = false;
            flightTempo = 1f;
            State = new BallState(position, Vector3.zero, Vector3.zero);
            previousPosition = position;
            accumulator = 0f;
            transform.position = position;
        }

        // tempo: 이번 샷만의 시간 배율 (스매시처럼 빠르게 날아가는 공은 1보다 크다)
        public void Launch(int hitterSide, Vector3 velocity, Vector3 spin, float tempo = 1f)
        {
            StartFlight(hitterSide, velocity, spin, tempo);
            IsServe = false;
            Launched?.Invoke(hitterSide);
        }

        public void LaunchServe(int serverSide, Vector3 velocity, Vector3 spin, bool toDeuce)
        {
            StartFlight(serverSide, velocity, spin, 1f);
            IsServe = true;
            ServeToDeuce = toDeuce;
            Launched?.Invoke(serverSide);
        }

        // 서버가 손에서 공을 위로 던진다
        public void Toss(Vector3 from, float upSpeed)
        {
            State = new BallState(from, new Vector3(0f, upSpeed, 0f), Vector3.zero);
            StartFlight(0, State.Velocity, Vector3.zero, 1f);
            IsServe = false;
            IsToss = true;
            Launched?.Invoke(0);
        }

        void StartFlight(int hitterSide, Vector3 velocity, Vector3 spin, float tempo)
        {
            State.Velocity = velocity;
            State.Spin = spin;
            LastHitterSide = hitterSide;
            BouncesSinceHit = 0;
            IsToss = TouchedNet = false;
            flightTempo = tempo;
            if (!IsLive)
            {
                IsLive = true;
                accumulator = 0f;
                previousPosition = State.Position;
            }
        }

        void Update()
        {
            float diameter = BallPhysics.Radius * 2f; // 설정에서 공 지름을 바꾸면 바로 반영
            if (!Mathf.Approximately(transform.localScale.x, diameter)) transform.localScale = Vector3.one * diameter;

            if (!IsLive) return;

            accumulator += Time.deltaTime * TimeScale; // 공의 물리만 공 템포대로
            int steps = 0;
            while (accumulator >= BallPhysics.Step && steps < MaxStepsPerFrame)
            {
                steps++;
                accumulator -= BallPhysics.Step;
                BeforeStep?.Invoke(StepIndex);
                if (!IsLive) break; // BeforeStep에서 공을 잡았다 (서브 타격 전 토스 회수 등)
                previousPosition = State.Position;
                StepEvents ev = BallPhysics.Integrate(ref State, config.surface);
                StepIndex++;

                if (ev.HitNet || ev.NetCord) TouchedNet = true;
                if (ev.HitNet) NetHit?.Invoke();
                if (ev.CrossedNet) CrossedNet?.Invoke();
                if (ev.Bounced)
                {
                    BouncesSinceHit++;
                    if (BouncesSinceHit == 1) FirstBouncePoint = ev.BouncePoint;
                    Bounced?.Invoke(ev.BouncePoint);
                }
            }
            if (steps >= MaxStepsPerFrame) accumulator = 0f;

            transform.position = Vector3.Lerp(previousPosition, State.Position, accumulator / BallPhysics.Step);
        }
    }
}
