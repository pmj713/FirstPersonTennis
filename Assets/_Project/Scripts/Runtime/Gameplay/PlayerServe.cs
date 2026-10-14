using System;
using UnityEngine;

namespace FirstPersonTennis
{
    // 플레이어 서브: A/D로 위치를 고르고, 좌클릭을 누르면 토스, 드래그로 코너(←→)와 구질(없음 플랫 · ↓ 슬라이스 · ↑ 킥),
    // 놓는 순간 친다. 공이 이상적 높이에 가까울 때 놓을수록 정확하고 빠르다. 너무 낮을 때 놓으면 헛스윙(폴트).
    public class PlayerServe : MonoBehaviour, IServeInput
    {
        [SerializeField] TennisConfig config;
        [SerializeField] TennisBall ball;
        [SerializeField] AutoSwing swing;
        [SerializeField] PlayerMovement movement;

        public bool MyTurn { get; private set; }
        public bool Tossing { get; private set; }
        public bool SecondServe { get; private set; }
        public bool ToDeuce { get; private set; }
        public bool WantsInput => MyTurn;

        public event Action Whiffed; // 타이밍을 놓쳐 헛스윙 → 폴트

        public void BeginTurn(bool toDeuce, bool secondServe)
        {
            MyTurn = true;
            Tossing = false;
            ToDeuce = toDeuce;
            SecondServe = secondServe;

            // 센터 마크와 단식 사이드라인 사이, 서브할 쪽 절반에서만 좌우로 움직인다
            int side = movement.Side;
            float rightSign = Court.Facing(side);
            float sign = toDeuce ? rightSign : -rightSign;
            movement.LockForServe(0.15f * sign, (Court.SinglesHalfWidth - 0.15f) * sign, (Court.HalfLength + 0.3f) * side);
            swing.PrepareServe(true);
            HoldBall();
        }

        public void EndTurn()
        {
            MyTurn = false;
            Tossing = false;
            movement.UnlockServe();
            swing.PrepareServe(false);
        }

        public void OnPress()
        {
            if (MyTurn && !Tossing) Toss();
        }

        public void OnRelease(ShotIntent intent)
        {
            if (MyTurn && Tossing) Hit(intent);
        }

        void Update()
        {
            if (!MyTurn) return;
            if (!Tossing)
            {
                HoldBall(); // 위치를 옮기는 동안 공도 손에 들고 따라간다
                return;
            }
            // 치지 않고 떨어뜨리면 다시 토스 (폴트 아님)
            if (ball.IsToss && ball.State.Velocity.y < 0f && ball.State.Position.y < config.serveRetossHeight)
            {
                Tossing = false;
                HoldBall();
            }
        }

        void HoldBall() => ball.Hold(swing.ServeHandPosition);

        void Toss()
        {
            Tossing = true;
            ball.Toss(swing.ServeHandPosition, ServeTiming.TossSpeed(config));
        }

        void Hit(ShotIntent intent)
        {
            float height = ball.State.Position.y;
            EndTurn();
            if (!ServeTiming.CanHit(config, height))
            {
                Whiffed?.Invoke();
                return;
            }
            swing.HitServe(TypeFor(intent), intent.Aim, ServeTiming.Quality(config, height), SecondServe, ToDeuce);
        }

        // 드래그 세로축: 없음 = 플랫, ↓ = 슬라이스, ↑ = 킥
        public static ServeType TypeFor(ShotIntent intent) => intent.Type switch
        {
            ShotType.Slice => ServeType.Slice,
            ShotType.Lob => ServeType.Kick,
            _ => ServeType.Flat,
        };
    }
}
