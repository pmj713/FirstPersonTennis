using UnityEngine;
using UnityEngine.SceneManagement;

namespace FirstPersonTennis
{
    // 경기 씬의 시작과 끝: 메인 화면이 요청한 것(튜토리얼·단계 도전·자유 경기)을 시작하고,
    // 단계 도전을 이기면 기록해서 다음 단계를 연다. 메뉴로 돌아갈 곳(메인 화면 / 경기 메뉴)도 여기서 정한다.
    // 심판이 Start에서 경기 메뉴를 띄운 뒤에 요청을 처리해야 해서 늦게 실행한다.
    [DefaultExecutionOrder(100)]
    public class GameFlow : MonoBehaviour
    {
        [SerializeField] TennisConfig config;
        [SerializeField] MatchReferee referee;
        [SerializeField] TutorialDirector tutorial;

        public int StageIndex { get; private set; } = -1; // 단계 도전 중인 단계 (아니면 -1)
        public bool InStage => StageIndex >= 0;
        public Stage CurrentStage => SinglePlayStages.Get(StageIndex);
        public bool LaunchedFromTitle { get; private set; }
        public bool LastStageWon { get; private set; }
        public bool UnlockedNext { get; private set; } // 방금 이겨서 다음 단계가 새로 열림
        public bool HasNextStage => InStage && StageIndex + 1 < SinglePlayStages.Count;

        int playerCharacter;

        void OnEnable() => referee.MatchFinished += OnMatchFinished;
        void OnDisable() => referee.MatchFinished -= OnMatchFinished;

        void Start()
        {
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
                // FreePlay·None: 심판이 띄운 경기 메뉴 그대로
            }
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

        // 메뉴로: 메인 화면에서 시작한 단계 도전·튜토리얼은 메인 화면으로, 자유 경기는 경기 메뉴로
        public void BackToMenu()
        {
            if (InStage || LaunchedFromTitle && referee.Mode == GameMode.Tutorial)
            {
                ToTitle(InStage);
                return;
            }
            StageIndex = -1;
            referee.ShowMenu();
        }

        // 메인 화면으로. openSinglePlay면 싱글플레이 창을 연 채로 돌아간다
        public void ToTitle(bool openSinglePlay)
        {
            referee.SetPaused(false);
            Time.timeScale = 1f;
            GameLaunch.OpenSinglePlay = openSinglePlay;
            SceneManager.LoadScene(GameLaunch.TitleScene);
        }
    }
}
