using NUnit.Framework;
using UnityEngine;

namespace FirstPersonTennis.Tests
{
    // 튜토리얼 발리 연습 공: 코트 길이·바닥과 상관없이 네트 앞 선수의 포핸드 쪽 가슴 높이로 낮게 지나간다
    public class VolleyFeedTests
    {
        [TearDown]
        public void RestoreCourt() => ItfDimensionsSetUp.Apply();

        [Test]
        public void FeedPassesTheNetPlayerAtChestHeight(
            [Values(1f, 1.5f)] float length, [Values(CourtType.Hard, CourtType.Clay, CourtType.Grass)] CourtType court)
        {
            Court.Configure(2f, length);
            Court.SetSurface(court, court == CourtType.Clay ? SurfaceProfile.Clay : court == CourtType.Grass ? SurfaceProfile.Grass : SurfaceProfile.Hard);
            BallPhysics.SetRadius(0.15f); // 게임의 공 크기 (지름 0.3m, 공기 저항이 크다)

            var from = new Vector3(-0.8f, 1f, Court.HalfLength + 0.8f - 0.45f); // AI 베이스라인 뒤 손에서
            var point = new Vector3(0.8f, 1.15f, -Court.ServiceLine * 0.5f);   // 네트 앞 선수의 포핸드 쪽
            float distance = new Vector2(point.x - from.x, point.z - from.z).magnitude;
            ShotSolution s = ShotSolver.SolveThrough(from, point, TutorialDirector.VolleyFeedSpeed(distance), 36f, Court.Surface);
            Assert.IsTrue(s.Success, $"길이 {length} {court}: 네트 여유 {s.NetClearance:0.00}");

            var state = new BallState(from, s.Velocity, s.Spin);
            float maxHeight = 0f, heightAtPlayer = -1f;
            for (int i = 0; i < 3000; i++)
            {
                Vector3 prev = state.Position;
                StepEvents ev = BallPhysics.Integrate(ref state, Court.Surface);
                Assert.IsFalse(ev.Bounced, "선수 자리 전에 바운드하면 안 된다");
                maxHeight = Mathf.Max(maxHeight, state.Position.y);
                if (state.Position.z <= point.z) { heightAtPlayer = Mathf.Lerp(prev.y, state.Position.y, 0.5f); break; }
            }
            Assert.AreEqual(1.15f, heightAtPlayer, 0.2f, "발리하기 좋은 가슴 높이");
            Assert.Less(maxHeight, 3.5f, "머리 위로 넘어가는 높은 공이 아니다");
        }
    }
}
