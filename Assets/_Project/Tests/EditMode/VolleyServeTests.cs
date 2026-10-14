using NUnit.Framework;
using UnityEngine;

namespace FirstPersonTennis.Tests
{
    // 서비스 박스, 서브 계획과 토스, 발리 타점.
    public class VolleyServeTests
    {
        static readonly SurfaceProfile Hard = SurfaceProfile.Hard;

        [Test]
        public void DeuceBoxIsOnReceiversRight()
        {
            // 근거리 리시버(+z를 봄)의 오른쪽은 +x, 원거리 리시버(-z를 봄)의 오른쪽은 -x
            Assert.IsTrue(Court.IsInServiceBox(new Vector3(2f, 0f, -3f), -1, true));
            Assert.IsFalse(Court.IsInServiceBox(new Vector3(-2f, 0f, -3f), -1, true));
            Assert.IsTrue(Court.IsInServiceBox(new Vector3(-2f, 0f, 3f), 1, true));
            Assert.IsTrue(Court.IsInServiceBox(new Vector3(2f, 0f, 3f), 1, false));
        }

        [Test]
        public void ServiceBoxEndsAtTheServiceLine()
        {
            Assert.IsTrue(Court.IsInServiceBox(new Vector3(-1f, 0f, Court.ServiceLine - 0.1f), 1, true));
            Assert.IsFalse(Court.IsInServiceBox(new Vector3(-1f, 0f, Court.ServiceLine + 0.3f), 1, true));
            Assert.IsFalse(Court.IsInServiceBox(new Vector3(-1f, 0f, -1f), 1, true), "네트 반대편");
        }

        [TestCase(-1, true)]
        [TestCase(-1, false)]
        [TestCase(1, true)]
        [TestCase(1, false)]
        public void ServerAndReceiverStandDiagonally(int server, bool deuce)
        {
            Vector3 s = Court.ServePosition(server, deuce, 0.6f);
            Vector3 r = Court.ReceivePosition(-server, deuce);
            Assert.Less(s.x * r.x, 0f, "서버와 리시버는 코트 반대쪽 절반에 선다");
            Court.ServiceBox(-server, deuce, out float xMin, out float xMax);
            Assert.That(r.x, Is.InRange(xMin, xMax), "리시버는 받을 박스 쪽에 선다");
            Assert.Greater(s.z * server, Court.HalfLength, "서버는 베이스라인 뒤");
        }

        // 이상적 높이에서 친 모든 서브(구질 × 코너 × 코트 × 첫/두 번째)가 오차 없이 박스에 들어간다
        [Test]
        public void EveryPlannedServeLandsInTheBox()
        {
            TennisConfig config = TestConfig.Create();
            int checkedCount = 0;
            foreach (int server in new[] { -1, 1 })
            foreach (bool deuce in new[] { true, false })
            foreach (ServeType type in new[] { ServeType.Flat, ServeType.Slice, ServeType.Kick })
            foreach (float aim in new[] { -1f, 0f, 1f })
            foreach (bool second in new[] { false, true })
            {
                Vector3 contact = Court.ServePosition(server, deuce, 0.6f) +
                                  new Vector3(0.35f * Court.Facing(server), config.serveIdealHeight, 0.3f * Court.Facing(server));
                ShotSolution s = ShotPlanner.PlanServe(config, type, aim, contact, server, deuce, second);
                string label = $"server {server} deuce {deuce} {type} aim {aim} second {second}";
                Assert.IsTrue(s.Success, label);

                var state = new BallState(contact, s.Velocity, s.Spin);
                bool crossed = false;
                Vector3 bounce = default;
                for (int i = 0; i < 4000; i++)
                {
                    StepEvents ev = BallPhysics.Integrate(ref state, Hard);
                    Assert.IsFalse(ev.HitNet, label + " 네트");
                    crossed |= ev.CrossedNet;
                    if (ev.Bounced)
                    {
                        bounce = ev.BouncePoint;
                        break;
                    }
                }
                Assert.IsTrue(crossed, label);
                Assert.IsTrue(Court.IsInServiceBox(bounce, -server, deuce), $"{label} → {bounce}");
                checkedCount++;
            }
            Assert.AreEqual(72, checkedCount);
            TestConfig.Destroy(config);
        }

