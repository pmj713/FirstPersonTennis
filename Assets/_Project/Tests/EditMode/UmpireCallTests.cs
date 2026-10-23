using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FirstPersonTennis.Tests
{
    // 심판 콜: 점수에 맞는 콜을 고르는지, 고를 수 있는 콜이 모두 녹음되어 있는지
    public class UmpireCallTests
    {
        const int A = -1, B = 1;
        const string Dir = "Assets/_Project/Audio/Umpire";

        static string Call(TennisScore score, int winner) => string.Join(" + ", UmpireVoice.CallFor(score, score.AddPoint(winner)));

        [Test]
        public void GameScoresAreCalledServerFirst()
        {
            var score = new TennisScore(new MatchRules { format = MatchFormat.OneSet }, A);
            Assert.AreEqual("point_1_0", Call(score, A), "Fifteen, love");
            Assert.AreEqual("point_1_1", Call(score, B), "Fifteen all");
            Assert.AreEqual("point_1_2", Call(score, B), "Fifteen, thirty");
            Assert.AreEqual("point_1_3", Call(score, B), "Fifteen, forty");
            Assert.AreEqual("point_2_3", Call(score, A));
            Assert.AreEqual("deuce", Call(score, A));
            Assert.AreEqual("advantage_receiver", Call(score, B));
            Assert.AreEqual("deuce", Call(score, A));
            Assert.AreEqual("advantage_server", Call(score, A));
            Assert.AreEqual("game", Call(score, A));
        }

        [Test]
        public void TiebreakScoresAndTheEndOfTheMatch()
        {
            var score = new TennisScore(new MatchRules { format = MatchFormat.TiebreakOnly }, A);
            Assert.AreEqual("tb_0_1", Call(score, A), "첫 포인트 뒤 서버가 바뀌어 새 서버(B) 점수 먼저");
            Assert.AreEqual("tb_1_1", Call(score, B));
            for (int i = 0; i < 6; i++) { score.AddPoint(A); score.AddPoint(B); } // 7-7
            score.AddPoint(A);
            Assert.AreEqual("num_8 + word_all", Call(score, B), "8 이상은 숫자를 이어 부른다");
            string nineEight = Call(score, A);
            Assert.IsTrue(nineEight == "num_9 + num_8" || nineEight == "num_8 + num_9", nineEight);
        }

        [Test]
        public void MatchPointWonIsGameSetAndMatch()
        {
            var score = new TennisScore(new MatchRules { format = MatchFormat.TiebreakOnly }, A);
            string last = "";
            for (int i = 0; i < 7; i++) last = Call(score, A);
            Assert.AreEqual("game_set_match", last);
        }

        [Test]
        public void EveryPossibleCallIsRecorded()
        {
            var names = new List<string> { "deuce", "advantage_server", "advantage_receiver", "game", "game_tiebreak", "game_set", "game_set_match",
                "fault", "out", "let_first", "let_second", "word_all" };
            for (int s = 0; s <= 3; s++)
                for (int r = 0; r <= 3; r++)
                    if (!(s == 0 && r == 0) && !(s == 3 && r == 3)) names.Add($"point_{s}_{r}");
            for (int a = 0; a <= 7; a++)
                for (int b = 0; b <= 7; b++)
                    if (a + b > 0) names.Add($"tb_{a}_{b}");
            for (int n = 0; n <= 15; n++) names.Add($"num_{n}");
            foreach (string name in names)
            {
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>($"{Dir}/{name}.wav");
                Assert.IsNotNull(clip, $"{name} 녹음이 없음 (ArtSource/Audio/Umpire로 만든다)");
                Assert.Less(clip.length, 4f, name);
            }
        }
    }
}
