using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace FirstPersonTennis.Tests
{
    // 복식 규칙: 서브 순서(A1 → B1 → A2 → B2), 리시브 코트 고정, 복식 앨리
    public class DoublesTests
    {
        const int A = -1, B = 1;

        [TearDown]
        public void BackToSingles() => Court.SetDoubles(false);

        // 서브권이 넘어올 때마다 그 팀은 지난번과 다른 선수가 넣는다. 같은 서브 게임 안에서는 같은 선수
        [Test]
        public void ServeOrderAlternatesWithinEachTeam()
        {
            var order = new DoublesOrder();
            Assert.AreEqual(0, order.ServerFor(A), "A1");
            Assert.AreEqual(0, order.ServerFor(A), "같은 게임의 다음 포인트");
            Assert.AreEqual(0, order.ServerFor(B), "B1");
            Assert.AreEqual(1, order.ServerFor(A), "A2");
            Assert.AreEqual(1, order.ServerFor(B), "B2");
            Assert.AreEqual(0, order.ServerFor(A), "다시 A1");
        }

        // 타이브레이크: 첫 포인트 뒤 두 포인트마다 서브권이 넘어가도 순서는 이어진다 (A1, B1 B1, A2 A2, B2)
        [Test]
        public void TiebreakContinuesTheRotation()
        {
            var score = new TennisScore(new MatchRules { format = MatchFormat.TiebreakOnly, doubles = true }, A);
            var order = new DoublesOrder();
            var servers = new List<string>();
            for (int point = 0; point < 6; point++)
            {
                servers.Add($"{(score.Server == A ? "A" : "B")}{order.ServerFor(score.Server) + 1}");
                score.AddPoint(point % 2 == 0 ? A : B); // 번갈아 따서 끝나지 않게
            }
            CollectionAssert.AreEqual(new[] { "A1", "B1", "B1", "A2", "A2", "B2" }, servers);
        }

        [Test]
        public void ReceiversKeepTheirCourts()
        {
            Assert.AreEqual(0, DoublesOrder.ReceiverFor(true), "선수 0이 듀스 코트");
            Assert.AreEqual(1, DoublesOrder.ReceiverFor(false), "선수 1이 애드 코트");
        }

        // 복식 앨리(단식 사이드라인과 복식 사이드라인 사이)는 복식에서만 인. 샷도 복식 너비를 노린다
        [Test]
        public void AlleyIsInOnlyInDoubles()
        {
            var alley = new Vector3(Court.SinglesHalfWidth + 0.5f, 0f, 5f);
            Court.SetDoubles(false);
            Assert.IsFalse(Court.IsInPlay(alley, B));
            float singlesWidth = Court.PlayHalfWidth;
            Court.SetDoubles(true);
            Assert.IsTrue(Court.IsInPlay(alley, B));
            Assert.Greater(Court.PlayHalfWidth, singlesWidth);
            Assert.IsFalse(Court.IsInPlay(new Vector3(Court.DoublesHalfWidth + 0.3f, 0f, 5f), B), "복식 사이드라인 밖");
        }
    }
}