        [Test]
        public void FlatServeIsFastestAndSecondServeIsSlower()
        {
            TennisConfig config = TestConfig.Create();
            var contact = new Vector3(0.9f, config.serveIdealHeight, -12.2f);
            float Speed(ServeType type, bool second) =>
                ShotPlanner.PlanServe(config, type, 0f, contact, -1, true, second).Velocity.magnitude;
            Assert.Greater(Speed(ServeType.Flat, false), Speed(ServeType.Slice, false));
            Assert.Greater(Speed(ServeType.Slice, false), Speed(ServeType.Kick, false));
            Assert.Greater(Speed(ServeType.Flat, false), Speed(ServeType.Flat, true));
            TestConfig.Destroy(config);
        }

        [Test]
        public void TossPeaksNearTheConfiguredApex()
        {
            TennisConfig config = TestConfig.Create();
            var state = new BallState(new Vector3(0f, config.serveHandHeight, -12f),
                new Vector3(0f, ServeTiming.TossSpeed(config), 0f), Vector3.zero);
            float apex = 0f;
            for (int i = 0; i < 1000 && state.Velocity.y > -0.01f; i++)
            {
                BallPhysics.Integrate(ref state, Hard);
                apex = Mathf.Max(apex, state.Position.y);
            }
            Assert.That(apex, Is.InRange(config.serveTossApex - 0.1f, config.serveTossApex + 0.1f));
            Assert.Greater(apex, config.serveIdealHeight, "이상적 타점은 토스 정점 아래");
            TestConfig.Destroy(config);
        }

        [Test]
        public void TimingQualityPeaksAtTheIdealHeight()
        {
            TennisConfig config = TestConfig.Create();
            Assert.AreEqual(1f, ServeTiming.Quality(config, config.serveIdealHeight), 1e-4f);
            Assert.AreEqual(0f, ServeTiming.Quality(config, config.serveIdealHeight - config.serveTimingWindow), 1e-4f);
            Assert.AreEqual(0.5f, ServeTiming.Quality(config, config.serveIdealHeight + config.serveTimingWindow * 0.5f), 1e-4f);
            Assert.IsFalse(ServeTiming.CanHit(config, config.serveHitMinHeight - 0.01f));
            Assert.IsTrue(ServeTiming.CanHit(config, config.serveIdealHeight));
            TestConfig.Destroy(config);
        }

        [TestCase(ShotType.Topspin)]
        [TestCase(ShotType.Slice)]
        [TestCase(ShotType.Lob)]
        public void VolleyFromTheNetLandsInOpponentCourt(ShotType type)
        {
            TennisConfig config = TestConfig.Create();
            var intent = new ShotIntent { Type = type, Aim = 0.5f, Depth = 1f };
            ShotSolution s = ShotPlanner.PlanVolley(config, intent, new Vector3(0.5f, 1.1f, -3f), -1);
            Assert.IsTrue(s.Success, $"{type} landing {s.Landing}");
            Assert.IsTrue(Court.IsInSingles(s.Landing, 1), $"{type} landing {s.Landing}");
            TestConfig.Destroy(config);
        }

        // 네트 앞(z = -3)에 선 선수 앞으로 낮고 빠른 공: 바운드 전에 타점 평면에서 잡는다
        [Test]
        public void PasserIsVolleyedBeforeItBounces()
        {
            Trajectory t = Drive(new Vector3(0f, 1f, 12f), new Vector3(0.5f, 0f, -9f), 26f, 150f);
            Assert.IsTrue(Volley(t, -3f, out int k));
            Assert.Less(k, t.FirstBounceIndex);
            Assert.AreEqual(-3f + 0.45f, t.Positions[k].z, 0.15f);
            Assert.That(t.Positions[k].y, Is.InRange(0.2f, 2.5f));
        }

        [Test]
        public void BallBouncingBeforeTheVolleyPlaneIsNotVolleyed()
        {
            // 서비스라인 근처(z = -6)에 선 선수 앞 z = -3에 떨어지는 공
            Trajectory t = Drive(new Vector3(0f, 1f, 12f), new Vector3(0.5f, 0f, -3f), 20f, 130f);
            Assert.IsFalse(Volley(t, -6f, out _));
        }

        static bool Volley(Trajectory t, float bodyZ, out int index) =>
            AutoSwing.FindVolleyIndex(t, t.FirstBounceIndex, bodyZ, 1f, 0.45f, 0.2f, 2.5f, out index);

        static Trajectory Drive(Vector3 from, Vector3 target, float speed, float spin)
        {
            ShotSolution s = ShotSolver.SolveDrive(from, target, speed, spin, Hard);
            Assert.IsTrue(s.Success, $"setup landing {s.Landing}");
            var t = new Trajectory(2000);
            t.Simulate(new BallState(from, s.Velocity, s.Spin), Hard);
            return t;
        }
    }
}
