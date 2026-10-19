using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FirstPersonTennis.Tests
{
    // 실제 경기 씬에서 서브·판정·점수가 이어지는지. 플레이어는 테스트가 대신 움직인다.
    public class MatchFlowTests
    {
        MatchReferee referee;
        ShotGestureInput input;
        AutoSwing swing;
        PlayerMovement mover;
        PlayerServe serve;
        readonly List<string> calls = new List<string>();
        readonly List<PointReason> reasons = new List<PointReason>();

        IEnumerator LoadMatch()
        {
            SceneManager.LoadScene("Match");
            yield return null;
            yield return null; // 씬 오브젝트의 Start(메뉴 표시)가 끝난 뒤에 조작한다
            referee = Object.FindAnyObjectByType<MatchReferee>();
            Assert.IsNotNull(referee, "Match 씬에 MatchReferee가 없음");
            input = Object.FindAnyObjectByType<ShotGestureInput>();
            swing = input.GetComponent<AutoSwing>();
            mover = input.GetComponent<PlayerMovement>();
            serve = input.GetComponent<PlayerServe>();
            calls.Clear();
            reasons.Clear();
            referee.Called += calls.Add;
            referee.PointEnded += (_, reason) => reasons.Add(reason);
            Time.captureDeltaTime = 1f / 60f;
        }

        [TearDown]
        public void RestoreTime()
        {
            Time.captureDeltaTime = 0f;
            Time.timeScale = 1f;
        }

        // 두 번 연속 헛스윙 = 폴트 뒤 더블 폴트, 상대 득점
        [UnityTest]
        public IEnumerator TwoWhiffedServesAreADoubleFault()
        {
            yield return LoadMatch();
            var rules = new MatchRules { format = MatchFormat.OneSet };
            referee.StartMatch(rules, AiDifficulty.Normal, referee.PlayerSide);

            yield return WaitUntil(() => serve.MyTurn, 5f, "첫 서브 차례가 오지 않음");
            serve.OnPress();
            serve.OnRelease(ShotIntent.Default); // 손 높이에서 바로 놓음 → 헛스윙
            Assert.AreEqual(2, referee.ServeNumber);
            Assert.Contains("헛스윙 — 폴트", calls);

            yield return WaitUntil(() => serve.MyTurn, 5f, "두 번째 서브 차례가 오지 않음");
            Assert.IsTrue(serve.SecondServe);
            serve.OnPress();
            serve.OnRelease(ShotIntent.Default);

            Assert.AreEqual(1, referee.Score.Points(referee.AiSide));
            Assert.AreEqual(0, referee.Score.Points(referee.PlayerSide));
            Assert.AreEqual(PointReason.DoubleFault, reasons[reasons.Count - 1]);
            Assert.AreEqual(1, referee.DoubleFaults(referee.PlayerSide));
            Assert.AreEqual(1, referee.ServeNumber, "다음 포인트는 첫 서브부터");
        }

        // 좋은 타이밍으로 넣은 서브는 박스에 들어가고 랠리가 시작된다
        [UnityTest]
        public IEnumerator WellTimedServeStartsARally()
        {
            yield return LoadMatch();
            referee.StartMatch(new MatchRules { format = MatchFormat.OneSet }, AiDifficulty.Normal, referee.PlayerSide);
            yield return WaitUntil(() => serve.MyTurn, 5f, "서브 차례가 오지 않음");

            for (int attempt = 0; attempt < 2 && referee.Phase != MatchPhase.Rally; attempt++)
            {
                yield return WaitUntil(() => serve.MyTurn, 5f, "서브 차례가 오지 않음");
                yield return ServeWell();
                yield return WaitUntil(() => referee.Ball.IsServe && referee.Ball.BouncesSinceHit >= 1 ||
                                             referee.Phase != MatchPhase.Rally, 5f, "서브 공이 바운드하지 않음");
            }
            Assert.IsTrue(referee.Ball.IsServe || referee.Ball.LastHitterSide == referee.AiSide,
                $"서브가 들어가야 함. 콜: {string.Join(", ", calls)}");
        }

        // 보조(낙하 지점 마커·범위 알림·자동 기본 스트로크)는 쉬움에서만. 튜토리얼은 표시만 켠다
        [UnityTest]
        public IEnumerator AssistsTurnOnOnlyForEasy()
        {
            yield return LoadMatch();
            referee.StartPractice(AiDifficulty.Easy);
            Assert.IsTrue(referee.AssistDisplays);
            Assert.IsTrue(swing.AutoStroke);

            referee.StartMatch(new MatchRules { format = MatchFormat.OneSet }, AiDifficulty.Normal);
            Assert.IsFalse(referee.AssistDisplays);
            Assert.IsFalse(swing.AutoStroke);

            referee.StartMatch(new MatchRules { format = MatchFormat.OneSet }, AiDifficulty.Easy);
            Assert.IsTrue(referee.AssistDisplays);
            Assert.IsTrue(swing.AutoStroke);

            Object.FindAnyObjectByType<TutorialDirector>().Begin();
            Assert.IsTrue(referee.AssistDisplays, "튜토리얼은 표시 보조를 켠다");
            Assert.IsFalse(swing.AutoStroke, "튜토리얼은 자동 스트로크를 끈다");
        }

        // 타이브레이크 한 판을 끝까지: 서브, 리턴, 랠리 판정, 점수, 경기 종료
        [UnityTest]
        public IEnumerator TiebreakMatchPlaysToTheEnd()
        {
            yield return LoadMatch();
            bool finished = false;
            referee.MatchFinished += () => finished = true;
            referee.StartMatch(new MatchRules { format = MatchFormat.TiebreakOnly }, AiDifficulty.Easy);

            float start = Time.time;
            while (!finished && Time.time - start < 900f)
            {
                yield return PlayerStep();
            }

            Debug.Log($"[MatchFlow] 타이브레이크 {referee.Score.LastTiebreakPoints} · 경기 시간 {Time.time - start:0}초 · " +
                      $"포인트 이유: {string.Join(", ", reasons)} · 콜: {string.Join(", ", calls)}");
            Assert.IsTrue(finished, $"경기가 끝나지 않음. 콜: {string.Join(", ", calls)}");
            Assert.AreEqual(MatchPhase.MatchOver, referee.Phase);
            TennisScore score = referee.Score;
            Assert.IsTrue(score.MatchOver);
            Vector2Int tb = score.LastTiebreakPoints;
            int winnerPoints = Mathf.Max(tb.x, tb.y), loserPoints = Mathf.Min(tb.x, tb.y);
            Assert.GreaterOrEqual(winnerPoints, TennisScore.TiebreakTarget);
            Assert.GreaterOrEqual(winnerPoints - loserPoints, 2);
            Assert.AreEqual(winnerPoints + loserPoints,
                referee.PointsWon(referee.PlayerSide) + referee.PointsWon(referee.AiSide));
            Assert.IsFalse(mover.InputEnabled, "경기가 끝나면 입력을 막는다");
        }

        // 사람 대신: 서브는 이상적 높이에서, 랠리는 이상적 위치에 서서 좌우를 섞어 친다. 높이 뜬 공(로브)은 스매시.
        IEnumerator PlayerStep()
        {
            if (serve.MyTurn)
            {
                yield return ServeWell();
                yield break;
            }
            if (!input.WindowOpen)
            {
                plannedForBall = false;
                yield return null;
                yield break;
            }

            if (!plannedForBall)
            {
                plannedForBall = true;
                planned = new ShotIntent { Type = ShotType.Topspin, Depth = 1f, Aim = (float)(aimRng.NextDouble() * 1.2 - 0.6) };
            }
            Vector3 smashContact = default;
            if (swing.PredictedApex >= 3.2f && swing.TryGetSmashContact(out smashContact))
            {
                mover.ResetTo(swing.SpotFor(smashContact, referee.Config.smashIdealLateral));
                planned.Type = ShotType.Smash;
            }
            else if (swing.TryGetIdealContact(out Vector3 contact))
            {
                mover.ResetTo(swing.SpotFor(contact));
            }
            if (!input.HasIntent || input.Intent.Type != planned.Type) input.SubmitIntent(planned);
            yield return null;
        }

        readonly System.Random aimRng = new System.Random(5);
        ShotIntent planned;
        bool plannedForBall;

        IEnumerator ServeWell()
        {
            TennisConfig config = referee.Config;
            TennisBall ball = referee.Ball;
            serve.OnPress();
            yield return WaitUntil(() => ball.State.Velocity.y < 0f && ball.State.Position.y <= config.serveIdealHeight,
                5f, "토스가 내려오지 않음");
            serve.OnRelease(ShotIntent.Default);
        }

        static IEnumerator WaitUntil(System.Func<bool> condition, float timeout, string message)
        {
            float start = Time.time;
            while (!condition())
            {
                if (Time.time - start > timeout) Assert.Fail(message);
                yield return null;
            }
        }
    }
}
