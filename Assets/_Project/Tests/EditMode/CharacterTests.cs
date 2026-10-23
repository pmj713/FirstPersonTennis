using NUnit.Framework;
using UnityEngine;

namespace FirstPersonTennis.Tests
{
    // 이동속도는 스탯 0이 기본값이며, 다른 스탯 효과는 기존 기준을 유지한다.
    public class CharacterTests
    {
        static readonly CharacterPreset[] Roster = CharacterPreset.DefaultRoster();
        static PlayerStats StatsOf(CharacterPreset c) => PlayerStats.From(c, StatTuning.Default);
        static CharacterPreset Named(string name) => System.Array.Find(Roster, c => c.name == name);

        [Test]
        // 합계는 150에서 시작해 밸런스에 맞춰 선수별로 조정한다. 스탯은 0~100 안이어야 한다.
        public void RosterStatsAreInRange()
        {
            Assert.AreEqual(4, Roster.Length);
            foreach (CharacterPreset c in Roster)
                foreach (int stat in new[] { c.power, c.technique, c.speed })
                    Assert.That(stat, Is.InRange(0, CharacterPreset.MaxStat), c.name);
        }

        [Test]
        public void BalancedCharacterHasMovementBonusAndOtherBaselineEffects()
        {
            PlayerStats s = StatsOf(Named("균형형"));
            Assert.AreEqual(1f, s.PowerScale, 1e-5f);
            Assert.AreEqual(1f, s.ErrorScale, 1e-5f);
            Assert.AreEqual(1f, s.SpinScale, 1e-5f);
            Assert.AreEqual(1.075f, s.MoveScale, 1e-5f);
            Assert.AreEqual(1f, s.RecoveryScale, 1e-5f);
            Assert.AreEqual(1f, s.SlideScale, 1e-5f);
        }

        // 스탯 100이면 정한 만큼. 이동속도는 0에서 기본값, 다른 효과는 50에서 기본값이다.
        [Test]
        public void EffectsScaleWithTheStatAndTechniqueIsWidest()
        {
            StatTuning t = StatTuning.Default;
            PlayerStats top = StatsOf(new CharacterPreset { power = 100, technique = 100, speed = 100 });
            PlayerStats bottom = StatsOf(new CharacterPreset { power = 0, technique = 0, speed = 0 });
            Assert.AreEqual(1f + t.powerSpeedAtMax, top.PowerScale, 1e-5f);
            Assert.AreEqual(1f - t.techniqueErrorAtMax, top.ErrorScale, 1e-5f);
            Assert.AreEqual(1f + t.techniqueErrorAtMax, bottom.ErrorScale, 1e-5f);
            Assert.AreEqual(1f + t.speedMoveAtMax, top.MoveScale, 1e-5f);
            Assert.AreEqual(1f, bottom.MoveScale, 1e-5f);

            Assert.Less(t.speedMoveAtMax, t.techniqueErrorAtMax);
            Assert.Less(t.powerSpeedAtMax, t.techniqueErrorAtMax);

            // 범위를 벗어난 스탯은 0~100으로 자른다
            PlayerStats over = StatsOf(new CharacterPreset { power = 150, technique = -20, speed = 50 });
            Assert.AreEqual(top.PowerScale, over.PowerScale, 1e-5f);
            Assert.AreEqual(bottom.ErrorScale, over.ErrorScale, 1e-5f);
        }

        [TestCase(-20, 1f)]
        [TestCase(0, 1f)]
        [TestCase(50, 1.05f)]
        [TestCase(100, 1.1f)]
        [TestCase(150, 1.1f)]
        public void MovementStartsAtZeroAndClampsTheStat(int speed, float expected)
        {
            StatTuning tuning = StatTuning.Default;
            tuning.speedMoveAtMax = 0.1f;
            PlayerStats stats = PlayerStats.From(new CharacterPreset { speed = speed }, tuning);
            Assert.AreEqual(expected, stats.MoveScale, 1e-5f);
        }

        [Test]
        public void ControlTypeHasTheSmallestError()
        {
            PlayerStats control = StatsOf(Named("컨트롤형"));
            Assert.AreEqual(0.76f, control.ErrorScale, 1e-4f, "기술 80 → 오차 −24%");
            foreach (CharacterPreset c in Roster)
                if (c.name != "컨트롤형") Assert.Greater(StatsOf(c).ErrorScale, control.ErrorScale, c.name);
        }

