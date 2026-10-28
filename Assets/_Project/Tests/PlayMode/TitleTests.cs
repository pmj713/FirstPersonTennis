using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FirstPersonTennis.Tests
{
    // 메인 화면: 배경 카메라와 코트 바꾸기, 메뉴에서 단계 도전·튜토리얼을 시작하고 돌아오기
    public class TitleTests
    {
        const string MusicVolumeKey = "FirstPersonTennis.MusicVolume";
        int savedProgress;
        bool hadMusicVolume;
        float savedMusicVolume;

        [SetUp]
        public void SaveProgress()
        {
            savedProgress = StageProgress.Snapshot(); // 이 PC의 실제 기록·설정은 끝나면 되돌린다
            StageProgress.Restore(0);
            hadMusicVolume = PlayerPrefs.HasKey(MusicVolumeKey);
            savedMusicVolume = UserSettings.MusicVolume;
            UserSettings.MusicVolume = 1f;
        }

        [TearDown]
        public void Restore()
        {
            StageProgress.Restore(savedProgress);
            UserSettings.MusicVolume = savedMusicVolume;
            if (!hadMusicVolume) PlayerPrefs.DeleteKey(MusicVolumeKey); // 고른 적 없던 상태로 (값은 기본값 1과 같다)
            Time.captureDeltaTime = 0f;
            Time.timeScale = 1f;
            Court.SetDoubles(false);
        }

        static IEnumerator LoadTitle()
        {
            SceneManager.LoadScene(GameLaunch.TitleScene);
            yield return null;
            yield return null;
            Assert.IsNotNull(Object.FindAnyObjectByType<TitleMenu>(), "Title 씬에 TitleMenu가 없음 (씬을 다시 만들어야 함)");
        }

        static IEnumerator WaitForScene(string name, float timeout = 10f)
        {
            float start = Time.realtimeSinceStartup;
            while (SceneManager.GetActiveScene().name != name)
            {
                if (Time.realtimeSinceStartup - start > timeout) Assert.Fail($"{name} 씬으로 넘어가지 않음");
                yield return null;
            }
            yield return null;
            yield return null; // 씬 오브젝트들의 Start가 끝난 뒤
        }

        [UnityTest]
        public IEnumerator BackgroundCirclesTheCourtAndChangesItEveryTenSeconds()
        {
            yield return LoadTitle();
            var cycle = Object.FindAnyObjectByType<TitleCourtCycle>();
            Transform cam = Object.FindAnyObjectByType<TitleCamera>().transform;
            Assert.AreEqual(CourtType.Hard, Court.Type);
            Vector3 startPos = cam.position;
            float startDistance = new Vector2(startPos.x, startPos.z).magnitude;
            Assert.Greater(Mathf.Abs(startPos.z), Court.HalfLength, "카메라는 코트 밖");
            var music = Object.FindAnyObjectByType<TitleMusic>();
            Assert.IsNotNull(music, "메인 화면 배경음악이 없음");
            Assert.Less(music.Volume, music.TargetVolume * 0.5f, "음악은 작게 시작");

            Time.captureDeltaTime = 1f / 30f;
            float t0 = Time.unscaledTime;
            while (Time.unscaledTime - t0 < 9.5f) yield return null;
            Assert.AreEqual(CourtType.Hard, Court.Type, "10초 전에는 그대로");
            Assert.AreEqual(music.TargetVolume, music.Volume, 0.001f, "음악이 서서히 커져 제 크기");
            float full = music.Volume;
            UserSettings.MusicVolume = 0.5f; // 설정 화면의 배경음악 크기를 바로 따른다
            yield return null;
            Assert.AreEqual(full * 0.5f, music.Volume, 0.001f);
            UserSettings.MusicVolume = 0f;
            yield return null;
            Assert.AreEqual(0f, music.Volume, "0이면 꺼짐");
            UserSettings.MusicVolume = 1f;
            while (Time.unscaledTime - t0 < 10.6f) yield return null;
            Assert.AreEqual(CourtType.Clay, Court.Type, "10초가 지나면 다음 코트");
            Assert.AreEqual(1, cycle.Switches);
            Assert.Greater(cycle.Fade, 0f, "디졸브 중");
            while (Time.unscaledTime - t0 < 19.5f) yield return null;
            Assert.AreEqual(CourtType.Clay, Court.Type);
            Assert.AreEqual(0f, cycle.Fade, "디졸브는 다음 전환 전에 끝남");
            while (Time.unscaledTime - t0 < 20.6f) yield return null;
            Assert.AreEqual(CourtType.Grass, Court.Type, "20초에 다음 코트");

            Vector3 pos = cam.position;
            Assert.Greater(Vector3.Distance(pos, startPos), 5f, "카메라가 돌았다");
            Assert.Greater(new Vector2(pos.x, pos.z).magnitude, Court.DoublesHalfWidth + 5f, "계속 코트 밖");
            Debug.Log($"[Title] 카메라 {startPos} → {pos} (중심 거리 {startDistance:0.0} → {new Vector2(pos.x, pos.z).magnitude:0.0})");
        }

        // 메인 화면 → 1단계(쉬움 하드 타이브레이크)를 이기면 2단계가 열리고, 다음 단계(쉬움 클레이)로 바로 갈 수 있다. 메인 화면으로 돌아오면 2단계가 골라져 있다
        [UnityTest]
        public IEnumerator WinningStageOneFromTheTitleUnlocksStageTwo()
        {
            yield return LoadTitle();
            var menu = Object.FindAnyObjectByType<TitleMenu>();
            Assert.AreEqual(0, menu.SelectedStage);
            // 싱글플레이 → 난이도를 골라야 단계가 나온다. 보통은 쉬움 3단계를 다 깨야 열린다
            menu.OpenSinglePlay();
            Assert.AreEqual(-1, menu.SelectedDifficulty);
            menu.SelectDifficulty(1);
            Assert.AreEqual(-1, menu.SelectedDifficulty, "보통은 잠겨 있다");
            menu.SelectDifficulty(0);
            Assert.AreEqual(0, menu.SelectedDifficulty);
            Assert.AreEqual(0, menu.SelectedStage, "쉬움을 고르면 아직 못 깬 첫 단계가 골라진다");
            menu.StartStage(1); // 잠긴 단계는 시작하지 않는다
            yield return null;
            Assert.AreEqual(GameLaunch.TitleScene, SceneManager.GetActiveScene().name);

            menu.StartStage(0);
            yield return WaitForScene(GameLaunch.MatchScene);
            var referee = Object.FindAnyObjectByType<MatchReferee>();
            var flow = Object.FindAnyObjectByType<GameFlow>();
            Assert.IsTrue(flow.InStage);
            Assert.AreEqual(GameMode.Match, referee.Mode);
            Assert.AreEqual(AiDifficulty.Easy, referee.Ai.Difficulty);
            Assert.AreEqual(CourtType.Hard, Court.Type);
            Assert.AreEqual(MatchFormat.TiebreakOnly, referee.Score.Rules.format);

            bool finished = false;
            referee.MatchFinished += () => finished = true;
            var driver = new PlayerDriver(referee);
            Time.captureDeltaTime = 1f / 60f;
            float start = Time.time;
            while (!finished && Time.time - start < 900f) yield return driver.Step();
            Assert.IsTrue(finished, "경기가 끝나지 않음");
            Debug.Log($"[Title] 1단계 타이브레이크 {referee.Score.LastTiebreakPoints}");
            Assert.AreEqual(referee.PlayerSide, referee.Score.Winner, "쉬움 AI에게 이겨야 함");
            Assert.IsTrue(flow.LastStageWon);
            Assert.IsTrue(flow.UnlockedNext);
            Assert.IsTrue(StageProgress.IsCleared(0));
            Assert.IsTrue(StageProgress.IsUnlocked(1));
            Assert.IsFalse(StageProgress.IsUnlocked(2));

            flow.NextStage();
            yield return null;
            Assert.AreEqual(1, flow.StageIndex);
            Assert.AreEqual(CourtType.Clay, Court.Type);
            Assert.AreEqual(AiDifficulty.Easy, referee.Ai.Difficulty);
            Assert.AreEqual(MatchFormat.TiebreakOnly, referee.Score.Rules.format);
            Assert.IsFalse(referee.Score.MatchOver, "새 경기가 시작됨");

            flow.BackToMenu(); // 단계 도전 중이면 메인 화면으로
            yield return WaitForScene(GameLaunch.TitleScene);
            menu = Object.FindAnyObjectByType<TitleMenu>();
            Assert.IsTrue(menu.SinglePlayOpen, "싱글플레이 창이 열린 채로 돌아온다");
            Assert.AreEqual(1, menu.SelectedStage, "다음에 도전할 단계가 골라져 있다");
            Assert.AreEqual(0, menu.SelectedDifficulty, "그 단계의 난이도(쉬움)가 펼쳐져 있다");
            Assert.AreEqual(1f, Time.timeScale);
        }

        [UnityTest]
        public IEnumerator TutorialStartsFromTheTitleAndReturnsThere()
        {
            yield return LoadTitle();
            Object.FindAnyObjectByType<TitleMenu>().StartTutorial();
            yield return WaitForScene(GameLaunch.MatchScene);
            var referee = Object.FindAnyObjectByType<MatchReferee>();
            var flow = Object.FindAnyObjectByType<GameFlow>();
            Assert.AreEqual(GameMode.Tutorial, referee.Mode);
            Assert.IsFalse(flow.InStage);

            flow.BackToMenu();
            yield return WaitForScene(GameLaunch.TitleScene);
            Assert.IsFalse(Object.FindAnyObjectByType<TitleMenu>().SinglePlayOpen);
        }

        // 싱글플레이 → 자유 경기: 메인 화면에서 고르면(뒤의 코트도 고른 코트로) 경기 씬에서 바로 그대로 시작한다.
        // 잠긴 경기는 시작하지 않고, 메뉴로 나오면 메인 화면의 자유 경기로 돌아온다. 단계 기록에는 들어가지 않는다
        [UnityTest]
        public IEnumerator FreePlayIsChosenOnTheTitleAndStartsDirectly()
        {
            yield return LoadTitle();
            var menu = Object.FindAnyObjectByType<TitleMenu>();
            menu.OpenSinglePlay();
            menu.OpenFreePlay();
            Assert.IsTrue(menu.FreePlayOpen);

            FreePlayChoice choice = menu.FreePlayChoice;
            choice.mode = GameMode.Match; // 단계를 다 깨기 전에는 잠김
            menu.FreePlayChoice = choice;
            menu.StartFreePlay();
            yield return null;
            Assert.AreEqual(GameLaunch.TitleScene, SceneManager.GetActiveScene().name, "잠긴 경기는 시작하지 않는다");

            choice.mode = GameMode.Practice;
            choice.court = CourtType.Clay;
            choice.difficulty = AiDifficulty.Hard;
            menu.FreePlayChoice = choice;
            menu.StartFreePlay();
            yield return WaitForScene(GameLaunch.MatchScene);
            var referee = Object.FindAnyObjectByType<MatchReferee>();
            var flow = Object.FindAnyObjectByType<GameFlow>();
            Assert.AreEqual(GameMode.Practice, referee.Mode, "경기 메뉴 없이 바로 시작");
            Assert.AreEqual(CourtType.Clay, Court.Type);
            Assert.AreEqual(AiDifficulty.Hard, referee.Ai.Difficulty);
            Assert.IsTrue(flow.InFreePlay);
            Assert.IsFalse(flow.InStage);

            flow.BackToMenu();
            yield return WaitForScene(GameLaunch.TitleScene);
            menu = Object.FindAnyObjectByType<TitleMenu>();
            Assert.IsTrue(menu.FreePlayOpen, "자유 경기를 연 채로 돌아온다");
            Assert.AreEqual(CourtType.Clay, menu.FreePlayChoice.court, "지난번 선택이 남아 있다");
            Assert.AreEqual(CourtType.Clay, Court.Type, "뒤의 코트도 고른 코트");
            Assert.AreEqual(0, StageProgress.ClearedCount, "자유 경기는 단계 기록에 들어가지 않는다");
        }

        // 사람 대신: 서브는 이상적 높이에서, 랠리는 이상적 자리에 서서 좌우를 섞어 친다. 높이 뜬 공은 스매시
        sealed class PlayerDriver
        {
            readonly MatchReferee referee;
            readonly ShotGestureInput input;
            readonly AutoSwing swing;
            readonly PlayerMovement mover;
            readonly PlayerServe serve;
            readonly System.Random rng = new System.Random(5);
            ShotIntent planned;
            bool plannedForBall;

            public PlayerDriver(MatchReferee referee)
            {
                this.referee = referee;
                input = Object.FindAnyObjectByType<ShotGestureInput>();
                swing = input.GetComponent<AutoSwing>();
                mover = input.GetComponent<PlayerMovement>();
                serve = input.GetComponent<PlayerServe>();
            }

            public IEnumerator Step()
            {
                if (serve.MyTurn)
                {
                    serve.OnPress();
                    TennisBall ball = referee.Ball;
                    float start = Time.time;
                    while (!(ball.State.Velocity.y < 0f && ball.State.Position.y <= referee.Config.serveIdealHeight))
                    {
                        if (Time.time - start > 5f) Assert.Fail("토스가 내려오지 않음");
                        yield return null;
                    }
                    serve.OnRelease(ShotIntent.Default);
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
                    planned = new ShotIntent { Type = ShotType.Topspin, Depth = 1f, Aim = (float)(rng.NextDouble() * 1.2 - 0.6) };
                }
                if (swing.PredictedApex >= 3.2f && swing.TryGetSmashContact(out Vector3 smash))
                {
                    mover.ResetTo(swing.SpotFor(smash, referee.Config.smashIdealLateral));
                    planned.Type = ShotType.Smash;
                }
                else if (swing.TryGetIdealContact(out Vector3 contact))
                {
                    mover.ResetTo(swing.SpotFor(contact));
                }
                if (!input.HasIntent || input.Intent.Type != planned.Type) input.SubmitIntent(planned);
                yield return null;
            }
        }
    }
}
