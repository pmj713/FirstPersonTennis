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
        public int LastHitterSide { get; private set; } // -1, +1, 0 = 아직 없음
        public int BouncesSinceHit { get; private set; }
        public Vector3 FirstBouncePoint { get; private set; }

        // 지금 날아가는 공의 시간 배율 (공 템포 × 이번 샷 배율, 예: 스매시)
        public float TimeScale => Mathf.Max(0.05f, config.ballTimeScale * flightTempo);

        public event Action<long> BeforeStep;
        public event Action<int> Launched;
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
            flightTempo = 1f;
            State = new BallState(position, Vector3.zero, Vector3.zero);
            previousPosition = position;
            accumulator = 0f;
            transform.position = position;
        }

        // tempo: 이번 샷만의 시간 배율 (스매시처럼 빠르게 날아가는 공은 1보다 크다)
        public void Launch(int hitterSide, Vector3 velocity, Vector3 spin, float tempo = 1f)
        {
            State.Velocity = velocity;
            State.Spin = spin;
            LastHitterSide = hitterSide;
            BouncesSinceHit = 0;
            flightTempo = tempo;
            if (!IsLive)
            {
                IsLive = true;
                accumulator = 0f;
                previousPosition = State.Position;
            }
            Launched?.Invoke(hitterSide);
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
                previousPosition = State.Position;
                StepEvents ev = BallPhysics.Integrate(ref State, config.surface);
                StepIndex++;

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
