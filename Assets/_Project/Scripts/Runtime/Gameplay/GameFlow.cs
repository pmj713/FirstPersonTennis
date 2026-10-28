using UnityEngine;
using UnityEngine.SceneManagement;

namespace FirstPersonTennis
{
    // 경기 씬의 시작과 끝: 메인 화면이 요청한 것(튜토리얼·단계 도전·자유 경기)을 시작하고,
    // 단계 도전을 이기면 기록해서 다음 단계를 연다. 메뉴로 돌아갈 곳도 여기서 정한다: 메인 화면에서 왔으면 메인 화면
    // (단계 도전은 싱글플레이, 자유 경기는 자유 경기를 연 채로), 경기 씬을 바로 열었으면 경기 메뉴.
    // 심판이 Start에서 경기 메뉴를 띄운 뒤에 요청을 처리해야 해서 늦게 실행한다.
    [DefaultExecutionOrder(100)]
    public class GameFlow : MonoBehaviour
    {
        [SerializeField] TennisConfig config;
        [SerializeField] MatchReferee referee;
        [SerializeField] TutorialDirector tutorial;
        [SerializeField] AudioClip menuMusic;

        Camera matchCamera;
        FirstPersonBallCamera matchCameraRig;
        GameObject menuPresentation;

        public int StageIndex { get; private set; } = -1; // 단계 도전 중인 단계 (아니면 -1)
        public bool InStage => StageIndex >= 0;
        public Stage CurrentStage => SinglePlayStages.Get(StageIndex);
        public bool LaunchedFromTitle { get; private set; }
        public bool InFreePlay { get; private set; } // 메인 화면의 자유 경기에서 시작함
        public bool LastStageWon { get; private set; }
        public bool UnlockedNext { get; private set; } // 방금 이겨서 다음 단계가 새로 열림
        public bool HasNextStage => InStage && StageIndex + 1 < SinglePlayStages.Count;

        int playerCharacter;

        void OnEnable() => referee.MatchFinished += OnMatchFinished;
        void OnDisable() => referee.MatchFinished -= OnMatchFinished;

        void Start()
        {
            CreateMenuPresentation();
            LaunchKind kind = GameLaunch.Take();
            LaunchedFromTitle = kind != LaunchKind.None;
            switch (kind)
            {
                case LaunchKind.Tutorial:
                    tutorial.Begin();
                    break;
                case LaunchKind.Stage:
                    StartStage(GameLaunch.StageIndex, GameLaunch.PlayerCharacter);
                    break;
                case LaunchKind.FreePlay:
                    InFreePlay = true;
                    GameLaunch.FreePlay.StartOn(referee, config);
                    break;
                // None: 심판이 띄운 경기 메뉴 그대로
            }
        }

        void CreateMenuPresentation()
        {
            matchCamera = Camera.main;
            if (matchCamera == null) return;
            matchCameraRig = matchCamera.GetComponent<FirstPersonBallCamera>();
            menuPresentation = new GameObject("Free Match Menu Background");
            menuPresentation.SetActive(false);
            menuPresentation.transform.SetParent(transform);
            var camera = menuPresentation.AddComponent<Camera>();
            camera.CopyFrom(matchCamera);
            menuPresentation.AddComponent<TitleCamera>();
            var music = menuPresentation.AddComponent<AudioSource>();
            music.playOnAwake = false;
            music.clip = menuMusic;
            menuPresentation.AddComponent<TitleMusic>();
        }

        void Update()
        {
            if (menuPresentation == null) return;
            bool showMenu = referee.Phase == MatchPhase.Menu;
            if (menuPresentation.activeSelf == showMenu) return;
            matchCamera.enabled = !showMenu;
            if (matchCameraRig != null) matchCameraRig.enabled = !showMenu;
            menuPresentation.SetActive(showMenu);
        }

        public void StartStage(int index, int character)
        {
            Stage stage = SinglePlayStages.Get(index);
            StageIndex = stage.Index;
            playerCharacter = character;
            LastStageWon = UnlockedNext = false;
            config.SelectCourt(stage.Court);
            referee.SetCharacters(character, stage.OpponentCharacter);
            referee.StartMatch(stage.Rules, stage.Difficulty);
        }

        public void RetryStage() => StartStage(StageIndex, playerCharacter);

        public void NextStage()
        {
            if (HasNextStage) StartStage(StageIndex + 1, playerCharacter);
        }

        void OnMatchFinished()
        {
            if (!InStage || referee.Mode != GameMode.Match) return;
            LastStageWon = referee.Score.Winner == referee.PlayerSide;
            UnlockedNext = LastStageWon && StageProgress.MarkCleared(StageIndex) && HasNextStage;
        }

        // 메뉴로: 메인 화면에서 시작했으면 메인 화면으로 (단계 도전은 싱글플레이, 자유 경기는 자유 경기를 연 채로).
        // 경기 씬을 바로 열었으면 경기 메뉴로
        public void BackToMenu()
        {
            if (LaunchedFromTitle)
            {
                ToTitle(InStage || InFreePlay, InFreePlay);
                return;
            }
            StageIndex = -1;
            referee.ShowMenu();
        }

        // 메인 화면으로. openSinglePlay면 싱글플레이 창을, openFreePlay면 자유 경기까지 연 채로 돌아간다
        public void ToTitle(bool openSinglePlay, bool openFreePlay = false)
        {
            referee.SetPaused(false);
            Time.timeScale = 1f;
            GameLaunch.OpenSinglePlay = openSinglePlay || openFreePlay;
            GameLaunch.OpenFreePlay = openFreePlay;
            SceneManager.LoadScene(GameLaunch.TitleScene);
        }
    }
}
