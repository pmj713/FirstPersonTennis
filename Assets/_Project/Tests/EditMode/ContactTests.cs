using NUnit.Framework;
using UnityEngine;

namespace FirstPersonTennis.Tests
{
    // 자동 스윙의 타점 찾기: 낮게 오는 공도 몸 범위 안이면 놓치지 않는다.
    public class ContactTests
    {
        const float BodyZ = -12f, Forward = 0.45f, Tolerance = 0.6f, MinHeight = 0.06f;
        static readonly SurfaceProfile Hard = SurfaceProfile.Hard;

        [TestCase(-6f, 20f, 130f)]     // 앞쪽 바운드
        [TestCase(-11.25f, 20f, 130f)] // 타점 평면 바로 앞 바운드
        [TestCase(-11.7f, 20f, 130f)]  // 발밑 바운드 (탑스핀)
        [TestCase(-11.75f, 16f, -84f)] // 발밑 바운드 (슬라이스)
        public void BallBouncingInFrontOrAtFeetIsHittable(float landZ, float speed, float spin)
        {
            Trajectory t = Incoming(landZ, speed, spin, out int start, out int end);
            Assert.IsTrue(Find(t, start, end, out int k), $"bounce {t.FirstBouncePoint}");

            Vector3 contact = t.Positions[k];
            float ahead = contact.z - (BodyZ + Forward);
            Assert.GreaterOrEqual(contact.y, MinHeight);
            Assert.GreaterOrEqual(ahead, -Tolerance);
        }

        [Test]
        public void BallBouncingBehindTheBodyIsNotHit()
        {
            Trajectory t = Incoming(-12.9f, 20f, 130f, out int start, out int end);
            Assert.IsFalse(Find(t, start, end, out _));
        }

        [Test]
        public void HighBallAtTheNetIsSmashedInTheAir()
        {
            // 정점이 높은 깊은 탑스핀을 네트 쪽(z = -5)에서 스매시
            Trajectory t = IncomingFrom(new Vector3(0f, 1f, 12.7f), -9.3f, 20.25f, 129f);
            Assert.IsTrue(AutoSwing.FindAirSmashIndex(t, t.FirstBounceIndex, -5f, 1f, Forward, 2f, 3.2f, out int k));
            Assert.That(t.Positions[k].y, Is.InRange(2f, 3.2f));
            Assert.Less(k, t.FirstBounceIndex);
        }

        [Test]
        public void LobOverTheNetPlayerIsTooHighToSmash()
        {
            var from = new Vector3(0f, 1f, 12.7f);
            ShotSolution s = ShotSolver.SolveFixedAngle(from, new Vector3(0.8f, 0f, -9.6f), 50f, 67f, Hard);
            var t = new Trajectory(2000);
            t.Simulate(new BallState(from, s.Velocity, s.Spin), Hard);
            Assert.IsFalse(AutoSwing.FindAirSmashIndex(t, t.FirstBounceIndex, -3f, 1f, Forward, 2f, 3.2f, out _));
        }

        [TestCase(3.1f, -2f)]
        [TestCase(3.1f, -12.3f)]
        [TestCase(2.1f, -13.5f)]
        public void PlannedSmashLandsInOpponentCourt(float height, float z)
        {
            var config = ScriptableObject.CreateInstance<TennisConfig>();
            var intent = new ShotIntent { Type = ShotType.Smash, Aim = 0.5f, Depth = 1f };
            ShotSolution s = ShotPlanner.Plan(config, intent, new Vector3(0.4f, height, z), -1);
            Assert.IsTrue(s.Success, $"landing {s.Landing}");
            Assert.IsTrue(Court.IsInSingles(s.Landing, 1), $"landing {s.Landing}");
            Object.DestroyImmediate(config);
        }

        static bool Find(Trajectory t, int start, int end, out int index) =>
            AutoSwing.FindContactIndex(t, start, end, BodyZ, 1f, Forward, Tolerance, MinHeight, out index);

        static Trajectory Incoming(float landZ, float speed, float spin, out int start, out int end)
        {
            Trajectory t = IncomingFrom(new Vector3(0f, 1f, 12f), landZ, speed, spin);
            start = t.FirstBounceIndex;
            end = t.SecondBounceIndex >= 0 ? t.SecondBounceIndex : t.Count - 1;
            return t;
        }

        static Trajectory IncomingFrom(Vector3 from, float landZ, float speed, float spin)
        {
            ShotSolution s = ShotSolver.SolveDrive(from, new Vector3(0.8f, 0f, landZ), speed, spin, Hard);
            var t = new Trajectory(2000);
            t.Simulate(new BallState(from, s.Velocity, s.Spin), Hard);
            return t;
        }
    }
}
