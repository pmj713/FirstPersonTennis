using NUnit.Framework;
using UnityEngine;

namespace FirstPersonTennis.Tests
{
    // 공격 로브: 네트 앞 상대는 머리 위를 스매시가 닿지 않는 높이(smashReachHeight + lobOverMargin)로 넘기되,
    // 기본 로브보다 낮게 빨리 떨어진다. 상대가 착지점 뒤에 있으면 기본 로브 그대로. 사람·AI 구분 없이 같다
    public class LobOverTests
    {
        static readonly ShotIntent DeepLob = new ShotIntent { Type = ShotType.Lob, Depth = 1f };
        static readonly Vector3 From = new Vector3(0.5f, 1f, -12.3f);

        [TestCase(1.5f)]
        [TestCase(3.2f)]
        [TestCase(5f)]
        public void LobPassesOverANetPlayerOutOfSmashReach(float netPlayerZ)
        {
            var config = TestConfig.Create();
            ShotSolution plain = ShotPlanner.Plan(config, DeepLob, From, -1);
            ShotSolution over = ShotPlanner.Plan(config, DeepLob, From, -1, opponents: new[] { new Vector3(0f, 0f, netPlayerZ) });

            Assert.IsTrue(over.Success, $"landing {over.Landing}");
            Assert.IsTrue(Court.IsInSingles(over.Landing, 1), $"landing {over.Landing}");
            Assert.AreEqual(plain.Landing.z, over.Landing.z, 0.3f);
            Assert.GreaterOrEqual(HeightAtZ(over, netPlayerZ), config.smashReachHeight + config.lobOverMargin - 0.1f);
            Assert.Less(FlightSeconds(over), FlightSeconds(plain) - 0.15f, "기본 로브보다 빨리 떨어진다");
            TestConfig.Destroy(config);
        }

        [Test]
        public void PlayerBehindTheLandingGetsTheUsualLob()
        {
            var config = TestConfig.Create();
            ShotSolution plain = ShotPlanner.Plan(config, DeepLob, From, -1);
            ShotSolution back = ShotPlanner.Plan(config, DeepLob, From, -1,
                opponents: new[] { new Vector3(0f, 0f, Court.HalfLength + 0.5f) });
            Assert.Less((plain.Velocity - back.Velocity).magnitude, 1e-4f);
            TestConfig.Destroy(config);
        }

        [Test]
        public void DoublesLobClearsEveryNetPlayerAndIgnoresTheBaselinePartner()
        {
            var config = TestConfig.Create();
            float clear = config.smashReachHeight + config.lobOverMargin;
            var near = new Vector3(-2f, 0f, 2.5f);
            var far = new Vector3(2f, 0f, 4.5f);
            var baseline = new Vector3(2f, 0f, Court.HalfLength + 0.5f);

            ShotSolution both = ShotPlanner.Plan(config, DeepLob, From, -1, opponents: new[] { near, far });
            Assert.IsTrue(both.Success, $"landing {both.Landing}");
            Assert.GreaterOrEqual(HeightAtZ(both, near.z), clear - 0.1f);
            Assert.GreaterOrEqual(HeightAtZ(both, far.z), clear - 0.1f);

            ShotSolution onlyNear = ShotPlanner.Plan(config, DeepLob, From, -1, opponents: new[] { near });
            ShotSolution withBaseline = ShotPlanner.Plan(config, DeepLob, From, -1, opponents: new[] { near, baseline });
            Assert.Less((onlyNear.Velocity - withBaseline.Velocity).magnitude, 1e-4f);
            TestConfig.Destroy(config);
        }

        // 공이 그 깊이(z)를 지나는 높이
        static float HeightAtZ(in ShotSolution s, float z)
        {
            var state = new BallState(From, s.Velocity, s.Spin);
            for (int i = 0; i < 4000; i++)
            {
                Vector3 prev = state.Position;
                if (BallPhysics.Integrate(ref state, Court.Surface).Bounced) break;
                if (prev.z < z && state.Position.z >= z)
                    return Mathf.Lerp(prev.y, state.Position.y, (z - prev.z) / (state.Position.z - prev.z));
            }
            return -1f;
        }

        // 첫 바운드까지 걸리는 공 시간 (초)
        static float FlightSeconds(in ShotSolution s)
        {
            var state = new BallState(From, s.Velocity, s.Spin);
            int steps = 0;
            while (steps < 4000 && !BallPhysics.Integrate(ref state, Court.Surface).Bounced) steps++;
            return steps * BallPhysics.Step;
        }
    }
}
