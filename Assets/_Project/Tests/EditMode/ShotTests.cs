using NUnit.Framework;
using UnityEngine;

namespace FirstPersonTennis.Tests
{
    public class ShotSolverTests
    {
        static readonly SurfaceProfile Hard = SurfaceProfile.Hard;

        [TestCase(0f, 9.3f)]
        [TestCase(3.3f, 7f)]
        [TestCase(-3.3f, 7f)]
        public void TopspinDriveLandsOnTarget(float x, float z)
        {
            var from = new Vector3(0.5f, 1f, -12.3f);
            ShotSolution s = ShotSolver.SolveDrive(from, new Vector3(x, 0f, z), 27f, 230f, Hard);
            Assert.IsTrue(s.Success, $"landing {s.Landing}");
            AssertLandsNear(from, s, new Vector2(x, z), 0.5f);
        }

        [Test]
        public void SliceDropShotLandsJustOverTheNet()
        {
            var from = new Vector3(0.5f, 0.9f, -12f);
            ShotSolution s = ShotSolver.SolveDrive(from, new Vector3(0f, 0f, 2.2f), 11f, -150f, Hard);
            Assert.IsTrue(s.Success, $"landing {s.Landing}");
            AssertLandsNear(from, s, new Vector2(0f, 2.2f), 0.5f);
        }

        [Test]
        public void LobGoesHighAndDeep()
        {
            var from = new Vector3(0.5f, 1f, -12f);
            ShotSolution s = ShotSolver.SolveFixedAngle(from, new Vector3(0f, 0f, 10f), 50f, 120f, Hard);
            Assert.IsTrue(s.Success, $"landing {s.Landing}");

            var state = new BallState(from, s.Velocity, s.Spin);
            float apex = 0f;
            for (int i = 0; i < 4000 && !BallPhysics.Integrate(ref state, Hard).Bounced; i++)
                apex = Mathf.Max(apex, state.Position.y);
            Assert.Greater(apex, 5f);
        }

        [Test]
        public void SmashFromNearTheNetIsHitDownward()
        {
            var from = new Vector3(0.3f, 2.6f, -4f);
            ShotSolution s = ShotSolver.SolveDrive(from, new Vector3(1f, 0f, 8f), 34f, 40f, Hard, -35f);
            Assert.IsTrue(s.Success, $"landing {s.Landing}");
            Assert.Less(s.Velocity.y, 0f);
        }

        [TestCase(-1)]
        [TestCase(1)]
        public void PlannerSendsEveryShotTypeIntoOpponentCourt(int hitterSide)
        {
            var config = ScriptableObject.CreateInstance<TennisConfig>();
            var from = new Vector3(0.5f, 1f, 12.3f * hitterSide);
            foreach (ShotType type in new[] { ShotType.Topspin, ShotType.Slice, ShotType.Lob })
            {
                foreach (float aim in new[] { -1f, 0f, 1f })
                {
                    var intent = new ShotIntent { Type = type, Aim = aim, Depth = 0.7f };
                    ShotSolution s = ShotPlanner.Plan(config, intent, from, hitterSide);
                    Assert.IsTrue(s.Success, $"{type} aim {aim}: landing {s.Landing}");
                    Assert.IsTrue(Court.IsInSingles(s.Landing, -hitterSide), $"{type} aim {aim}: landing {s.Landing}");
                }
            }
            Object.DestroyImmediate(config);
        }

        static void AssertLandsNear(Vector3 from, ShotSolution s, Vector2 target, float tolerance)
        {
            var state = new BallState(from, s.Velocity, s.Spin);
            for (int i = 0; i < 4000; i++)
            {
                StepEvents ev = BallPhysics.Integrate(ref state, Hard);
                Assert.IsFalse(ev.HitNet, "공이 네트에 걸림");
                if (ev.Bounced)
                {
                    var landing = new Vector2(ev.BouncePoint.x, ev.BouncePoint.z);
                    Assert.Less(Vector2.Distance(landing, target), tolerance, $"landing {landing}");
                    return;
                }
            }
            Assert.Fail("공이 떨어지지 않음");
        }
    }

    public class GestureTests
    {
        [Test]
        public void ClickOnlyIsCenterTopspin()
        {
            ShotIntent i = ShotGestureInput.Classify(Vector2.zero, 0f);
            Assert.AreEqual(ShotType.Topspin, i.Type);
            Assert.AreEqual(0f, i.Aim);
        }

        [Test]
        public void DragDownLeftIsSliceToTheLeft()
        {
            ShotIntent i = ShotGestureInput.Classify(new Vector2(-0.2f, -0.2f), 0f);
            Assert.AreEqual(ShotType.Slice, i.Type);
            Assert.Less(i.Aim, 0f);
            Assert.That(i.Depth, Is.InRange(0.5f, 0.7f));
        }

        [Test]
        public void ShortDragDownIsShortSlice()
        {
            ShotIntent i = ShotGestureInput.Classify(new Vector2(0f, -0.04f), 0f);
            Assert.AreEqual(ShotType.Slice, i.Type);
            Assert.Less(i.Depth, 0.1f);
        }

        [Test]
        public void DragUpIsLob()
        {
            ShotIntent i = ShotGestureInput.Classify(new Vector2(0.01f, 0.25f), 0.25f);
            Assert.AreEqual(ShotType.Lob, i.Type);
            Assert.AreEqual(0f, i.Aim);
        }

        [Test]
        public void UpThenDownIsSmash()
        {
            ShotIntent i = ShotGestureInput.Classify(new Vector2(0f, -0.02f), 0.15f);
            Assert.AreEqual(ShotType.Smash, i.Type);
        }
    }
}
