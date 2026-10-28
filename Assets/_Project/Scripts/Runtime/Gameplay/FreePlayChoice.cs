namespace FirstPersonTennis
{
    // 자유 경기에서 고른 것: 모드(경기·랠리 연습·AI 대결 관전), 단식/복식·경기 방식·노애드, 코트, AI 난이도, 선수 넷.
    // 메인 화면의 자유 경기에서 고르면 GameLaunch로 넘어가 경기 씬이 그대로 시작한다
    public struct FreePlayChoice
    {
        public GameMode mode;
        public MatchRules rules;
        public AiDifficulty difficulty;
        public CourtType court;
        public int player, ai, partner, ai2; // 관전이면 player가 가까운 쪽 AI

        // 처음 고르기: 설정 파일의 기본값. 경기가 잠겨 있으면 열린 모드(랠리 연습)부터
        public static FreePlayChoice Default(TennisConfig config) => new FreePlayChoice
        {
            mode = StageProgress.AllCleared ? GameMode.Match : GameMode.Practice,
            rules = config.defaultRules,
            difficulty = config.defaultDifficulty,
            court = config.defaultCourt,
            player = config.defaultPlayerCharacter,
            partner = config.defaultPlayerCharacter,
            ai = config.defaultAiCharacter,
            ai2 = config.defaultAiCharacter,
        };

        // 경기는 싱글플레이 단계를 모두 깨야 열린다 (랠리 연습·관전은 언제나)
        public bool Locked => mode == GameMode.Match && !StageProgress.AllCleared;

        public bool MatchLike => mode == GameMode.Match || mode == GameMode.Watch;

        // 경기 씬에서 고른 대로 시작한다
        public void StartOn(MatchReferee referee, TennisConfig config)
        {
            config.SelectCourt(court);
            referee.SetCharacters(player, ai, partner, ai2);
            if (mode == GameMode.Match) referee.StartMatch(rules, difficulty);
            else if (mode == GameMode.Watch) referee.StartWatch(rules, difficulty);
            else referee.StartPractice(difficulty);
        }
    }
}