        [Test]
        public void EachTypeHasItsStrength()
        {
            PlayerStats attack = StatsOf(Named("공격형"));
            PlayerStats control = StatsOf(Named("컨트롤형"));
            PlayerStats defence = StatsOf(Named("수비형"));
            Assert.Greater(attack.PowerScale, 1f);
            Assert.Less(control.ErrorScale, 1f);
            Assert.Greater(control.SpinScale, 1f);
            Assert.Greater(defence.MoveScale, 1f);
            Assert.Greater(defence.RecoveryScale, 1f);
            Assert.Less(defence.SlideScale, 1f, "발이 빠른 선수는 클레이에서 덜 미끄러진다");
        }

        // 힘이 세면 더 빠르게 날아가지만 같은 목표에 떨어지도록 계획된다
        [Test]
        public void PowerfulDriveIsFasterAndStillLandsOnTarget()
        {
            TennisConfig config = TestConfig.Create();
            PlayerStats attack = StatsOf(Named("공격형"));
            var contact = new Vector3(0.8f, 1f, -12.3f);
            ShotSolution normal = ShotPlanner.Plan(config, ShotIntent.Default, contact, -1);
            ShotSolution strong = ShotPlanner.Plan(config, ShotIntent.Default, contact, -1, attack.PowerScale, attack.SpinScale);
            Assert.IsTrue(strong.Success, $"landing {strong.Landing}");
            Assert.Greater(strong.Velocity.magnitude, normal.Velocity.magnitude * (1f + (attack.PowerScale - 1f) * 0.5f),
                "힘으로 빨라진 만큼의 절반 이상은 궤적 계산 뒤에도 남는다");
            Assert.AreEqual(normal.Landing.z, strong.Landing.z, 0.5f);
            Assert.IsTrue(Court.IsInSingles(strong.Landing, 1));
            TestConfig.Destroy(config);
        }

        // 기술이 높을수록 코너를 라인 가까이 노린다 (그래도 코트 안으로 계획된다)
        [Test]
        public void SkilledPlayerAimsCloserToTheSideline()
        {
            TennisConfig config = TestConfig.Create();
            var contact = new Vector3(0.8f, 1f, -12.3f);
            var corner = new ShotIntent { Type = ShotType.Topspin, Aim = 1f, Depth = 1f };
            float LandingX(CharacterPreset c)
            {
                PlayerStats s = StatsOf(c);
                ShotSolution sol = ShotPlanner.Plan(config, corner, contact, -1, s.PowerScale, s.SpinScale, s.AimMarginScale);
                Assert.IsTrue(sol.Success, c.name);
                Assert.IsTrue(Court.IsInSingles(sol.Landing, 1), $"{c.name} {sol.Landing}");
                return sol.Landing.x;
            }
            float control = LandingX(Named("컨트롤형"));
            float balanced = LandingX(Named("균형형"));
            float attack = LandingX(Named("공격형"));
            Assert.AreEqual(Court.SinglesHalfWidth - config.aimMargin * 0.52f, control, 0.3f, "기술 80 → 라인에서 약 0.42m");
            Assert.Greater(control, balanced + 0.25f);
            Assert.Greater(balanced, attack);
            TestConfig.Destroy(config);
        }

        [Test]
        public void EveryCharactersServeIsPlannedIntoTheBox()
        {
            TennisConfig config = TestConfig.Create();
            foreach (CharacterPreset c in Roster)
            {
                PlayerStats s = StatsOf(c);
                foreach (ServeType type in new[] { ServeType.Flat, ServeType.Slice, ServeType.Kick })
                foreach (float aim in new[] { -1f, 0f, 1f })
                foreach (bool deuce in new[] { true, false })
                {
                    Vector3 contact = Court.ServePosition(-1, deuce, 0.6f) + new Vector3(0.35f, config.serveIdealHeight, 0.3f);
                    ShotSolution sol = ShotPlanner.PlanServe(config, type, aim, contact, -1, deuce, s.PowerScale, s.SpinScale);
                    var state = new BallState(contact, sol.Velocity, sol.Spin);
                    Vector3 bounce = default;
                    bool netted = false;
                    for (int i = 0; i < 4000; i++)
                    {
                        StepEvents ev = BallPhysics.Integrate(ref state, SurfaceProfile.Hard);
                        if (ev.HitNet) { netted = true; break; }
                        if (ev.Bounced) { bounce = ev.BouncePoint; break; }
                    }
                    string label = $"{c.name} {type} aim {aim} deuce {deuce}";
                    Assert.IsFalse(netted, label);
                    Assert.IsTrue(Court.IsInServiceBox(bounce, 1, deuce), $"{label} → {bounce}");
                }
            }
            TestConfig.Destroy(config);
        }
    }
}
