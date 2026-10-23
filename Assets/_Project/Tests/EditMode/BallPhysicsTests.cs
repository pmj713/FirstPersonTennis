using NUnit.Framework;
using UnityEngine;

namespace FirstPersonTennis.Tests
{
    public class BallPhysicsTests
    {
        static readonly SurfaceProfile Hard = SurfaceProfile.Hard;

        [Test]
        public void DroppedBallReboundsToRealisticHeight()
        {
            // ITF 기준: 254cm에서 떨어뜨리면 135~147cm 튄다. 코트는 그보다 약간 덜 튄다.
            var s = new BallState(new Vector3(0f, 2.54f, -8f), Vector3.zero, Vector3.zero);
            bool bounced = false;
            float apex = 0f;
            for (int i = 0; i < 2400; i++)
            {
                StepEvents ev = BallPhysics.Integrate(ref s, Hard);
                if (ev.Bounced)
                {
                    if (bounced) break;
                    bounced = true;
                }
                if (bounced) apex = Mathf.Max(apex, s.Position.y);
            }
            Assert.IsTrue(bounced);
            Assert.That(apex, Is.InRange(1.15f, 1.5f));
        }

        [Test]
        public void TopspinKicksForwardMoreThanBackspinAfterBounce()
        {
            var v = new Vector3(0f, -5f, 18f);
            var start = new Vector3(0f, BallPhysics.Radius + 0.001f, 5f);
            var top = new BallState(start, v, new Vector3(200f, 0f, 0f));
            var back = new BallState(start, v, new Vector3(-200f, 0f, 0f));

            Assert.IsTrue(BallPhysics.Integrate(ref top, Hard).Bounced);
            Assert.IsTrue(BallPhysics.Integrate(ref back, Hard).Bounced);
            Assert.Greater(top.Velocity.z, back.Velocity.z);
        }

        // 드롭샷처럼 느리고 백스핀이 많은 공은 바운드에서 죽는다 (낮게 튀고 앞으로 거의 안 감). 빠른 슬라이스는 그대로 미끄러져 간다
        [Test]
        public void DropShotDiesOnTheBounceButFastSliceRunsOn()
        {
            var inside = new Vector3(0.8f, 1f, -7f);
            (float apex, float travel) drop = AfterBounce(inside, ShotSolver.SolveDrive(inside, new Vector3(3f, 0f, 2.2f), 11f, -150f, Hard));
            var baseline = new Vector3(0.8f, 1f, -12.2f);
            (float apex, float travel) slice = AfterBounce(baseline, ShotSolver.SolveDrive(baseline, new Vector3(0f, 0f, 9.5f), 28f, -150f, Hard));

            Assert.Less(drop.apex, 0.5f, "드롭샷은 낮게 튄다");
            Assert.Less(drop.travel, 1.5f, "드롭샷은 두 번째 바운드까지 앞으로 거의 안 간다");
            Assert.Greater(slice.travel, 5f, "빠른 슬라이스는 바운드 뒤에도 앞으로 간다");
        }

        // 첫 바운드 뒤 최고 높이와 두 번째 바운드까지 앞으로 간 거리
        static (float apex, float travel) AfterBounce(Vector3 from, ShotSolution shot)
        {
            var s = new BallState(from, shot.Velocity, shot.Spin);
            float apex = 0f, firstZ = 0f;
            int bounces = 0;
            for (int i = 0; i < 6000; i++)
            {
                if (BallPhysics.Integrate(ref s, Hard).Bounced && ++bounces == 1) firstZ = s.Position.z;
                if (bounces == 2) break;
                if (bounces == 1) apex = Mathf.Max(apex, s.Position.y);
            }
            Assert.AreEqual(2, bounces, "두 번 튀어야 함");
            return (apex, Mathf.Abs(s.Position.z - firstZ));
        }

        [Test]
        public void TopspinDipsShorterThanFlat()
        {
            var from = new Vector3(0f, 1f, -11f);
            var v = new Vector3(0f, 5f, 25f);
            float flat = LandingZ(new BallState(from, v, Vector3.zero));
            float topspin = LandingZ(new BallState(from, v, new Vector3(250f, 0f, 0f)));
            Assert.Less(topspin, flat - 1f);
        }

        [Test]
        public void BallIntoNetDropsOnHitterSide()
        {
            var s = new BallState(new Vector3(0f, 0.5f, -3f), new Vector3(0f, 0f, 20f), Vector3.zero);
            bool hitNet = false;
            for (int i = 0; i < 2400; i++)
            {
                StepEvents ev = BallPhysics.Integrate(ref s, Hard);
                if (ev.HitNet) hitNet = true;
                if (ev.Bounced) break;
            }
            Assert.IsTrue(hitNet);
            Assert.Less(s.Position.z, 0f);
        }

        [Test]
        public void PredictionMatchesStepByStepIntegration()
        {
            var start = new BallState(new Vector3(1f, 1f, -11f), new Vector3(-1f, 4f, 24f), new Vector3(200f, 0f, 0f));
            var trajectory = new Trajectory(1500);
            trajectory.Simulate(start, Hard);

            BallState live = start;
            for (int k = 1; k < trajectory.Count; k++)
            {
                BallPhysics.Integrate(ref live, Hard);
                Assert.AreEqual(trajectory.Positions[k], live.Position, $"step {k}");
            }
            Assert.GreaterOrEqual(trajectory.FirstBounceIndex, 0);
        }

        static float LandingZ(BallState s)
        {
            for (int i = 0; i < 4000; i++)
                if (BallPhysics.Integrate(ref s, Hard).Bounced) return s.Position.z;
            return float.NaN;
        }
    }
}
