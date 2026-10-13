using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FirstPersonTennis.Tests
{
    // 실제 1단계 씬에서: 허수아비가 공을 보내고, 플레이어가 이상적 위치에 서서 선입력하면 자동 스윙으로 돌려보내는지.
    public class RallySmokeTests
    {
        [UnityTest]
        public IEnumerator PlayerAtIdealSpotReturnsBalls()
        {
            SceneManager.LoadScene("Phase1Court");
            yield return null;

            var input = Object.FindAnyObjectByType<ShotGestureInput>();
            Assert.IsNotNull(input, "Phase1Court 씬에 ShotGestureInput이 없음");
            var swing = input.GetComponent<AutoSwing>();
            var mover = input.GetComponent<PlayerMovement>();
            var referee = Object.FindAnyObjectByType<RallyReferee>();

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

            Assert.GreaterOrEqual(hits, 4, $"맞힘 {hits}, 헛스윙 {whiffs}, 점수 {referee.PlayerPoints}:{referee.DummyPoints}");
            Assert.AreEqual(0, whiffs, "이상적 위치에서 헛스윙이 나면 안 됨");
        }
    }
}
