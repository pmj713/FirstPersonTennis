namespace FirstPersonTennis
{
    public enum LaunchKind { None, Tutorial, Stage, FreePlay }

    // 메인 화면(Title 씬)에서 경기 씬(Match)으로 무엇을 시작할지 넘긴다. 경기 씬의 GameFlow가 시작할 때 한 번 읽고 지운다.
    // 아무 요청 없이 경기 씬을 바로 열면(에디터·테스트) 예전처럼 경기 메뉴가 뜬다.
    public static class GameLaunch
    {
        public const string TitleScene = "Title";
        public const string MatchScene = "Match";

        public static LaunchKind Pending { get; private set; }
        public static int StageIndex { get; private set; }
        public static int PlayerCharacter { get; private set; }

        // 메인 화면으로 돌아왔을 때 싱글플레이 창을 바로 열지
        public static bool OpenSinglePlay { get; set; }

        public static void Request(LaunchKind kind, int stageIndex = 0, int playerCharacter = 0)
        {
            Pending = kind;
            StageIndex = stageIndex;
            PlayerCharacter = playerCharacter;
        }

        public static LaunchKind Take()
        {
            LaunchKind kind = Pending;
            Pending = LaunchKind.None;
            return kind;
        }

        // 빌드에서는 프로그램을 닫고, 에디터에서는 플레이 모드를 멈춘다
        public static void QuitGame()
        {
            UserSettings.Save();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            UnityEngine.Application.Quit();
#endif
        }
    }
}
