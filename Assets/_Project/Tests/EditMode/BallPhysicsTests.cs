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
