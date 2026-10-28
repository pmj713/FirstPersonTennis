using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace FirstPersonTennis
{
    // 메인 화면 메뉴: 왼쪽에 위에서부터 튜토리얼 · 싱글플레이 · 멀티플레이(준비 중) · 설정 · 게임 종료.
    // 싱글플레이를 누르면 그 자리가 쉬움 · 보통 · 어려움 버튼(+ 자유 경기, 뒤로)으로 바뀐다.
    //  - 난이도를 고르면: 화면 오른쪽 끝에 그 난이도의 3단계(하드 · 클레이 · 잔디)가 세로로, 그 왼쪽에 VS를 사이에 두고 내 선수 고르기.
    //    단계는 쉬움 하드부터 이길 때마다 차례로 열린다
    //  - 자유 경기를 고르면: 같은 자리에 자유 경기 고르기(모드 · 경기 방식 · 코트 · 난이도 · 선수). 고르는 동안 뒤의 코트는 고른 코트로 멈춘다
    // 오른쪽 칸들은 왼쪽 메뉴처럼 화면 위아래를 꽉 채운다. 고른 것은 GameLaunch로 넘기고 경기 씬을 연다
    public class TitleMenu : MonoBehaviour
    {
        [SerializeField] TennisConfig config;

        static readonly string[] DifficultyNames = { "쉬움", "보통", "어려움" };
        static readonly string[] CourtNames = { "하드", "클레이", "잔디" };
        static readonly string[] FormatNames = { "타이브레이크", "숏세트 (4게임)", "1세트", "3세트 (2선승)" };
        static readonly Color[] CourtColors = { new Color(0.22f, 0.45f, 0.85f), new Color(0.85f, 0.45f, 0.2f), new Color(0.3f, 0.68f, 0.3f) };
        static readonly Color Accent = new Color(0.3f, 0.75f, 1f);
        static readonly Color PanelColor = new Color(0.02f, 0.04f, 0.07f, 0.72f); // 왼쪽 메뉴와 같은 판
        static readonly Color PowerColor = new Color(0.95f, 0.45f, 0.35f), TechniqueColor = new Color(0.55f, 0.75f, 1f), SpeedColor = new Color(0.45f, 0.9f, 0.55f);

        public bool SinglePlayOpen { get; private set; }
        public bool FreePlayOpen { get; private set; }
        public int SelectedDifficulty { get; private set; } = -1; // -1 = 아직 안 고름
        public int SelectedStage { get; private set; }

        // 자유 경기에서 고른 것 (테스트도 여기로 고른다)
        public FreePlayChoice FreePlayChoice
        {
            get => freePlay.Choice;
            set => freePlay.Choice = value;
        }

        [System.NonSerialized] HudSkin skin;
        [System.NonSerialized] GUIStyle logo, menuButton, menuButtonOff, card, heading, vs, startButton, statusLabel;
        [System.NonSerialized] readonly SettingsPanel settings = new SettingsPanel();
        [System.NonSerialized] FreePlayPanel freePlay;
        TitleCourtCycle courtCycle;
        bool confirmQuit;
        int character;
        string[] characterNames;
        string toast;
        float toastUntil;

        void Awake()
        {
            courtCycle = GetComponent<TitleCourtCycle>();
            freePlay = new FreePlayPanel(GameLaunch.HasFreePlay ? GameLaunch.FreePlay : FreePlayChoice.Default(config));
        }

        void Start()
        {
            Time.timeScale = 1f;
            CharacterPreset[] roster = config.characters;
            characterNames = new string[roster.Length];
            for (int i = 0; i < roster.Length; i++) characterNames[i] = roster[i].name;
            character = Mathf.Clamp(StageProgress.LastCharacter, 0, Mathf.Max(0, roster.Length - 1));
            SelectedStage = StageProgress.NextStage;
            if (GameLaunch.OpenFreePlay) OpenFreePlay(); // 자유 경기에서 돌아왔으면 자유 경기를 연 채로
            else if (GameLaunch.OpenSinglePlay) // 단계 도전에서 돌아왔으면 다음 단계의 난이도를 펼쳐 둔다
            {
                SinglePlayOpen = true;
                SelectedDifficulty = SelectedStage / SinglePlayStages.CourtsPerDifficulty;
            }
            GameLaunch.OpenSinglePlay = GameLaunch.OpenFreePlay = false;
        }

        void Update()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null || !kb.escapeKey.wasPressedThisFrame) return;
            if (confirmQuit) confirmQuit = false;
            else if (settings.IsOpen) settings.Close();
            else if (FreePlayOpen) CloseFreePlay(); // 한 단계씩 뒤로
            else if (SelectedDifficulty >= 0) SelectedDifficulty = -1;
            else if (SinglePlayOpen) SinglePlayOpen = false;
        }

        // ── 고르기 (버튼과 테스트가 같이 쓴다) ─────────────────

        public void StartTutorial() => Launch(LaunchKind.Tutorial);

        public void OpenSinglePlay(bool open = true)
        {
            CloseFreePlay();
            SinglePlayOpen = open;
            SelectedDifficulty = -1;
        }

        public static bool IsDifficultyUnlocked(int difficulty) =>
            StageProgress.IsUnlocked(difficulty * SinglePlayStages.CourtsPerDifficulty);

        // 난이도를 고르면 그 난이도에서 아직 못 깬 첫 단계를 골라 둔다 (다 깼으면 첫 단계)
        public void SelectDifficulty(int difficulty)
        {
            if (difficulty < 0 || difficulty >= DifficultyNames.Length) return;
            if (!IsDifficultyUnlocked(difficulty))
            {
                Toast($"{DifficultyNames[difficulty - 1]} 3단계를 모두 깨면 열려요");
                return;
            }
            CloseFreePlay();
            SelectedDifficulty = difficulty;
            int first = difficulty * SinglePlayStages.CourtsPerDifficulty;
            SelectedStage = first;
            for (int i = first; i < first + SinglePlayStages.CourtsPerDifficulty; i++)
                if (StageProgress.IsUnlocked(i) && !StageProgress.IsCleared(i)) { SelectedStage = i; break; }
        }

        public void SelectStage(int index)
        {
            if (StageProgress.IsUnlocked(index)) SelectedStage = index;
        }

        public void SelectCharacter(int index) => character = Mathf.Clamp(index, 0, characterNames.Length - 1);

        public void StartStage(int index)
        {
            if (!StageProgress.IsUnlocked(index)) return;
            StageProgress.LastCharacter = character;
            Launch(LaunchKind.Stage, index);
        }

        // 자유 경기 고르기를 연다. 뒤의 코트는 10초마다 바뀌던 것을 멈추고 고른 코트를 보여 준다
        public void OpenFreePlay()
        {
            SinglePlayOpen = true;
            FreePlayOpen = true;
            SelectedDifficulty = -1;
            if (courtCycle != null) courtCycle.enabled = false;
            config.SelectCourt(freePlay.Choice.court);
        }

        public void CloseFreePlay()
        {
            if (!FreePlayOpen) return;
            FreePlayOpen = false;
            if (courtCycle != null) courtCycle.enabled = true;
        }

        // 고른 대로 경기 씬에서 바로 시작한다. 잠긴 경기는 시작하지 않는다
        public void StartFreePlay()
        {
            if (freePlay.Choice.Locked)
            {
                Toast(FreePlayPanel.LockNote);
                return;
            }
            GameLaunch.RequestFreePlay(freePlay.Choice);
            SceneManager.LoadScene(GameLaunch.MatchScene);
        }

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

        static int ClearedIn(int difficulty)
        {
            int n = 0, first = difficulty * SinglePlayStages.CourtsPerDifficulty;
            for (int i = first; i < first + SinglePlayStages.CourtsPerDifficulty; i++) if (StageProgress.IsCleared(i)) n++;
            return n;
        }

        // ── 그리기 ────────────────────────────────────────────

        const float SideW = 440f, X = 56f, ButtonW = 320f, ButtonH = 58f, Gap = 14f;
        const float Bottom = 40f, StartH = 64f, PanelPad = 16f; // 아래 여백, 시작 버튼 높이, 오른쪽 판 안쪽 여백

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

            if (SinglePlayOpen)
            {
                DrawSinglePlaySide(h);
                if (FreePlayOpen) DrawFreePlay(w, h);
                else if (SelectedDifficulty >= 0) DrawMatchup(w, h);
            }
            else DrawMainSide(h);
            if (Time.unscaledTime < toastUntil)
            {
                var r = new Rect(w / 2f - 300f, h - 110f, 600f, 52f);
                HudSkin.Fill(r, new Color(0f, 0f, 0f, 0.8f));
                GUI.Label(r, toast, skin.TextCenter);
            }
        }

        static float TitleY(float h) => h * 0.12f;
        static float FirstButtonY(float h) => Mathf.Max(TitleY(h) + 130f, h * 0.36f);

        // 제목 두 줄 (왼쪽 메뉴의 제목과 같은 높이)
        void DrawTitle(float x, float width, float h, string title, string subtitle)
        {
            GUI.Label(new Rect(x, TitleY(h), width, 70f), title, logo);
            GUI.Label(new Rect(x + 4f, TitleY(h) + 70f, width, 26f), subtitle, skin.Small);
        }

        void DrawSidePanel(float h, string title, string subtitle)
        {
            HudSkin.Fill(new Rect(0f, 0f, SideW, h), PanelColor);
            DrawTitle(X, SideW - X, h, title, subtitle);
            GUI.Label(new Rect(X, h - 50f, SideW - X, 24f), "Esc  뒤로", skin.Small);
        }

        // 메인: 제목과 메뉴 다섯 개
        void DrawMainSide(float h)
        {
            DrawSidePanel(h, "1인칭 테니스", "공이 오기 전에 구질을 고르면, 스윙은 알아서");
            float y = FirstButtonY(h);
            if (GUI.Button(new Rect(X, y, ButtonW, ButtonH), "튜토리얼", menuButton)) StartTutorial();
            y += ButtonH + Gap;
            if (GUI.Button(new Rect(X, y, ButtonW, ButtonH), "싱글플레이", menuButton)) OpenSinglePlay();
            y += ButtonH + Gap;
            if (GUI.Button(new Rect(X, y, ButtonW, ButtonH), "멀티플레이        준비 중", menuButtonOff)) Toast("멀티플레이는 준비 중이에요");
            y += ButtonH + Gap;
            if (GUI.Button(new Rect(X, y, ButtonW, ButtonH), "설정", menuButton)) settings.Open();
            y += ButtonH + Gap;
            if (GUI.Button(new Rect(X, y, ButtonW, ButtonH), "게임 종료", menuButton)) confirmQuit = true;
        }

        // 싱글플레이: 메인 메뉴 자리에 난이도 셋 + 자유 경기 + 뒤로
        void DrawSinglePlaySide(float h)
        {
            DrawSidePanel(h, "싱글플레이", $"쉬움부터 차례로 — 이기면 다음 단계가 열려요   {StageProgress.ClearedCount} / {SinglePlayStages.Count}");
            float y = FirstButtonY(h);
            for (int d = 0; d < DifficultyNames.Length; d++)
            {
                var rect = new Rect(X, y, ButtonW, ButtonH);
                bool unlocked = IsDifficultyUnlocked(d);
                if (d == SelectedDifficulty && !FreePlayOpen) HudSkin.Fill(new Rect(X - 10f, y, 4f, ButtonH), Accent);
                if (GUI.Button(rect, DifficultyNames[d], unlocked ? menuButton : menuButtonOff)) SelectDifficulty(d);
                string info = unlocked
                    ? $"{FormatNames[(int)SinglePlayStages.FormatFor((AiDifficulty)d)]} · {ClearedIn(d)}/{SinglePlayStages.CourtsPerDifficulty}"
                    : "잠김";
                GUI.Label(new Rect(rect.x, rect.y, rect.width - 18f, rect.height), info, skin.SmallRight);
                y += ButtonH + Gap;
            }

            y += Gap;
            if (FreePlayOpen) HudSkin.Fill(new Rect(X - 10f, y, 4f, ButtonH), Accent);
            if (GUI.Button(new Rect(X, y, ButtonW, ButtonH), "자유 경기", menuButton))
            {
                if (FreePlayOpen) CloseFreePlay();
                else OpenFreePlay();
            }
            GUI.Label(new Rect(X, y, ButtonW - 18f, ButtonH), StageProgress.AllCleared ? "경기 · 연습 · 관전" : "연습 · 관전", skin.SmallRight);
            y += ButtonH + Gap;
            if (GUI.Button(new Rect(X, y, ButtonW, ButtonH), "←  메인 화면", menuButton)) OpenSinglePlay(false);
        }

        // 오른쪽 칸 자리: [내 선수]  VS  [단계] (단계 칸이 화면 오른쪽 끝)
        struct Columns
        {
            public float PlayerX, VsX, StagesX, Width, VsWidth;
        }

        static Columns Layout(float w)
        {
            float left = SideW + 40f, right = w - 40f, avail = right - left;
            float vsW = Mathf.Clamp(avail * 0.12f, 80f, 140f);
            float colW = Mathf.Min(440f, (avail - vsW) / 2f);
            float stagesX = right - colW;
            return new Columns { PlayerX = stagesX - vsW - colW, VsX = stagesX - vsW, StagesX = stagesX, Width = colW, VsWidth = vsW };
        }

        // 고른 난이도: 두 칸 모두 화면 위아래를 꽉 채운 판. 제목은 왼쪽 메뉴 제목과, 선수 버튼은 난이도 버튼과 같은 줄
        void DrawMatchup(float w, float h)
        {
            Columns c = Layout(w);
            HudSkin.Fill(new Rect(c.PlayerX - PanelPad, 0f, c.Width + 2f * PanelPad, h), PanelColor);
            HudSkin.Fill(new Rect(c.StagesX - PanelPad, 0f, w - c.StagesX + PanelPad, h), PanelColor);

            int difficulty = SelectedDifficulty;
            DrawTitle(c.PlayerX, c.Width, h, "내 선수", "어떤 유형으로 도전할지 고르세요");
            DrawTitle(c.StagesX, c.Width, h, DifficultyNames[difficulty],
                $"{FormatNames[(int)SinglePlayStages.FormatFor((AiDifficulty)difficulty)]} · 클리어 {ClearedIn(difficulty)} / {SinglePlayStages.CourtsPerDifficulty}");

            float top = FirstButtonY(h), bottom = h - Bottom;
            DrawPlayer(new Rect(c.PlayerX, top, c.Width, bottom - top));

            const float CardGap = 14f;
            float cardsBottom = bottom - StartH - 20f;
            float cardH = (cardsBottom - top - 2f * CardGap) / 3f;
            for (int i = 0; i < SinglePlayStages.CourtsPerDifficulty; i++)
                DrawStageCard(new Rect(c.StagesX, top + i * (cardH + CardGap), c.Width, cardH), difficulty * SinglePlayStages.CourtsPerDifficulty + i);
            vs.fontSize = Mathf.RoundToInt(Mathf.Clamp((c.VsWidth - 2f * PanelPad) * 0.85f, 36f, 64f)); // 두 판 사이 틈에 들어가게
            GUI.Label(new Rect(c.VsX, top, c.VsWidth, cardsBottom - top), "VS", vs);

            Stage selected = SinglePlayStages.Get(SelectedStage);
            GUI.enabled = StageProgress.IsUnlocked(SelectedStage) && selected.Difficulty == (AiDifficulty)difficulty;
            if (GUI.Button(new Rect(c.StagesX, bottom - StartH, c.Width, StartH), $"{selected.Number}단계 도전 시작", startButton)) StartStage(SelectedStage);
            GUI.enabled = true;
        }

        // 내 선수: 네 유형 버튼(세로, 오른쪽에 스탯 요약)과 고른 선수의 설명·스탯 막대
        void DrawPlayer(Rect area)
        {
            float bh = Mathf.Min(ButtonH, (area.height * 0.5f - 3f * Gap) / 4f), y = area.y;
            for (int i = 0; i < characterNames.Length; i++)
            {
                var r = new Rect(area.x, y, area.width, bh);
                if (i == character) HudSkin.Fill(new Rect(area.x - 10f, y, 4f, bh), Accent);
                if (GUI.Button(r, characterNames[i], menuButton)) SelectCharacter(i);
                CharacterPreset c = config.Character(i);
                GUI.Label(new Rect(r.x, r.y, r.width - 18f, r.height), $"힘 {c.power} · 기술 {c.technique} · 속도 {c.speed}", skin.SmallRight);
                y += bh + Gap;
            }

            y += 16f;
            CharacterPreset me = config.Character(character);
            GUI.Label(new Rect(area.x, y, area.width, 32f), me.name, heading);
            GUI.Label(new Rect(area.x, y + 34f, area.width, 24f), me.description, skin.Small);
            y += 70f;
            float barH = Mathf.Clamp((area.yMax - y) / 3f, 24f, 44f);
            StatBar(new Rect(area.x, y, area.width, barH), "힘", me.power, PowerColor);
            StatBar(new Rect(area.x, y + barH, area.width, barH), "기술", me.technique, TechniqueColor);
            StatBar(new Rect(area.x, y + 2f * barH, area.width, barH), "속도", me.speed, SpeedColor);
        }

        void StatBar(Rect r, string label, int value, Color color)
        {
            const float LabelW = 56f, NumberW = 44f;
            GUI.Label(new Rect(r.x, r.y, LabelW, r.height), label, skin.Small);
            var track = new Rect(r.x + LabelW, r.y + r.height * 0.32f, r.width - LabelW - NumberW, r.height * 0.36f);
            HudSkin.Fill(track, new Color(1f, 1f, 1f, 0.12f));
            HudSkin.Fill(new Rect(track.x, track.y, track.width * Mathf.Clamp01(value / (float)CharacterPreset.MaxStat), track.height), color);
            GUI.Label(new Rect(r.xMax - NumberW, r.y, NumberW, r.height), value.ToString(), skin.SmallRight);
        }

        // 단계 카드: 왼쪽 코트 색 띠, 단계·코트와 상태, 상대 (카드가 넉넉하면 설명과 스탯 막대까지)
        void DrawStageCard(Rect rect, int index)
        {
            Stage s = SinglePlayStages.Get(index);
            bool unlocked = StageProgress.IsUnlocked(index), cleared = StageProgress.IsCleared(index);
            if (index == SelectedStage && unlocked) HudSkin.Fill(new Rect(rect.x - 3f, rect.y - 3f, rect.width + 6f, rect.height + 6f), Accent);
            GUI.enabled = unlocked;
            if (GUI.Button(rect, GUIContent.none, card)) SelectStage(index);
            GUI.enabled = true;

            float dim = unlocked ? 1f : 0.45f;
            Color court = CourtColors[(int)s.Court];
            HudSkin.Fill(new Rect(rect.x, rect.y, 8f, rect.height), new Color(court.r, court.g, court.b, dim));
            var inner = new Rect(rect.x + 24f, rect.y + 12f, rect.width - 38f, rect.height - 24f);

            Color old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, dim);
            GUI.Label(new Rect(inner.x, inner.y, inner.width, 30f), $"{s.Number}단계   {CourtNames[(int)s.Court]} 코트", heading);
            statusLabel.normal.textColor = cleared ? SpeedColor : unlocked ? Accent : new Color(0.7f, 0.7f, 0.7f);
            GUI.Label(new Rect(inner.x, inner.y, inner.width, 30f), cleared ? "클리어" : unlocked ? "도전 가능" : "잠김", statusLabel);

            CharacterPreset opponent = config.Character(Mathf.Clamp(s.OpponentCharacter, 0, characterNames.Length - 1));
            float y = inner.y + 36f;
            GUI.Label(new Rect(inner.x, y, inner.width, 26f), $"상대  {opponent.name}  (AI {DifficultyNames[(int)s.Difficulty]})", skin.Text);
            if (inner.height >= 140f) // 막대 한 줄이 22px 넘게 나올 때
            {
                GUI.Label(new Rect(inner.x, y + 26f, inner.width, 22f), opponent.description, skin.Small);
                y += 50f;
                float barH = Mathf.Min(32f, (inner.yMax - y) / 3f);
                StatBar(new Rect(inner.x, y, inner.width, barH), "힘", opponent.power, PowerColor);
                StatBar(new Rect(inner.x, y + barH, inner.width, barH), "기술", opponent.technique, TechniqueColor);
                StatBar(new Rect(inner.x, y + 2f * barH, inner.width, barH), "속도", opponent.speed, SpeedColor);
            }
            else GUI.Label(new Rect(inner.x, y + 28f, inner.width, 22f), $"힘 {opponent.power} · 기술 {opponent.technique} · 속도 {opponent.speed}", skin.Small);
            GUI.color = old;
        }

        // 자유 경기: 오른쪽 칸 자리 전체를 위아래로 꽉 채운 판 하나. 고르기는 경기 씬 메뉴와 같은 화면(FreePlayPanel)
        void DrawFreePlay(float w, float h)
        {
            Columns c = Layout(w);
            float x = c.PlayerX, right = c.StagesX + c.Width;
            HudSkin.Fill(new Rect(x - PanelPad, 0f, w - x + PanelPad, h), PanelColor);
            DrawTitle(x, right - x, h, "자유 경기", "원하는 대로 골라서 경기해요 · 결과는 단계 기록에 들어가지 않아요");

            float top = TitleY(h) + 120f, bottom = h - Bottom;
            freePlay.Draw(new Rect(x, top, right - x, bottom - StartH - 56f - top), skin, config);

            bool locked = freePlay.Choice.Locked;
            if (locked) GUI.Label(new Rect(x, bottom - StartH - 46f, right - x, 30f), FreePlayPanel.LockNote, skin.SubBanner);
            GUI.enabled = !locked;
            string label = freePlay.Choice.mode == GameMode.Practice ? "랠리 연습 시작" : freePlay.Choice.mode == GameMode.Watch ? "관전 시작" : "경기 시작";
            if (GUI.Button(new Rect(right - 320f, bottom - StartH, 320f, StartH), label, startButton)) StartFreePlay();
            GUI.enabled = true;
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
            menuButton = new GUIStyle(skin.Button) { fontSize = 22, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(26, 12, 0, 0) };
            menuButtonOff = new GUIStyle(menuButton);
            Color off = new Color(1f, 1f, 1f, 0.4f);
            menuButtonOff.normal.textColor = menuButtonOff.hover.textColor = menuButtonOff.active.textColor = off;
            card = new GUIStyle(skin.Button);
            heading = new GUIStyle(skin.Text) { fontSize = 23, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            vs = new GUIStyle(skin.Big) { fontSize = 64, fontStyle = FontStyle.BoldAndItalic, alignment = TextAnchor.MiddleCenter };
            vs.normal.textColor = new Color(1f, 0.85f, 0.4f);
            startButton = new GUIStyle(skin.Button) { fontSize = 22, fontStyle = FontStyle.Bold };
            statusLabel = new GUIStyle(skin.SmallRight) { fontStyle = FontStyle.Bold };
        }
    }
}
