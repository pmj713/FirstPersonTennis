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
            var config = TestConfig.Create();
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
            TestConfig.Destroy(config);
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

        // 설정의 데드존: 좌우로 그보다 짧게 끌면 가운데
        [Test]
        public void DeadZoneSettingDecidesWhetherShortSideDragAims()
        {
            var shortSide = new Vector2(0.02f, 0f);
            Assert.AreEqual(0f, ShotGestureInput.Classify(shortSide, 0f).Aim, "기본 데드존 3%");
            Assert.Greater(ShotGestureInput.Classify(shortSide, 0f, 0.01f).Aim, 0f, "데드존 1%");
            Assert.AreEqual(0f, ShotGestureInput.Classify(new Vector2(0.04f, 0f), 0f, 0.05f).Aim, "데드존 5%");
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
            ShotIntent i = ShotGestureInput.Classify(new Vector2(0f, -0.06f), 0f);
            Assert.AreEqual(ShotType.Slice, i.Type);
            Assert.Less(i.Depth, 0.1f);
        }

        // 드롭샷도 짧게 비스듬히 끌면 코너 끝까지 노린다 (길이가 짧아도 방향은 각도로)
        [TestCase(-1f)]
        [TestCase(1f)]
        public void ShortDiagonalDragDownIsDropShotToTheCorner(float side)
        {
            ShotIntent i = ShotGestureInput.Classify(new Vector2(0.07f * side, -0.06f), 0f);
            Assert.AreEqual(ShotType.Slice, i.Type);
            Assert.Less(i.Depth, 0.1f, "드롭샷 길이");
            Assert.Greater(i.Aim * side, 0.95f, "코너 끝까지");

            ShotIntent half = ShotGestureInput.Classify(new Vector2(0.035f * side, -0.06f), 0f);
            Assert.AreEqual(ShotType.Slice, half.Type);
            Assert.That(half.Aim * side, Is.InRange(0.5f, 0.75f), "덜 기울이면 덜 간다");
        }

        [Test]
        public void ShortStraightDragDownStillGoesToTheCenter()
        {
            ShotIntent i = ShotGestureInput.Classify(new Vector2(0.02f, -0.06f), 0f);
            Assert.AreEqual(ShotType.Slice, i.Type);
            Assert.AreEqual(0f, i.Aim, "데드존 안의 손 떨림은 가운데");
        }

        // 계획한 드롭샷이 좌우 끝 목표에 실제로 떨어진다
        [TestCase(-1f)]
        [TestCase(1f)]
        public void AimedDropShotLandsShortNearTheSideline(float aim)
        {
            var config = TestConfig.Create();
            var from = new Vector3(0.5f, 1f, -12.3f);
            ShotSolution s = ShotPlanner.Plan(config, new ShotIntent { Type = ShotType.Slice, Aim = aim, Depth = 0f }, from, -1);
            Assert.IsTrue(s.Success, $"landing {s.Landing}");
            Assert.Less(s.Landing.z, 4f, "네트 가까이");
            Assert.Greater(s.Landing.x * aim, Court.SinglesHalfWidth * 0.6f, "옆줄 쪽");
            Assert.IsTrue(Court.IsInSingles(s.Landing, 1), $"landing {s.Landing}");
            TestConfig.Destroy(config);
        }

        [Test]
        public void DragUpIsLob()
        {
            ShotIntent i = ShotGestureInput.Classify(new Vector2(0.01f, 0.25f), 0.25f);
            Assert.AreEqual(ShotType.Lob, i.Type);
            Assert.AreEqual(0f, i.Aim);
        }

        [TestCase(0f, 0.05f)]   // 위로 살짝
        [TestCase(0.2f, 0.07f)] // 오른쪽으로 밀다가 위로 흐름
        [TestCase(-0.25f, 0.1f)] // 왼쪽으로 크게 밀다가 위로 흐름 (22°)
        public void SlightUpwardDriftStaysTopspin(float x, float y)
        {
            ShotIntent i = ShotGestureInput.Classify(new Vector2(x, y), y);
            Assert.AreEqual(ShotType.Topspin, i.Type);
        }

        [Test]
        public void DiagonalUpRightIsLobToTheRight()
        {
            ShotIntent i = ShotGestureInput.Classify(new Vector2(0.15f, 0.15f), 0.15f);
            Assert.AreEqual(ShotType.Lob, i.Type);
            Assert.Greater(i.Aim, 0f);
        }

        [Test]
        public void UpThenDownIsSmash()
        {
            ShotIntent i = ShotGestureInput.Classify(new Vector2(0f, -0.02f), 0.15f);
            Assert.AreEqual(ShotType.Smash, i.Type);
        }
    }
}
