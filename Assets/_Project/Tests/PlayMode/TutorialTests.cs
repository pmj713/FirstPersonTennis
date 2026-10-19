using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FirstPersonTennis.Tests
{
    // 실제 경기 씬의 튜토리얼을 처음부터 끝까지: 사람 대신 과제마다 맞는 입력을 넣고 이상적 위치에 선다.
    // 공을 주는 연습(스매시용 로브, 네트 앞 발리)이 실제로 칠 수 있는 공인지도 여기서 확인된다.
    // 달리기 과제는 키보드 입력이 필요해 건너뛴다.
    public class TutorialTests
    {
        MatchReferee referee;
        TutorialDirector tutorial;
        ShotGestureInput input;
        AutoSwing swing;
        PlayerMovement mover;
        PlayerServe serve;

        IEnumerator LoadMatch()
        {
            SceneManager.LoadScene("Match");
            yield return null;
            yield return null; // 씬 오브젝트의 Start(메뉴 표시)가 끝난 뒤에 조작한다
            referee = Object.FindAnyObjectByType<MatchReferee>();
            tutorial = Object.FindAnyObjectByType<TutorialDirector>();
            Assert.IsNotNull(tutorial, "Match 씬에 TutorialDirector가 없음 (씬을 다시 만들어야 함)");
            input = Object.FindAnyObjectByType<ShotGestureInput>();
            swing = input.GetComponent<AutoSwing>();
            mover = input.GetComponent<PlayerMovement>();
            serve = input.GetComponent<PlayerServe>();
            Time.captureDeltaTime = 1f / 60f;
        }

        [TearDown]
        public void RestoreTime()
        {
            Time.captureDeltaTime = 0f;
            Time.timeScale = 1f;
        }

        [UnityTest]
        public IEnumerator TutorialCanBeCompleted()
        {
            yield return LoadMatch();
            var messages = new List<string>();
            tutorial.Said += messages.Add;
            tutorial.Begin();
            Assert.AreEqual(GameMode.Tutorial, referee.Mode);
            Assert.IsTrue(referee.Ai.FeedOnly, "튜토리얼 상대는 공만 준다");

            var skipped = new HashSet<string>();
            float start = Time.time;
            while (!tutorial.Completed && Time.time - start < 900f)
            {
                string task = $"{tutorial.LessonNumber}-{tutorial.TaskNumber}";
                switch (tutorial.Drill)
                {
                    case TutorialDrill.Move when tutorial.SprintRequired:
                        tutorial.SkipTask();
                        skipped.Add(task);
                        break;
                    case TutorialDrill.Move:
                        if (tutorial.TargetActive) mover.ResetTo(tutorial.TargetPosition);
                        break;
                    case TutorialDrill.Serve:
                        if (serve.MyTurn && !serve.Tossing) yield return ServeWell(IntentFor(task));
                        break;
                    default:
                        Position(tutorial.Drill);
                        if (input.WindowOpen && task == "2-2")
                        {
                            if (!input.SkipRequested) input.RequestSkip(); // 치지 않고 보내기
                        }
                        else if (input.WindowOpen && !input.HasIntent && !swing.IsSwinging)
                        {
                            input.SubmitIntent(IntentFor(task));
                        }
                        break;
                }
                yield return null;
            }

            Debug.Log($"[Tutorial] {Time.time - start:0}초 · {string.Join(" / ", messages)}");
            Assert.IsTrue(tutorial.Completed,
                $"{tutorial.LessonNumber}-{tutorial.TaskNumber} '{tutorial.TaskText}'에서 멈춤 ({tutorial.Progress}/{tutorial.Goal}). " +
                $"마지막 안내: {string.Join(" / ", messages.GetRange(Mathf.Max(0, messages.Count - 6), Mathf.Min(6, messages.Count)))}");
            CollectionAssert.AreEquivalent(new[] { "1-2" }, skipped, "건너뛴 과제는 달리기 하나뿐");
            Assert.IsFalse(messages.Contains("자동 스트로크는 세지 않아요 — 직접 입력해 보세요"));
        }

        // 튜토리얼에서 나가면 AI가 다시 받아치고, 이동 목표도 사라진다
        [UnityTest]
        public IEnumerator LeavingTutorialRestoresTheOpponent()
        {
            yield return LoadMatch();
            tutorial.Begin();
            yield return null;
            Assert.IsTrue(tutorial.TargetActive, "첫 과제는 이동 목표");

            referee.StartPractice(AiDifficulty.Normal);
            yield return null;
            Assert.IsFalse(referee.Ai.FeedOnly);
            Assert.IsFalse(tutorial.Active);
            Assert.IsFalse(tutorial.TargetActive);
        }

        // 과제 순서: 1 이동, 2 선입력, 3 구질 5종, 4 발리, 5 서브
        static ShotIntent IntentFor(string task) => task switch
        {
            "3-2" => new ShotIntent { Type = ShotType.Topspin, Depth = 1f, Aim = -0.5f },
            "3-3" => new ShotIntent { Type = ShotType.Topspin, Depth = 1f, Aim = 0.5f },
            "3-4" => new ShotIntent { Type = ShotType.Slice, Depth = 1f },
            "3-5" => new ShotIntent { Type = ShotType.Slice, Depth = 0f },
            "3-6" => new ShotIntent { Type = ShotType.Lob, Depth = 0.6f },
            "3-7" => new ShotIntent { Type = ShotType.Smash, Depth = 1f },
            "4-2" => new ShotIntent { Type = ShotType.Slice, Depth = 0.5f },
            "5-2" => new ShotIntent { Type = ShotType.Lob, Depth = 1f }, // 서브에서 ↑ = 킥
            _ => ShotIntent.Default,
        };

        void Position(TutorialDrill drill)
        {
            Vector3 contact;
            if (drill == TutorialDrill.Lob && swing.TryGetSmashContact(out contact))
                mover.ResetTo(swing.SpotFor(contact, referee.Config.smashIdealLateral));
            else if (drill == TutorialDrill.Net && swing.TryGetVolleyContact(mover.transform.position.z, out contact, out _))
                mover.ResetTo(swing.SpotFor(contact));
            else if (drill == TutorialDrill.Baseline && swing.TryGetIdealContact(out contact))
                mover.ResetTo(swing.SpotFor(contact));
        }

        IEnumerator ServeWell(ShotIntent intent)
        {
            TennisConfig config = referee.Config;
            TennisBall ball = referee.Ball;
            serve.OnPress();
            float start = Time.time;
            while (!(ball.State.Velocity.y < 0f && ball.State.Position.y <= config.serveIdealHeight))
            {
                if (Time.time - start > 5f) Assert.Fail("토스가 내려오지 않음");
                yield return null;
            }
            serve.OnRelease(intent);
        }
    }
}
