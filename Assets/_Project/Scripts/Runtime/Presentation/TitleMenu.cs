using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace FirstPersonTennis
{
    // 메인 화면 메뉴: 왼쪽에 위에서부터 튜토리얼 · 싱글플레이 · 멀티플레이(준비 중) · 설정 · 게임 종료.
    // 싱글플레이 창은 단계 도전(9단계, 쉬움 하드부터 이길 때마다 다음 단계가 열림)과 자유 경기(경기 씬의 상세 메뉴)로 나뉜다.
    // 고른 것은 GameLaunch로 넘기고 경기 씬을 연다
    public class TitleMenu : MonoBehaviour
    {
        [SerializeField] TennisConfig config;

        static readonly string[] DifficultyNames = { "쉬움", "보통", "어려움" };
        static readonly string[] CourtNames = { "하드", "클레이", "잔디" };
        static readonly string[] FormatNames = { "타이브레이크", "숏세트 (4게임)", "1세트", "3세트 (2선승)" };
        static readonly string[] TabNames = { "단계 도전", "자유 경기" };

        public bool SinglePlayOpen { get; private set; }
        public int SelectedStage { get; private set; }

        [System.NonSerialized] HudSkin skin;
        [System.NonSerialized] GUIStyle logo, logoSmall, menuButton, menuButtonOff, cell;
        [System.NonSerialized] readonly SettingsPanel settings = new SettingsPanel();
        bool confirmQuit;
        int tab;
        int character;
        string[] characterNames;
        string toast;
        float toastUntil;

        void Start()
        {
            Time.timeScale = 1f;
            CharacterPreset[] roster = config.characters;
            characterNames = new string[roster.Length];
            for (int i = 0; i < roster.Length; i++) characterNames[i] = roster[i].name;
            character = Mathf.Clamp(StageProgress.LastCharacter, 0, Mathf.Max(0, roster.Length - 1));
            SelectedStage = StageProgress.NextStage;
            if (GameLaunch.OpenSinglePlay)
            {
                SinglePlayOpen = true;
                GameLaunch.OpenSinglePlay = false;
            }
        }

        void Update()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null || !kb.escapeKey.wasPressedThisFrame) return;
            if (confirmQuit) confirmQuit = false;
            else if (settings.IsOpen) settings.Close();
            else if (SinglePlayOpen) SinglePlayOpen = false;
        }

        // ── 고르기 (버튼과 테스트가 같이 쓴다) ─────────────────

        public void StartTutorial() => Launch(LaunchKind.Tutorial);

        public void OpenSinglePlay(bool open = true) => SinglePlayOpen = open;

        public void SelectStage(int index)
        {
            if (StageProgress.IsUnlocked(index)) SelectedStage = index;
        }

        public void StartStage(int index)
        {
            if (!StageProgress.IsUnlocked(index)) return;
            StageProgress.LastCharacter = character;
            Launch(LaunchKind.Stage, index);
        }

        public void StartFreePlay() => Launch(LaunchKind.FreePlay);

        void Launch(LaunchKind kind, int stage = 0)
        {
            GameLaunch.Request(kind, stage, character);
            SceneManager.LoadScene(GameLaunch.MatchScene);
        }

        void Toast(string value)
        {
            toast = value;
            toastUntil = Time.unscaledTime + 2f;
        }

        // ── 그리기 ────────────────────────────────────────────

        void OnGUI()
        {
            EnsureStyles();
            GUI.depth = 0;
            float w = Screen.width, h = Screen.height;
            if (settings.IsOpen)
            {
                settings.Draw(w, h, skin, config.horizontalFov);
                return;
            }
            if (confirmQuit)
            {
                DrawQuitConfirm(w, h);
                return;
            }

            DrawSide(w, h);
            if (SinglePlayOpen) DrawSinglePlay(w, h);
            if (Time.unscaledTime < toastUntil)
            {
                var r = new Rect(w / 2f - 260f, h - 110f, 520f, 52f);
                HudSkin.Fill(r, new Color(0f, 0f, 0f, 0.7f));
                GUI.Label(r, toast, skin.TextCenter);
            }
        }

        // 왼쪽: 제목과 메뉴 다섯 개
        void DrawSide(float w, float h)
        {
            const float SideW = 440f, X = 56f, ButtonW = 320f, ButtonH = 58f, Gap = 14f;
            HudSkin.Fill(new Rect(0f, 0f, SideW, h), new Color(0.02f, 0.04f, 0.07f, 0.72f));
            GUI.Label(new Rect(X, h * 0.12f, SideW - X, 70f), "1인칭 테니스", logo);
            GUI.Label(new Rect(X + 4f, h * 0.12f + 70f, SideW - X, 26f), "공이 오기 전에 구질을 고르면, 스윙은 알아서", skin.Small);

            float y = Mathf.Max(h * 0.12f + 130f, h * 0.36f);
            if (GUI.Button(new Rect(X, y, ButtonW, ButtonH), "튜토리얼", menuButton)) StartTutorial();
            y += ButtonH + Gap;
            if (SinglePlayOpen) HudSkin.Fill(new Rect(X - 10f, y, 4f, ButtonH), new Color(0.3f, 0.75f, 1f)); // 열려 있는 메뉴 표시
            if (GUI.Button(new Rect(X, y, ButtonW, ButtonH), "싱글플레이", menuButton)) OpenSinglePlay(!SinglePlayOpen);
            y += ButtonH + Gap;
            if (GUI.Button(new Rect(X, y, ButtonW, ButtonH), "멀티플레이        준비 중", menuButtonOff)) Toast("멀티플레이는 준비 중이에요");
            y += ButtonH + Gap;
            if (GUI.Button(new Rect(X, y, ButtonW, ButtonH), "설정", menuButton)) settings.Open();
            y += ButtonH + Gap;
            if (GUI.Button(new Rect(X, y, ButtonW, ButtonH), "게임 종료", menuButton)) confirmQuit = true;

            GUI.Label(new Rect(X, h - 50f, SideW - X, 24f), "Esc  뒤로", skin.Small);
        }

        // 오른쪽: 싱글플레이 창 (단계 도전 / 자유 경기)
        void DrawSinglePlay(float w, float h)
        {
            float left = 480f;
            float pw = Mathf.Min(880f, w - left - 40f), ph = Mathf.Min(640f, h - 40f);
            var panel = new Rect(left + Mathf.Max(0f, (w - left - pw) / 2f), (h - ph) / 2f, pw, ph);
            HudSkin.Fill(panel, new Color(0.05f, 0.08f, 0.12f, 0.92f));
            GUI.Label(new Rect(panel.x + 28f, panel.y + 18f, 300f, 40f), "싱글플레이", logoSmall);
            tab = GUI.Toolbar(new Rect(panel.xMax - 348f, panel.y + 22f, 320f, 36f), tab, TabNames, skin.Toolbar);

            var body = new Rect(panel.x + 28f, panel.y + 76f, pw - 56f, ph - 96f);
            if (tab == 0) DrawStages(body);
            else DrawFreePlay(body);
        }

        void DrawStages(Rect area)
        {
            GUI.Label(new Rect(area.x, area.y, area.width, 24f),
                $"쉬움 하드부터 차례로 도전해요. 이기면 다음 단계가 열려요     클리어 {StageProgress.ClearedCount} / {SinglePlayStages.Count}", skin.Small);

            // 3 × 3: 줄 = 난이도(경기 길이), 칸 = 코트
            const float RowLabelW = 150f, HeaderH = 26f, CellH = 64f, Gap = 8f;
            float gridY = area.y + 34f;
            float cellW = (area.width - RowLabelW - Gap * 3f) / 3f;
            for (int c = 0; c < 3; c++)
                GUI.Label(new Rect(area.x + RowLabelW + Gap + c * (cellW + Gap), gridY, cellW, HeaderH), CourtNames[c], skin.SmallCenter);
            for (int r = 0; r < 3; r++)
            {
                float y = gridY + HeaderH + r * (CellH + Gap);
                GUI.Label(new Rect(area.x, y + 6f, RowLabelW, 26f), DifficultyNames[r], skin.Text);
                GUI.Label(new Rect(area.x, y + 34f, RowLabelW, 22f), FormatNames[(int)SinglePlayStages.FormatFor((AiDifficulty)r)], skin.Small);
                for (int c = 0; c < 3; c++)
                {
                    int index = r * SinglePlayStages.CourtsPerDifficulty + c;
                    Stage s = SinglePlayStages.Get(index);
                    var rect = new Rect(area.x + RowLabelW + Gap + c * (cellW + Gap), y, cellW, CellH);
                    bool unlocked = StageProgress.IsUnlocked(index), cleared = StageProgress.IsCleared(index);
                    if (index == SelectedStage) HudSkin.Fill(new Rect(rect.x - 3f, rect.y - 3f, rect.width + 6f, rect.height + 6f), new Color(0.3f, 0.75f, 1f));
                    if (cleared) HudSkin.Fill(rect, new Color(0.15f, 0.45f, 0.25f, 0.9f));
                    string status = cleared ? "클리어" : unlocked ? "도전 가능" : "잠김";
                    string label = $"{s.Number}단계 · {status}\nvs {characterNames[Mathf.Clamp(s.OpponentCharacter, 0, characterNames.Length - 1)]}";
                    GUI.enabled = unlocked;
                    if (GUI.Button(rect, label, cell)) SelectStage(index);
                    GUI.enabled = true;
                }
            }

            // 내 선수와 고른 단계
            float py = gridY + HeaderH + 3f * (CellH + Gap) + 14f;
            GUI.Label(new Rect(area.x, py, 200f, 26f), "내 선수", skin.Small);
            character = GUI.Toolbar(new Rect(area.x, py + 26f, area.width, 36f), character, characterNames, skin.Toolbar);
            CharacterPreset me = config.Character(character);
            GUI.Label(new Rect(area.x, py + 66f, area.width, 24f),
                $"힘 {me.power} · 기술 {me.technique} · 속도 {me.speed} — {me.description}", skin.Small);

            Stage selected = SinglePlayStages.Get(SelectedStage);
            string opponent = characterNames[Mathf.Clamp(selected.OpponentCharacter, 0, characterNames.Length - 1)];
            GUI.Label(new Rect(area.x, area.yMax - 54f, area.width - 240f, 26f),
                $"{selected.Number}단계: {DifficultyNames[(int)selected.Difficulty]} · {CourtNames[(int)selected.Court]} · " +
                $"{FormatNames[(int)selected.Format]}", skin.Text);
            GUI.Label(new Rect(area.x, area.yMax - 28f, area.width - 240f, 22f), $"상대 {opponent} (AI {DifficultyNames[(int)selected.Difficulty]})", skin.Small);
            GUI.enabled = StageProgress.IsUnlocked(SelectedStage);
            if (GUI.Button(new Rect(area.xMax - 220f, area.yMax - 58f, 220f, 54f), "도전 시작", skin.Button)) StartStage(SelectedStage);
            GUI.enabled = true;
        }

        void DrawFreePlay(Rect area)
        {
            string match = StageProgress.AllCleared
                ? "· 경기: 단식 · 복식(2 대 2), 경기 방식, 코트, AI 난이도, 선수를 직접 고름\n"
                : $"· 경기: 단계 도전 {SinglePlayStages.Count}단계를 모두 깨면 열려요 (클리어 {StageProgress.ClearedCount} / {SinglePlayStages.Count})\n";
            GUI.Label(new Rect(area.x, area.y + 10f, area.width, 140f),
                "원하는 대로 골라서 경기해요.\n\n" + match +
                "· 랠리 연습: 점수 없이 상대가 주는 공으로 랠리\n" +
                "· AI 대결 관전: AI끼리 하는 경기를 보기\n\n" +
                "자유 경기 결과는 단계 기록에 들어가지 않아요.", skin.Text);
            if (GUI.Button(new Rect(area.x, area.y + 250f, 280f, 56f), "자유 경기 메뉴 열기", skin.Button)) StartFreePlay();
        }

        // 실수로 끄지 않게 한 번 더 묻는다
        void DrawQuitConfirm(float w, float h)
        {
            HudSkin.Fill(new Rect(0f, 0f, w, h), new Color(0f, 0f, 0f, 0.6f));
            float pw = 440f, ph = 170f;
            var panel = new Rect((w - pw) / 2f, (h - ph) / 2f, pw, ph);
            HudSkin.Fill(panel, new Color(0.05f, 0.08f, 0.12f, 0.95f));
            GUI.Label(new Rect(panel.x, panel.y + 26f, pw, 40f), "게임을 종료할까요?", skin.Title);
            float by = panel.yMax - 70f;
            if (GUI.Button(new Rect(panel.center.x - 190f, by, 180f, 48f), "종료", skin.Button)) GameLaunch.QuitGame();
            if (GUI.Button(new Rect(panel.center.x + 10f, by, 180f, 48f), "취소 (Esc)", skin.Button)) confirmQuit = false;
        }

        void EnsureStyles()
        {
            if (skin != null && skin.Valid) return;
            skin = HudSkin.Create();
            logo = new GUIStyle(skin.Big) { fontSize = 52, alignment = TextAnchor.MiddleLeft };
            logoSmall = new GUIStyle(skin.Big) { fontSize = 30, alignment = TextAnchor.MiddleLeft };
            menuButton = new GUIStyle(skin.Button) { fontSize = 22, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(26, 12, 0, 0) };
            menuButtonOff = new GUIStyle(menuButton);
            Color off = new Color(1f, 1f, 1f, 0.4f);
            menuButtonOff.normal.textColor = menuButtonOff.hover.textColor = menuButtonOff.active.textColor = off;
            cell = new GUIStyle(skin.Button) { fontSize = 16, alignment = TextAnchor.MiddleCenter, wordWrap = true };
        }
    }
}
