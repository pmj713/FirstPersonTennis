using NUnit.Framework;

namespace FirstPersonTennis.Tests
{
    // 싱글플레이 단계 도전: 9단계 순서와 경기 길이, 이길 때마다 다음 단계가 열리는지
    public class SinglePlayTests
    {
        int saved;

        [SetUp]
        public void SaveProgress()
        {
            saved = StageProgress.Snapshot(); // 이 PC의 실제 기록을 건드리지 않게 되돌려 둔다
            StageProgress.Restore(0);
        }

        [TearDown]
        public void RestoreProgress() => StageProgress.Restore(saved);

        [Test]
        public void StagesGoThroughEveryCourtBeforeTheNextDifficulty()
        {
            Assert.AreEqual(9, SinglePlayStages.Count);
            var expected = new (AiDifficulty difficulty, CourtType court)[]
            {
                (AiDifficulty.Easy, CourtType.Hard), (AiDifficulty.Easy, CourtType.Clay), (AiDifficulty.Easy, CourtType.Grass),
                (AiDifficulty.Normal, CourtType.Hard), (AiDifficulty.Normal, CourtType.Clay), (AiDifficulty.Normal, CourtType.Grass),
                (AiDifficulty.Hard, CourtType.Hard), (AiDifficulty.Hard, CourtType.Clay), (AiDifficulty.Hard, CourtType.Grass),
            };
            for (int i = 0; i < expected.Length; i++)
            {
                Stage s = SinglePlayStages.Get(i);
                Assert.AreEqual(i + 1, s.Number);
                Assert.AreEqual(expected[i].difficulty, s.Difficulty, $"{s.Number}단계 난이도");
                Assert.AreEqual(expected[i].court, s.Court, $"{s.Number}단계 코트");
            }
        }

        [Test]
        public void MatchesGetLongerWithDifficulty()
        {
            Assert.AreEqual(MatchFormat.TiebreakOnly, SinglePlayStages.Get(0).Format);
            Assert.AreEqual(MatchFormat.TiebreakOnly, SinglePlayStages.Get(2).Format);
            Assert.AreEqual(MatchFormat.ShortSet, SinglePlayStages.Get(3).Format);
            Assert.AreEqual(MatchFormat.ShortSet, SinglePlayStages.Get(5).Format);
            Assert.AreEqual(MatchFormat.OneSet, SinglePlayStages.Get(6).Format);
            Assert.AreEqual(MatchFormat.OneSet, SinglePlayStages.Get(8).Format);
        }

        [Test]
        public void OnlyTheFirstStageIsOpenAtTheStart()
        {
            Assert.IsTrue(StageProgress.IsUnlocked(0));
            for (int i = 1; i < SinglePlayStages.Count; i++) Assert.IsFalse(StageProgress.IsUnlocked(i), $"{i + 1}단계");
            Assert.AreEqual(0, StageProgress.NextStage);
            Assert.AreEqual(0, StageProgress.ClearedCount);
        }

        [Test]
        public void WinningAStageOpensOnlyTheNextOne()
        {
            Assert.IsTrue(StageProgress.MarkCleared(0), "처음 깨면 true");
            Assert.IsFalse(StageProgress.MarkCleared(0), "다시 깨도 새로 열리는 단계는 없다");
            Assert.IsTrue(StageProgress.IsUnlocked(1));
            Assert.IsFalse(StageProgress.IsUnlocked(2));
            Assert.AreEqual(1, StageProgress.NextStage);

            for (int i = 1; i < SinglePlayStages.Count - 1; i++) StageProgress.MarkCleared(i);
            Assert.IsFalse(StageProgress.AllCleared, "마지막 단계를 깨기 전에는 자유 경기의 경기 모드가 잠겨 있다");
            StageProgress.MarkCleared(SinglePlayStages.Count - 1);
            Assert.IsTrue(StageProgress.AllCleared);
            Assert.AreEqual(SinglePlayStages.Count, StageProgress.ClearedCount);
            Assert.AreEqual(SinglePlayStages.Count - 1, StageProgress.NextStage, "모두 깨면 마지막 단계");
        }
    }
}
