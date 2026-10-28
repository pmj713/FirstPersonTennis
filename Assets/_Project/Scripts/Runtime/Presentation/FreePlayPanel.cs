using UnityEngine;

namespace FirstPersonTennis
{
    // 자유 경기 고르기 (메인 화면의 자유 경기, 경기 씬을 바로 열었을 때의 메뉴가 같이 쓴다).
    // 왼쪽 칸: 모드 · 단식/복식 · 경기 방식 · 노애드 · 코트 · AI 난이도, 오른쪽 칸: 선수 (복식이면 네 명).
    // 잠긴 경기(싱글플레이 단계를 다 깨기 전)는 설정이 보이되 고를 수 없다. 코트는 고르는 즉시 뒤의 코트에 적용한다
    public sealed class FreePlayPanel
    {
        static readonly GameMode[] Modes = { GameMode.Match, GameMode.Practice, GameMode.Watch };
        static readonly string[] ModeNames = { "경기", "랠리 연습", "AI 대결 관전" };
        static readonly string[] LockedModeNames = { "경기 · 잠김", "랠리 연습", "AI 대결 관전" };
        static readonly string[] MatchTypeNames = { "단식", "복식 (2 대 2)" };
        static readonly string[] FormatNames = { "타이브레이크", "숏세트", "1세트", "3세트" };
        static readonly string[] DifficultyNames = { "쉬움", "보통", "어려움" };
        static readonly string[] CourtNames = { "하드", "클레이", "잔디" };

        public FreePlayChoice Choice;
        string[] characterNames;

        public FreePlayPanel(FreePlayChoice choice) => Choice = choice;

        public static string LockNote =>
            $"경기는 싱글플레이 {SinglePlayStages.Count}단계를 모두 깨면 열려요 (클리어 {StageProgress.ClearedCount} / {SinglePlayStages.Count})";

        // area에 두 칸으로 그린다. 선수가 바뀌었으면 true (경기 씬 메뉴가 선수 모델을 바로 바꾸려고)
        public bool Draw(Rect area, HudSkin skin, TennisConfig config)
        {
            const float Gap = 30f;
            float cw = (area.width - Gap) / 2f;
            float row = Mathf.Clamp((area.height - 30f) / 6.2f, 60f, 80f); // 이름 줄 + 버튼 줄
            float x = area.x, y = area.y;

            int modeIndex = GUI.Toolbar(new Rect(x, y + 24f, cw, 36f), Mathf.Max(0, System.Array.IndexOf(Modes, Choice.mode)),
                StageProgress.AllCleared ? ModeNames : LockedModeNames, skin.Toolbar);
            GUI.Label(new Rect(x, y, cw, 24f), "모드", skin.Small);
            Choice.mode = Modes[modeIndex];
            y += row;

            GUI.enabled = !Choice.Locked;
            if (Choice.MatchLike)
            {
                GUI.Label(new Rect(x, y, cw, 24f), "단식 · 복식", skin.Small);
                Choice.rules.doubles = GUI.Toolbar(new Rect(x, y + 24f, cw, 36f), Choice.rules.doubles ? 1 : 0, MatchTypeNames, skin.Toolbar) == 1;
                y += row;
                GUI.Label(new Rect(x, y, cw, 24f), "경기 방식", skin.Small);
                Choice.rules.format = (MatchFormat)GUI.Toolbar(new Rect(x, y + 24f, cw, 36f), (int)Choice.rules.format, FormatNames, skin.Toolbar);
                y += row;
                Choice.rules.noAd = GUI.Toggle(new Rect(x, y, cw, 32f), Choice.rules.noAd, "  노애드 (듀스에서 다음 1점으로 게임 결정)", skin.Button);
                y += 46f;
            }
            else
            {
                GUI.Label(new Rect(x, y, cw, row * 2f), "점수 없이 상대가 주는 공으로 랠리만 합니다.\n서브는 경기 모드에서 연습하세요.", skin.Small);
                y += 62f;
            }

            GUI.Label(new Rect(x, y, cw, 24f), "코트", skin.Small);
            var court = (CourtType)GUI.Toolbar(new Rect(x, y + 24f, cw, 36f), (int)Choice.court, CourtNames, skin.Toolbar);
            if (court != Choice.court)
            {
                Choice.court = court;
                config.SelectCourt(court); // 고르는 즉시 뒤의 코트가 바뀐다
            }
            y += row;

            GUI.Label(new Rect(x, y, cw, 24f), "AI 난이도", skin.Small);
            Choice.difficulty = (AiDifficulty)GUI.Toolbar(new Rect(x, y + 24f, cw, 36f), (int)Choice.difficulty, DifficultyNames, skin.Toolbar);
            if (!Choice.Locked && Choice.difficulty == AiDifficulty.Easy && Choice.mode != GameMode.Watch)
                GUI.Label(new Rect(x, y + 62f, cw, 24f), "보조 켜짐: 낙하 지점 마커 · 타격 범위 알림 · 자동 기본 스트로크", skin.Small);
            GUI.enabled = true;

            return DrawCharacters(new Rect(x + cw + Gap, area.y, cw, area.height), skin, config, Choice.MatchLike && Choice.rules.doubles);
        }

