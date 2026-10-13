using System;
using UnityEngine;

namespace FirstPersonTennis
{
    // 공 상태를 고정 스텝(1/240초)으로 진행한다. 타격은 BeforeStep에서 정확한 스텝에 적용된다.
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

        public event Action<long> BeforeStep;
        public event Action<int> Launched;
        public event Action<Vector3> Bounced;
        public event Action NetHit;
        public event Action CrossedNet;

        const int MaxStepsPerFrame = 120;
        float accumulator;
        Vector3 previousPosition;

        public void Hold(Vector3 position)
        {
            IsLive = false;
            LastHitterSide = 0;
            BouncesSinceHit = 0;
            State = new BallState(position, Vector3.zero, Vector3.zero);
            previousPosition = position;
            accumulator = 0f;
            transform.position = position;
        }

        public void Launch(int hitterSide, Vector3 velocity, Vector3 spin)
        {
            State.Velocity = velocity;
            State.Spin = spin;
            LastHitterSide = hitterSide;
            BouncesSinceHit = 0;
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
            if (!IsLive) return;

            accumulator += Time.deltaTime;
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
