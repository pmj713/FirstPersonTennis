using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FirstPersonTennis.Tests
{
    // 실제 경기 씬의 연습 모드에서: AI가 공을 보내고, 플레이어가 이상적 위치에 서서 선입력하면 자동 스윙으로 돌려보내는지.
    // 코트마다 공이 튀는 높이가 달라도 타점을 찾아야 한다.
    public class RallySmokeTests
    {
        [UnityTest]
        public IEnumerator PlayerAtIdealSpotReturnsBalls() => Rally(CourtType.Hard);

        [UnityTest]
        public IEnumerator PlayerAtIdealSpotReturnsBallsOnClay() => Rally(CourtType.Clay);

        [UnityTest]
        public IEnumerator PlayerAtIdealSpotReturnsBallsOnGrass() => Rally(CourtType.Grass);

        [TearDown]
        public void RestoreCourt() => Court.SetSurface(CourtType.Hard, SurfaceProfile.Hard);

        static IEnumerator Rally(CourtType court)
        {
            SceneManager.LoadScene("Match");
            yield return null;
            yield return null; // 씬 오브젝트의 Start(메뉴 표시)가 끝난 뒤에 조작한다

            var referee = Object.FindAnyObjectByType<MatchReferee>();
            Assert.IsNotNull(referee, "Match 씬에 MatchReferee가 없음");
            var input = Object.FindAnyObjectByType<ShotGestureInput>();
            var swing = input.GetComponent<AutoSwing>();
            var mover = input.GetComponent<PlayerMovement>();
            referee.Config.SelectCourt(court);
            referee.StartPractice(AiDifficulty.Normal);

            int hits = 0, whiffs = 0;
            swing.SwingResolved += hit =>
            {
                if (hit) hits++;
                else whiffs++;
            };

            Time.captureDeltaTime = 1f / 60f; // 실제 시간과 무관하게 프레임당 1/60초씩 진행
            try
            {
                float start = Time.time;
                while (Time.time - start < 60f && hits < 4)
                {
                    if (swing.TryGetIdealContact(out Vector3 contact)) mover.ResetTo(swing.SpotFor(contact));
                    if (input.WindowOpen && !input.HasIntent) input.SubmitIntent(ShotIntent.Default);
                    yield return null;
                }
            }
            finally
            {
                Time.captureDeltaTime = 0f;
            }

            Assert.AreEqual(court, Court.Type);
            Assert.GreaterOrEqual(hits, 4,
                $"{court}: 맞힘 {hits}, 헛스윙 {whiffs}, 점수 {referee.PracticePlayerPoints}:{referee.PracticeAiPoints}");
            Assert.AreEqual(0, whiffs, $"{court}: 이상적 위치에서 헛스윙이 나면 안 됨");
        }
    }
}