        // 같은 선수를 여럿이 동시에 골라도 된다. 복식이면 네 명을 설명 없이 짧게
        bool DrawCharacters(Rect area, HudSkin skin, TennisConfig config, bool doubles)
        {
            CharacterPreset[] roster = config.characters;
            if (roster == null || roster.Length == 0) return false;
            if (characterNames == null || characterNames.Length != roster.Length) characterNames = new string[roster.Length];
            for (int i = 0; i < roster.Length; i++) characterNames[i] = roster[i].name;

            float y = area.y, step = Mathf.Min(doubles ? 100f : 140f, area.height / (doubles ? 4f : 2f));
            bool watch = Choice.mode == GameMode.Watch;
            int player = Pick(new Rect(area.x, y, area.width, step), watch ? (doubles ? "AI 1팀 1 (가까운 쪽)" : "AI 1 선수 (가까운 쪽)") : "내 선수",
                Choice.player, roster, !doubles, skin);
            y += step;
            int partner = Choice.partner;
            if (doubles)
            {
                partner = Pick(new Rect(area.x, y, area.width, step), watch ? "AI 1팀 2" : "파트너 (AI)", Choice.partner, roster, false, skin);
                y += step;
            }
            int ai = Pick(new Rect(area.x, y, area.width, step),
                watch ? (doubles ? "AI 2팀 1 (먼 쪽)" : "AI 2 선수 (먼 쪽)") : doubles ? "상대 1 (AI)" : "상대 선수 (AI)", Choice.ai, roster, !doubles, skin);
            y += step;
            int ai2 = Choice.ai2;
            if (doubles) ai2 = Pick(new Rect(area.x, y, area.width, step), watch ? "AI 2팀 2" : "상대 2 (AI)", Choice.ai2, roster, false, skin);

            bool changed = player != Choice.player || ai != Choice.ai || partner != Choice.partner || ai2 != Choice.ai2;
            Choice.player = player;
            Choice.ai = ai;
            Choice.partner = partner;
            Choice.ai2 = ai2;
            return changed;
        }

        int Pick(Rect r, string label, int selected, CharacterPreset[] roster, bool withDescription, HudSkin skin)
        {
            selected = Mathf.Clamp(selected, 0, roster.Length - 1);
            GUI.Label(new Rect(r.x, r.y, r.width, 24f), label, skin.Small);
            selected = GUI.Toolbar(new Rect(r.x, r.y + 24f, r.width, 36f), selected, characterNames, skin.Toolbar);
            CharacterPreset c = roster[selected];
            GUI.Label(new Rect(r.x, r.y + 62f, r.width, 30f), $"힘 {c.power} · 기술 {c.technique} · 속도 {c.speed}", withDescription ? skin.Text : skin.Small);
            if (withDescription) GUI.Label(new Rect(r.x, r.y + 92f, r.width, 24f), c.description, skin.Small);
            return selected;
        }
    }
}
