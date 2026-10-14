using NUnit.Framework;

namespace FirstPersonTennis.Tests
{
    public class ScoreTests
    {
        const int Near = -1, Far = 1;

        static TennisScore New(MatchFormat format, bool noAd = false, int server = Near) =>
            new TennisScore(new MatchRules { format = format, noAd = noAd }, server);

        static void Win(TennisScore s, int side, int count)
        {
            for (int i = 0; i < count; i++) s.AddPoint(side);
        }

        static void WinGames(TennisScore s, int side, int count) => Win(s, side, 4 * count);

        [Test]
        public void PointLabelsFollowTennisCounting()
        {
            var s = New(MatchFormat.OneSet);
            Assert.AreEqual("0", s.PointLabel(Near));
            Win(s, Near, 1);
            Assert.AreEqual("15", s.PointLabel(Near));
            Win(s, Near, 1);
            Assert.AreEqual("30", s.PointLabel(Near));
            Win(s, Near, 1);
            Assert.AreEqual("40", s.PointLabel(Near));
            Assert.AreEqual("0", s.PointLabel(Far));
        }

        [Test]
        public void DeuceNeedsTwoPointLead()
        {
            var s = New(MatchFormat.OneSet);
            Win(s, Near, 3);
            Win(s, Far, 3);
            Assert.IsTrue(s.IsDeuce);
            s.AddPoint(Near);
            Assert.AreEqual("AD", s.PointLabel(Near));
            Assert.AreEqual(0, s.Games(Near));
            s.AddPoint(Far);
            Assert.IsTrue(s.IsDeuce);
            s.AddPoint(Far);
            PointOutcome o = s.AddPoint(Far);
            Assert.IsTrue(o.GameWon);
            Assert.AreEqual(1, s.Games(Far));
        }

        [Test]
        public void NoAdDecidesAtDeuce()
        {
            var s = New(MatchFormat.OneSet, noAd: true);
            Win(s, Near, 3);
            Win(s, Far, 3);
            PointOutcome o = s.AddPoint(Far);
            Assert.IsTrue(o.GameWon);
            Assert.AreEqual(1, s.Games(Far));
        }

        [Test]
        public void ServerAlternatesEachGameAndSidesAlternateEachPoint()
        {
            var s = New(MatchFormat.OneSet, server: Near);
            Assert.IsTrue(s.ServeFromDeuce);
            s.AddPoint(Near);
            Assert.IsFalse(s.ServeFromDeuce);
            Win(s, Near, 3);
            Assert.AreEqual(Far, s.Server);
            Assert.IsTrue(s.ServeFromDeuce);
        }

        [Test]
        public void SetIsWonSixFourAndNeedsTwoGameLead()
        {
            var s = New(MatchFormat.BestOfThree);
            WinGames(s, Near, 5);
            WinGames(s, Far, 5);
            WinGames(s, Near, 1);
            Assert.AreEqual(0, s.Sets(Near), "6-5는 아직 세트가 아님");
            PointOutcome o = default;
            for (int i = 0; i < 4; i++) o = s.AddPoint(Near);
            Assert.IsTrue(o.SetWon);
            Assert.AreEqual(1, s.Sets(Near));
            Assert.AreEqual(new UnityEngine.Vector2Int(7, 5), s.CompletedSets[0]);
            Assert.IsFalse(s.MatchOver);
        }

        [Test]
        public void TiebreakAtSixAllAndServerRotation()
        {
            var s = New(MatchFormat.OneSet, server: Near);
            for (int g = 0; g < 6; g++)
            {
                WinGames(s, Near, 1);
                WinGames(s, Far, 1);
            }
            Assert.IsTrue(s.InTiebreak);
            int first = s.Server;
            s.AddPoint(Near); // 1점 뒤 교대
            Assert.AreEqual(-first, s.Server);
            Assert.IsFalse(s.ServeFromDeuce, "교대한 서버의 첫 포인트는 애드 코트");
            s.AddPoint(Near);
            Assert.AreEqual(-first, s.Server, "같은 서버가 두 포인트");
            s.AddPoint(Near); // 3점 뒤 교대
            Assert.AreEqual(first, s.Server);

            Win(s, Near, 3); // 6-0
            Assert.IsFalse(s.MatchOver);
            PointOutcome o = s.AddPoint(Near); // 7-0
            Assert.IsTrue(o.MatchWon);
            Assert.AreEqual(Near, s.Winner);
            Assert.AreEqual(new UnityEngine.Vector2Int(7, 6), s.CompletedSets[0]);
        }

        [Test]
        public void TiebreakNeedsTwoPointLead()
        {
            var s = New(MatchFormat.TiebreakOnly);
            Assert.IsTrue(s.InTiebreak);
            for (int i = 0; i < 6; i++)
            {
                s.AddPoint(Near);
                s.AddPoint(Far);
            }
            s.AddPoint(Near); // 7-6
            Assert.IsFalse(s.MatchOver);
            s.AddPoint(Near); // 8-6
            Assert.IsTrue(s.MatchOver);
            Assert.AreEqual(new UnityEngine.Vector2Int(8, 6), s.LastTiebreakPoints);
        }

        [Test]
        public void ShortSetTiebreakAtFourAll()
        {
            var s = New(MatchFormat.ShortSet);
            for (int g = 0; g < 4; g++)
            {
                WinGames(s, Near, 1);
                WinGames(s, Far, 1);
            }
            Assert.IsTrue(s.InTiebreak);
        }

        [Test]
        public void BestOfThreeEndsAfterTwoSets()
        {
            var s = New(MatchFormat.BestOfThree);
            WinGames(s, Far, 6);
            Assert.AreEqual(1, s.Sets(Far));
            Assert.IsFalse(s.MatchOver);
            WinGames(s, Far, 6);
            Assert.IsTrue(s.MatchOver);
            Assert.AreEqual(Far, s.Winner);
        }

        [Test]
        public void EndChangeAfterOddGames()
        {
            var s = New(MatchFormat.OneSet);
            PointOutcome o = default;
            for (int i = 0; i < 4; i++) o = s.AddPoint(Near);
            Assert.IsTrue(o.EndChange, "1게임 뒤");
            for (int i = 0; i < 4; i++) o = s.AddPoint(Near);
            Assert.IsFalse(o.EndChange, "2게임 뒤");
        }
    }
}
