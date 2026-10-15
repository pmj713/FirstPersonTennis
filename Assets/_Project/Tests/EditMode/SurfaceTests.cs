using NUnit.Framework;
using UnityEngine;

namespace FirstPersonTennis.Tests
{
    // 코트 종류: 클레이는 높이 튀고 느려지며, 잔디는 낮게 깔리고 빠르다. 선수 이동도 바닥을 탄다.
    public class SurfaceTests
    {
        struct BounceResult
        {
            public float Apex;            // 첫 바운드 뒤 최고 높이
            public float SpeedRetained;   // 바운드 뒤 수평 속도 / 바운드 전
        }

        static BounceResult Bounce(SurfaceProfile surface, float speed, float spin)
        {
            var from = new Vector3(0.5f, 1f, -12.3f);
            ShotSolution s = ShotSolver.SolveDrive(from, new Vector3(0.5f, 0f, 9.3f), speed, spin, SurfaceProfile.Hard);
            Assert.IsTrue(s.Success, "setup");
            var state = new BallState(from, s.Velocity, s.Spin);
            var result = new BounceResult();
            bool bounced = false;
            for (int i = 0; i < 4000; i++)
            {
                Vector3 before = state.Velocity;
                StepEvents ev = BallPhysics.Integrate(ref state, surface);
                if (ev.Bounced)
                {
                    if (bounced) break;
                    bounced = true;
                    result.SpeedRetained = new Vector2(state.Velocity.x, state.Velocity.z).magnitude /
                                           new Vector2(before.x, before.z).magnitude;
                }
                if (bounced) result.Apex = Mathf.Max(result.Apex, state.Position.y);
            }
            Assert.IsTrue(bounced);
            return result;
        }

        [TestCase(32f, 160f)]  // 탑스핀
        [TestCase(24f, -150f)] // 슬라이스
        [TestCase(34f, 40f)]   // 플랫
        public void ClayBouncesHighestAndGrassLowest(float speed, float spin)
        {
            float clay = Bounce(SurfaceProfile.Clay, speed, spin).Apex;
            float hard = Bounce(SurfaceProfile.Hard, speed, spin).Apex;
            float grass = Bounce(SurfaceProfile.Grass, speed, spin).Apex;
            Assert.Greater(clay, hard);
            Assert.Greater(hard, grass);
        }

        [TestCase(32f, 160f)]
        [TestCase(24f, -150f)]
        [TestCase(34f, 40f)]
        public void ClayIsSlowestAfterTheBounceAndGrassFastest(float speed, float spin)
        {
            float clay = Bounce(SurfaceProfile.Clay, speed, spin).SpeedRetained;
            float hard = Bounce(SurfaceProfile.Hard, speed, spin).SpeedRetained;
            float grass = Bounce(SurfaceProfile.Grass, speed, spin).SpeedRetained;
            Assert.Less(clay, hard);
            Assert.Less(hard, grass);
        }

        [Test]
        public void SliceSkidsLowerOnGrassThanTopspin()
        {
            Assert.Less(Bounce(SurfaceProfile.Grass, 24f, -150f).Apex, Bounce(SurfaceProfile.Grass, 32f, 160f).Apex);
        }

        [Test]
        public void PlayersSlideWhenStoppingOnClay()
        {
            var moving = new Vector3(0f, 0f, 5f);
            try
            {
                Court.SetSurface(CourtType.Clay, SurfaceProfile.Clay);
                float clayBrake = Court.MoveRate(moving, Vector3.zero, 30f);
                float clayStart = Court.MoveRate(Vector3.zero, moving, 30f);
                Court.SetSurface(CourtType.Hard, SurfaceProfile.Hard);
                float hardBrake = Court.MoveRate(moving, Vector3.zero, 30f);

                Assert.AreEqual(30f, hardBrake, 1e-4f);
                Assert.Less(clayBrake, hardBrake);
                Assert.Less(clayBrake, clayStart, "클레이는 출발보다 멈출 때가 더 미끄럽다");
            }
            finally
            {
                Court.SetSurface(CourtType.Hard, SurfaceProfile.Hard);
            }
        }

        // 값이 비어 있는 바닥(0)은 하드처럼 동작한다: 공이 멈추거나 선수가 못 움직이지 않는다
        [Test]
        public void UnsetSurfaceValuesFallBackToNeutral()
        {
            var unset = new SurfaceProfile { restitution = 0.73f, friction = 0.55f };
            Assert.AreEqual(Bounce(SurfaceProfile.Hard, 34f, 40f).SpeedRetained, Bounce(unset, 34f, 40f).SpeedRetained, 1e-4f);
            try
            {
                Court.SetSurface(CourtType.Hard, unset);
                Assert.AreEqual(30f, Court.MoveRate(Vector3.forward, Vector3.zero, 30f), 1e-4f);
            }
            finally
            {
                Court.SetSurface(CourtType.Hard, SurfaceProfile.Hard);
            }
        }
    }
}
