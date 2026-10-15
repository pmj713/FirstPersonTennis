using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FirstPersonTennis
{
    // 프로토타입용 IMGUI HUD: 시작 메뉴, 점수판, 심판 콜, 서브 토스 미터, 일시정지, 경기 결과. 정식 UI는 이후 단계에서 교체.
    public class TennisHud : MonoBehaviour
    {
        [SerializeField] TennisConfig config;
        [SerializeField] MatchReferee referee;
        [SerializeField] PlayerMovement player;
        [SerializeField] ShotGestureInput input;
        [SerializeField] AutoSwing playerSwing;
        [SerializeField] PlayerServe playerServe;
        [SerializeField] FirstPersonBallCamera cameraRig;

        static readonly string[] HelpLines =
        {
            "WASD  이동 (W = 네트 쪽) · Shift  달리기",
            "좌클릭 드래그  ←→ 방향 · ↓ 슬라이스 · ↑ 로브 · ↑↓ 스매시",
            "클릭만  탑스핀 가운데 · 우클릭  이번 공 보내기",
            "네트 앞(서비스라인 안)에서는 바운드 전에 발리",
            "서브  A/D 위치 · 누르면 토스 · 드래그 ←→ 코너, ↓ 슬라이스, ↑ 킥 · 초록 칸에서 놓기",
            "- =  공 템포 · M 미니맵 · V 시점 · [ ] 시야각 · R 범위 링",
            "T 슬로모션 · Esc 일시정지 · H 도움말",
        };

        static readonly string[] ModeNames = { "경기", "랠리 연습", "AI 대결 관전" };
        static readonly string[] FormatNames = { "타이브레이크", "숏세트 (4게임)", "1세트", "3세트 (2선승)" };
        static readonly string[] DifficultyNames = { "쉬움", "보통", "어려움" };
        static readonly string[] CourtNames = { "하드", "클레이", "잔디" };

        GUIStyle text, small, title, banner, subBanner, textCenter, smallCenter, smallRight, button, toolbar, big;
        string message, subMessage;
        float messageUntil, subMessageUntil;
        ShotQuality lastQuality;
        string qualityReasons;
        float qualityUntil;
        bool showHelp = true;
        bool showMinimap;
        Texture2D dot;

        // 시작 메뉴 선택
        GameMode menuMode = GameMode.Match;
        MatchRules menuRules;
        AiDifficulty menuDifficulty;
        int menuPlayerCharacter, menuAiCharacter;
        string[] characterNames;

        void Start()
        {
            showMinimap = config.showMinimap;
            menuRules = config.defaultRules;
            menuDifficulty = config.defaultDifficulty;
            menuPlayerCharacter = config.defaultPlayerCharacter;
            menuAiCharacter = config.defaultAiCharacter;
            dot = CreateDotTexture();
        }

        void OnDestroy()
        {
            if (dot != null) Destroy(dot);
        }

        void OnEnable()
        {
            referee.PointEnded += OnPointEnded;
            referee.Called += OnCalled;
            referee.ScoreCalled += OnScoreCalled;
            playerSwing.SwingResolved += OnSwingResolved;
            playerSwing.Struck += OnStruck;
            input.IntentRejected += OnIntentRejected;
        }

        void OnDisable()
        {
            referee.PointEnded -= OnPointEnded;
            referee.Called -= OnCalled;
            referee.ScoreCalled -= OnScoreCalled;
            playerSwing.SwingResolved -= OnSwingResolved;
            playerSwing.Struck -= OnStruck;
            input.IntentRejected -= OnIntentRejected;
        }

        bool InPlay => referee.Phase != MatchPhase.Menu && referee.Phase != MatchPhase.MatchOver && !referee.Paused;

        void OnIntentRejected()
        {
            if (InPlay) Show(playerSwing.IsSwinging ? "이미 스윙 중" : "상대가 친 뒤에 입력하세요");
        }

        void Update()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null) return;
            if (kb.escapeKey.wasPressedThisFrame && referee.Phase != MatchPhase.Menu && referee.Phase != MatchPhase.MatchOver)
                referee.SetPaused(!referee.Paused);
            if (kb.hKey.wasPressedThisFrame) showHelp = !showHelp;
            if (kb.mKey.wasPressedThisFrame) showMinimap = !showMinimap;
            if (kb.tKey.wasPressedThisFrame && !referee.Paused) Time.timeScale = Time.timeScale < 1f ? 1f : 0.5f;
            if (kb.minusKey.wasPressedThisFrame) ChangeBallTempo(-0.05f);
            if (kb.equalsKey.wasPressedThisFrame) ChangeBallTempo(0.05f);
        }

        // 공의 시간 배율을 바꾼다. 설정 파일에 저장되어 다음 플레이에도 유지된다.
        void ChangeBallTempo(float delta)
        {
            config.ballTimeScale = Mathf.Clamp(Mathf.Round((config.ballTimeScale + delta) * 100f) / 100f, 0.4f, 2f);
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(config);
#endif
            Show($"공 템포 {config.ballTimeScale:0.00}");
        }

        void OnPointEnded(int winner, PointReason reason)
        {
            bool won = winner == player.Side;
            string what = reason switch
            {
                PointReason.Out => won ? "상대 샷 아웃" : "아웃",
                PointReason.Net => won ? "상대 샷 네트" : "네트",
                PointReason.Ace => won ? "에이스!" : "에이스 허용",
                PointReason.DoubleFault => won ? "상대 더블 폴트" : "더블 폴트",
                _ => won ? "위너" : "놓침",
            };
            Show(won ? $"{what} — 득점" : $"{what} — 실점");
        }

        void OnCalled(string call) => Show(call);

        void OnScoreCalled(string call)
        {
            subMessage = call;
            subMessageUntil = Time.unscaledTime + 2.2f;
        }

        void OnSwingResolved(bool hit)
        {
            if (!hit && InPlay) Show("헛스윙");
        }

        void OnStruck(ShotQuality quality)
        {
            if (!InPlay) return;
            lastQuality = quality;
            qualityReasons = QualityReasons(quality);
            qualityUntil = Time.unscaledTime + 2f;
        }

        // 벌점이 큰 원인부터 두 개
        static string QualityReasons(ShotQuality q)
        {
            const float Notable = 0.06f;
            var items = new List<(float value, string text)>();
            if (q.IsServe)
            {
                if (q.Timing >= Notable) items.Add((q.Timing, q.Early ? "공이 높을 때 침 (빠름)" : "공이 떨어진 뒤 침 (늦음)"));
            }
            else
            {
                if (q.Reach >= Notable) items.Add((q.Reach, q.TooClose ? "공이 몸에 너무 붙음" : "공이 몸에서 멂"));
                if (q.Height >= Notable) items.Add((q.Height, q.TooLow ? "타점 낮음" : "타점 높음"));
                if (q.Running > 0f) items.Add((q.Running, "뛰면서 침"));
                if (q.Tired > 0f) items.Add((q.Tired, "스테미너 바닥"));
                if (q.Aim >= Notable) items.Add((q.Aim, "방향을 크게 줌"));
                if (q.BallSpeed >= Notable) items.Add((q.BallSpeed, "빠른 공"));
                if (q.Power >= Notable) items.Add((q.Power, "강하게 침"));
            }
            items.Sort((a, b) => b.value.CompareTo(a.value));
            if (items.Count > 2) items.RemoveRange(2, items.Count - 2);
            return string.Join(" · ", items.ConvertAll(i => i.text));
        }

        void Show(string value)
        {
            message = value;
            messageUntil = Time.unscaledTime + 1.6f;
        }

        void OnGUI()
        {
            EnsureStyles();
            float w = Screen.width, h = Screen.height;

            if (referee.Phase == MatchPhase.Menu)
            {
                DrawMenu(w, h);
                return;
            }

            if (referee.Mode != GameMode.Practice) DrawScoreboard(w);
            else DrawPracticeScore(w);

            if (Time.unscaledTime < messageUntil)
                GUI.Label(new Rect(0f, h * 0.2f, w, 52f), message, banner);
            if (Time.unscaledTime < subMessageUntil)
                GUI.Label(new Rect(0f, h * 0.2f + 52f, w, 40f), subMessage, subBanner);

            if (referee.Watching)
            {
                DrawWatchInfo(w, h);
            }
            else
            {
                DrawStamina(new Rect(20f, h - 52f, 240f, 16f));
                if (playerServe.MyTurn) DrawServe(w, h);
                else DrawInputState(w, h);
                if (Time.unscaledTime < qualityUntil) DrawShotQuality(w, h);
            }

            if (input.IsDragging)
                DrawLine(ToGui(input.DragStart), ToGui(input.DragCurrent), 4f, new Color(1f, 1f, 1f, 0.85f));

            if (showMinimap) DrawMinimap();

            GUI.Label(new Rect(w - 430f, 12f, 420f, 24f),
                (cameraRig.ThirdPerson ? "3인칭 (디버그)" : "1인칭") + $" · 시야각 {cameraRig.HorizontalFov:0}°" +
                $" · 공 템포 {config.ballTimeScale:0.00}" +
                (Time.timeScale > 0f && Time.timeScale < 1f ? " · 슬로모션" : ""),
                smallRight);

            if (showHelp)
            {
                float top = h - 120f - 24f * HelpLines.Length;
                Fill(new Rect(10f, top, 620f, 24f * HelpLines.Length + 12f), new Color(0f, 0f, 0f, 0.35f));
                for (int i = 0; i < HelpLines.Length; i++)
                    GUI.Label(new Rect(20f, top + 6f + 24f * i, 610f, 24f), HelpLines[i], small);
            }

            if (referee.Phase == MatchPhase.MatchOver) DrawMatchOver(w, h);
            else if (referee.Paused) DrawPause(w, h);
        }

        // ── 메뉴와 오버레이 ───────────────────────────────────

        // 왼쪽: 모드·경기 방식·코트·난이도, 오른쪽: 내 선수·상대 선수
        void DrawMenu(float w, float h)
        {
            Fill(new Rect(0f, 0f, w, h), new Color(0f, 0f, 0f, 0.55f));
            float pw = Mathf.Min(1000f, w - 40f), ph = 540f;
            var panel = new Rect((w - pw) / 2f, (h - ph) / 2f, pw, ph);
            Fill(panel, new Color(0.05f, 0.08f, 0.12f, 0.92f));
            GUI.Label(new Rect(panel.x, panel.y + 18f, pw, 44f), "1인칭 테니스", big);

            float cw = (pw - 90f) / 2f;
            float x = panel.x + 30f, y = panel.y + 80f;
            DrawCharacterPicker(new Rect(x + cw + 30f, y, cw, 300f));

            GUI.Label(new Rect(x, y, cw, 26f), "모드", small);
            menuMode = (GameMode)GUI.Toolbar(new Rect(x, y + 26f, cw, 36f), (int)menuMode, ModeNames, toolbar);
            y += 76f;

            if (menuMode != GameMode.Practice)
            {
                GUI.Label(new Rect(x, y, cw, 26f), "경기 방식", small);
                menuRules.format = (MatchFormat)GUI.Toolbar(new Rect(x, y + 26f, cw, 36f), (int)menuRules.format, FormatNames, toolbar);
                y += 76f;
                menuRules.noAd = GUI.Toggle(new Rect(x, y, cw, 30f), menuRules.noAd, "  노애드 (듀스에서 다음 1점으로 게임 결정)", button);
                y += 44f;
            }
            else
            {
                GUI.Label(new Rect(x, y, cw, 60f), "점수 없이 상대가 주는 공으로 랠리만 합니다.\n서브는 경기 모드에서 연습하세요.", small);
                y += 120f;
            }

            GUI.Label(new Rect(x, y, cw, 26f), "코트", small);
            var court = (CourtType)GUI.Toolbar(new Rect(x, y + 26f, cw, 36f), (int)Court.Type, CourtNames, toolbar);
            if (court != Court.Type) config.SelectCourt(court); // 고르는 즉시 뒤의 코트가 바뀐다
            y += 76f;

            GUI.Label(new Rect(x, y, cw, 26f), "AI 난이도", small);
            menuDifficulty = (AiDifficulty)GUI.Toolbar(new Rect(x, y + 26f, cw, 36f), (int)menuDifficulty, DifficultyNames, toolbar);

            if (GUI.Button(new Rect(panel.center.x - 120f, panel.yMax - 72f, 240f, 50f), "시작", button))
            {
                referee.SetCharacters(menuPlayerCharacter, menuAiCharacter);
                if (menuMode == GameMode.Match) referee.StartMatch(menuRules, menuDifficulty);
                else if (menuMode == GameMode.Watch) referee.StartWatch(menuRules, menuDifficulty);
                else referee.StartPractice(menuDifficulty);
            }
        }

        // 같은 선수를 양쪽이 동시에 골라도 된다
        void DrawCharacterPicker(Rect area)
        {
            CharacterPreset[] roster = config.characters;
            if (roster == null || roster.Length == 0) return;
            if (characterNames == null || characterNames.Length != roster.Length) characterNames = new string[roster.Length];
            for (int i = 0; i < roster.Length; i++) characterNames[i] = roster[i].name;

            float y = area.y;
            bool watch = menuMode == GameMode.Watch;
            int player = PickCharacter(new Rect(area.x, y, area.width, 130f), watch ? "AI 1 선수 (가까운 쪽)" : "내 선수", menuPlayerCharacter, roster);
            y += 140f;
            int ai = PickCharacter(new Rect(area.x, y, area.width, 130f), watch ? "AI 2 선수 (먼 쪽)" : "상대 선수 (AI)", menuAiCharacter, roster);
            if (player != menuPlayerCharacter || ai != menuAiCharacter)
            {
                menuPlayerCharacter = player;
                menuAiCharacter = ai;
                referee.SetCharacters(player, ai);
            }
        }

        int PickCharacter(Rect r, string label, int selected, CharacterPreset[] roster)
        {
            selected = Mathf.Clamp(selected, 0, roster.Length - 1);
            GUI.Label(new Rect(r.x, r.y, r.width, 26f), label, small);
            selected = GUI.Toolbar(new Rect(r.x, r.y + 26f, r.width, 36f), selected, characterNames, toolbar);
            CharacterPreset c = roster[selected];
            GUI.Label(new Rect(r.x, r.y + 68f, r.width, 24f), $"힘 {c.power} · 기술 {c.technique} · 속도 {c.speed}", text);
            GUI.Label(new Rect(r.x, r.y + 94f, r.width, 24f), c.description, small);
            return selected;
        }

        void DrawPause(float w, float h)
        {
            Fill(new Rect(0f, 0f, w, h), new Color(0f, 0f, 0f, 0.5f));
            GUI.Label(new Rect(0f, h * 0.35f, w, 50f), "일시정지", big);
            if (GUI.Button(new Rect(w / 2f - 130f, h * 0.35f + 70f, 260f, 46f), "계속 (Esc)", button)) referee.SetPaused(false);
            if (GUI.Button(new Rect(w / 2f - 130f, h * 0.35f + 126f, 260f, 46f), "메뉴로", button)) referee.ShowMenu();
        }

        void DrawMatchOver(float w, float h)
        {
            TennisScore s = referee.Score;
            int me = player.Side, them = -me;
            Fill(new Rect(0f, 0f, w, h), new Color(0f, 0f, 0f, 0.55f));
            float pw = 520f, ph = 380f;
            var panel = new Rect((w - pw) / 2f, (h - ph) / 2f, pw, ph);
            Fill(panel, new Color(0.05f, 0.08f, 0.12f, 0.92f));

            float y = panel.y + 22f;
            string result = referee.Watching ? $"{referee.NameOf(s.Winner)} ({referee.CharacterOf(s.Winner)?.name}) 승리" : s.Winner == me ? "승리!" : "패배";
            GUI.Label(new Rect(panel.x, y, pw, 44f), result, big);
            y += 56f;

            var sets = new System.Text.StringBuilder();
            foreach (Vector2Int set in s.CompletedSets)
            {
                if (sets.Length > 0) sets.Append("  ");
                sets.Append($"{SetGames(set, me)}-{SetGames(set, them)}");
            }
            GUI.Label(new Rect(panel.x, y, pw, 34f), sets.ToString(), title);
            y += 52f;

            string[] rows =
            {
                $"획득 포인트   {referee.PointsWon(me)}  :  {referee.PointsWon(them)}",
                $"에이스   {referee.Aces(me)}  :  {referee.Aces(them)}",
                $"더블 폴트   {referee.DoubleFaults(me)}  :  {referee.DoubleFaults(them)}",
            };
            GUI.Label(new Rect(panel.x, y, pw, 22f), $"{referee.NameOf(me)}  :  {referee.NameOf(them)}", smallCenter);
            y += 26f;
            foreach (string row in rows)
            {
                GUI.Label(new Rect(panel.x, y, pw, 28f), row, textCenter);
                y += 30f;
            }

            y = panel.yMax - 74f;
            if (GUI.Button(new Rect(panel.center.x - 230f, y, 220f, 48f), "다시 하기", button))
            {
                if (referee.Watching) referee.StartWatch(s.Rules, referee.Ai.Difficulty);
                else referee.StartMatch(s.Rules, referee.Ai.Difficulty);
            }
            if (GUI.Button(new Rect(panel.center.x + 10f, y, 220f, 48f), "메뉴로", button))
                referee.ShowMenu();
        }

        // ── 점수 ──────────────────────────────────────────────

        // 행마다: 서버 표시 · 이름 · 끝난 세트 게임 수 · 현재 게임 수 · 포인트
        void DrawScoreboard(float w)
        {
            TennisScore s = referee.Score;
            if (s == null) return;
            const float RowH = 30f, NameW = 150f, SetW = 34f, GameW = 40f, PointW = 62f;
            int setCount = s.CompletedSets.Count;
            float width = Mathf.Max(380f, 28f + NameW + SetW * setCount + GameW + PointW + 12f);
            float x0 = w / 2f - width / 2f, y0 = 10f;
            Fill(new Rect(x0, y0, width, RowH * 2f + 34f), new Color(0f, 0f, 0f, 0.5f));

            string format = Court.Surface.name + " · " + FormatNames[(int)s.Rules.format] + (s.Rules.noAd ? " · 노애드" : "") +
                            (s.InTiebreak ? " · 타이브레이크" : "") +
                            (referee.ServeNumber == 2 ? " · 두 번째 서브" : "");
            GUI.Label(new Rect(x0, y0 + 2f, width, 22f), format, smallCenter);

            int[] sides = { player.Side, -player.Side };
            for (int r = 0; r < 2; r++)
            {
                int side = sides[r];
                float y = y0 + 28f + r * RowH;
                float x = x0 + 10f;
                if (s.Server == side && !s.MatchOver) Dot(new Vector2(x + 6f, y + RowH / 2f), 10f, new Color(1f, 0.85f, 0.1f));
                x += 18f;
                GUI.Label(new Rect(x, y, NameW, RowH), $"{referee.NameOf(side)} · {referee.CharacterOf(side)?.name}", text);
                x += NameW;
                foreach (Vector2Int set in s.CompletedSets)
                {
                    GUI.Label(new Rect(x, y, SetW, RowH), SetGames(set, side).ToString(), textCenter);
                    x += SetW;
                }
                Fill(new Rect(x + 2f, y + 3f, GameW - 4f, RowH - 6f), new Color(1f, 1f, 1f, 0.12f));
                GUI.Label(new Rect(x, y, GameW, RowH), s.Games(side).ToString(), textCenter);
                x += GameW;
                GUI.Label(new Rect(x, y, PointW, RowH), s.MatchOver ? "" : s.PointLabel(side), title);
            }
        }

        void DrawPracticeScore(float w)
        {
            Fill(new Rect(w / 2f - 200f, 10f, 400f, 66f), new Color(0f, 0f, 0f, 0.45f));
            GUI.Label(new Rect(w / 2f - 200f, 12f, 400f, 34f),
                $"나 {referee.PracticePlayerPoints}  :  {referee.PracticeAiPoints} 상대", title);
            GUI.Label(new Rect(w / 2f - 200f, 46f, 400f, 26f),
                $"랠리 연습 · {Court.Surface.name} · 이번 랠리 {referee.RallyReturns}회 · 최고 {referee.BestReturns}회", smallCenter);
        }

        static int SetGames(Vector2Int set, int side) => side < 0 ? set.x : set.y;

        // ── 서브 ──────────────────────────────────────────────

        // 토스 미터: 공 높이와 타이밍 구간. 초록 = 좋음, 노랑 = 칠 수 있음, 빨강 = 헛스윙
        void DrawServe(float w, float h)
        {
            Fill(new Rect(w / 2f - 330f, h - 92f, 660f, 74f), new Color(0f, 0f, 0f, 0.45f));
            string head = playerServe.SecondServe ? "두 번째 서브" : "첫 서브";
            string state = !playerServe.Tossing ? "A/D로 위치 · 좌클릭을 누르면 토스" :
                input.IsDragging ? $"{ServeName(PlayerServe.TypeFor(input.DragPreview))} {AimName(input.DragPreview.Aim)} — 초록 칸에서 놓기" :
                "초록 칸에서 놓기";
            GUI.Label(new Rect(w / 2f - 330f, h - 88f, 660f, 32f), $"{head} · {state}", textCenter);
            GUI.Label(new Rect(w / 2f - 330f, h - 56f, 660f, 28f),
                "드래그 ←→ 코너 · 그대로 플랫 · ↓ 슬라이스 · ↑ 킥 (두 번째 서브는 느리지만 안전)", smallCenter);

            float top = config.serveTossApex + 0.3f;
            var bar = new Rect(w / 2f + 180f, h * 0.28f, 22f, 240f);
            float Y(float height) => bar.yMax - Mathf.Clamp01(height / top) * bar.height;
            Fill(bar, new Color(0f, 0f, 0f, 0.5f));
            Band(bar, Y(0f), Y(config.serveHitMinHeight), new Color(0.85f, 0.2f, 0.2f, 0.6f));
            float lo = Mathf.Max(config.serveHitMinHeight, config.serveIdealHeight - config.serveTimingWindow);
            Band(bar, Y(lo), Y(config.serveIdealHeight + config.serveTimingWindow), new Color(0.95f, 0.8f, 0.2f, 0.6f));
            float good = config.serveTimingWindow * 0.35f;
            Band(bar, Y(config.serveIdealHeight - good), Y(config.serveIdealHeight + good), new Color(0.3f, 0.9f, 0.35f, 0.9f));

            float ballY = Y(referee.Ball.State.Position.y);
            Fill(new Rect(bar.x - 8f, ballY - 2f, bar.width + 16f, 4f), Color.white);
            GUI.Label(new Rect(bar.xMax + 8f, bar.y - 4f, 120f, 22f), "토스", small);
        }

        static void Band(Rect bar, float yBottom, float yTop, Color color) =>
            Fill(new Rect(bar.x, yTop, bar.width, Mathf.Max(1f, yBottom - yTop)), color);

        static string ServeName(ServeType type) => type switch
        {
            ServeType.Slice => "슬라이스 서브",
            ServeType.Kick => "킥 서브",
            _ => "플랫 서브",
        };

        static string AimName(float aim) => aim < -0.1f ? $"← {Mathf.Abs(aim) * 100f:0}%" :
            aim > 0.1f ? $"→ {aim * 100f:0}%" : "가운데";

        // ── 미니맵 · 상태 ─────────────────────────────────────

        // 위에서 본 코트. 내 코트가 아래쪽, 내 오른쪽이 오른쪽.
        void DrawMinimap()
        {
            float halfW = Court.PlayableHalfWidth, halfD = Court.PlayableDepth;
            float s = Mathf.Min(170f / (2f * halfW), 260f / (2f * halfD));
            var area = new Rect(Screen.width - 2f * halfW * s - 14f, 42f, 2f * halfW * s, 2f * halfD * s);
            Vector2 c = area.center;
            float f = Court.Facing(player.Side);
            Vector2 Map(Vector3 p) => new Vector2(c.x + p.x * s * f, c.y - p.z * s * f);

            void WorldRect(float x0, float z0, float x1, float z1, Color color)
            {
                Vector2 a = Map(new Vector3(x0, 0f, z0)), b = Map(new Vector3(x1, 0f, z1));
                Fill(new Rect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y),
                    Mathf.Max(1f, Mathf.Abs(b.x - a.x)), Mathf.Max(1f, Mathf.Abs(b.y - a.y))), color);
            }

            float hl = Court.HalfLength, sw = Court.SinglesHalfWidth, dw = Court.DoublesHalfWidth, sl = Court.ServiceLine;
            var line = new Color(1f, 1f, 1f, 0.85f);
            Fill(area, new Color(0.08f, 0.3f, 0.2f, 0.75f));
            WorldRect(-dw, -hl, dw, hl, new Color(0.15f, 0.35f, 0.65f, 0.9f));
            foreach (float z in new[] { -hl, hl }) WorldRect(-dw, z, dw, z, line);
            foreach (float x in new[] { -dw, -sw, sw, dw }) WorldRect(x, -hl, x, hl, line);
            foreach (float z in new[] { -sl, sl }) WorldRect(-sw, z, sw, z, line);
            WorldRect(0f, -sl, 0f, sl, line);
            WorldRect(-Court.NetPostX, -0.15f, Court.NetPostX, 0.15f, new Color(0.85f, 0.85f, 0.85f));

            if (playerSwing.HasLanding && Court.SideOf(playerSwing.LandingPoint.z) == player.Side)
                Dot(Map(playerSwing.LandingPoint), 10f, new Color(1f, 0.85f, 0.1f));
            Dot(Map(referee.Ai.transform.position), 9f, new Color(0.95f, 0.3f, 0.25f));
            Transform near = referee.Watching && referee.NearAi != null ? referee.NearAi.transform : player.transform;
            Dot(Map(near.position), 9f, new Color(0.35f, 0.6f, 1f));
            Dot(Map(referee.Ball.transform.position), 6f, Color.white);

            GUI.Label(new Rect(area.x - 60f, area.yMax + 2f, area.width + 60f, 20f), "파랑 나 · 빨강 상대 · 노랑 낙하", smallRight);
        }

        void Dot(Vector2 center, float size, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(center.x - size / 2f, center.y - size / 2f, size, size), dot);
            GUI.color = previous;
        }

        static Texture2D CreateDotTexture()
        {
            const int N = 32;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave };
            var pixels = new Color32[N * N];
            float r = N / 2f - 1f;
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float d = new Vector2(x - (N - 1) / 2f, y - (N - 1) / 2f).magnitude;
                pixels[y * N + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(r - d) * 255f));
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }

        // 방금 친 공의 타구 품질: 좋음(초록) · 보통(노랑) · 나쁨(빨강)과 큰 원인 두 개
        void DrawShotQuality(float w, float h)
        {
            ShotQuality q = lastQuality;
            string rating = q.IsGood ? "좋음" : q.IsFair ? "보통" : "나쁨";
            Color color = q.IsGood ? new Color(0.45f, 0.9f, 0.5f) :
                q.IsFair ? new Color(1f, 0.8f, 0.3f) : new Color(1f, 0.45f, 0.4f);
            bool hasReasons = !string.IsNullOrEmpty(qualityReasons);
            var r = new Rect(w / 2f - 220f, h * 0.64f, 440f, hasReasons ? 60f : 36f);
            Fill(r, new Color(0f, 0f, 0f, 0.4f));

            Color previous = GUI.contentColor;
            GUI.contentColor = color;
            GUI.Label(new Rect(r.x, r.y + 3f, r.width, 30f), (q.IsServe ? "서브 " : "타구 ") + rating, title);
            GUI.contentColor = previous;
            if (hasReasons) GUI.Label(new Rect(r.x, r.y + 32f, r.width, 24f), qualityReasons, smallCenter);
        }

        // 관전: 두 AI의 스테미너와 네트 대시 여부
        void DrawWatchInfo(float w, float h)
        {
            var box = new Rect(w / 2f - 300f, h - 92f, 600f, 74f);
            Fill(box, new Color(0f, 0f, 0f, 0.45f));
            GUI.Label(new Rect(box.x, box.y + 4f, box.width, 26f), "AI 대결 관전 · Esc 일시정지 · V 시점 전환", smallCenter);
            DrawAiStamina(new Rect(box.x + 20f, box.y + 48f, 250f, 14f), referee.PlayerSide, referee.NearAi);
            DrawAiStamina(new Rect(box.xMax - 270f, box.y + 48f, 250f, 14f), referee.AiSide, referee.Ai);
        }

        void DrawAiStamina(Rect r, int side, AiOpponent ai)
        {
            if (ai == null) return;
            string state = ai.Exhausted ? " · 지침" : ai.AtNet ? " · 네트" : "";
            GUI.Label(new Rect(r.x, r.y - 22f, r.width, 20f), $"{referee.NameOf(side)} {ai.Character?.name}{state}", small);
            Fill(r, new Color(0f, 0f, 0f, 0.5f));
            Color color = ai.Exhausted ? new Color(0.9f, 0.25f, 0.2f) :
                ai.IsSprinting ? new Color(1f, 0.7f, 0.2f) : new Color(0.35f, 0.85f, 0.4f);
            Fill(new Rect(r.x + 2f, r.y + 2f, (r.width - 4f) * ai.Stamina01, r.height - 4f), color);
        }

        void DrawStamina(Rect r)
        {
            GUI.Label(new Rect(r.x, r.y - 24f, r.width, 22f), "스테미너", small);
            Fill(r, new Color(0f, 0f, 0f, 0.5f));
            Color color = player.Exhausted ? new Color(0.9f, 0.25f, 0.2f) :
                player.IsSprinting ? new Color(1f, 0.7f, 0.2f) : new Color(0.35f, 0.85f, 0.4f);
            Fill(new Rect(r.x + 2f, r.y + 2f, (r.width - 4f) * player.Stamina01, r.height - 4f), color);
        }

        void DrawInputState(float w, float h)
        {
            string state;
            if (referee.Phase == MatchPhase.Serve && referee.Ai.Serving) state = "상대 서브 — 바운드 뒤에 받기";
            else if (input.IsDragging) state = $"드래그 중: {Describe(input.DragPreview, playerSwing.InVolleyZone)}";
            else if (playerSwing.IsSwinging) state = "스윙!";
            else if (input.HasIntent) state = $"선입력: {Describe(input.Intent, playerSwing.InVolleyZone)}";
            else if (input.SkipRequested) state = "이번 공은 보냄";
            else if (input.WindowOpen)
                state = config.autoStrokeWhenNoInput ? "입력 가능 · 입력이 없으면 자동 스트로크" : "입력 가능 — 좌클릭 드래그";
            else state = "대기";

            Fill(new Rect(w / 2f - 260f, h - 92f, 520f, 74f), new Color(0f, 0f, 0f, 0.45f));
            GUI.Label(new Rect(w / 2f - 260f, h - 88f, 520f, 32f), state, textCenter);

            if (playerSwing.HasContact)
            {
                string zone = playerSwing.ContactInHitZone ? "범위 안" :
                    playerSwing.ContactInSwingZone ? "안 닿음 (헛스윙 위치)" : "범위 밖";
                string stroke = playerSwing.ContactIsVolley ? "발리" :
                    playerSwing.PredictedStroke == StrokeSide.Forehand ? "포핸드" : "백핸드";
                GUI.Label(new Rect(w / 2f - 260f, h - 56f, 520f, 28f),
                    $"{stroke} · 타점까지 {playerSwing.TimeToContact:0.00}초 · {zone}", smallCenter);
            }
        }

        // 네트 앞에서는 같은 드래그가 발리로 나간다 (↓ 드롭 발리, ↑ 깊은 발리)
        static string Describe(ShotIntent i, bool volleyZone)
        {
            string type = i.Type switch
            {
                ShotType.Slice => volleyZone ? "슬라이스 / 드롭 발리" : "슬라이스",
                ShotType.Lob => volleyZone ? "로브 / 깊은 발리" : "로브",
                ShotType.Smash => "스매시",
                _ => volleyZone ? "탑스핀 / 발리" : "탑스핀",
            };
            string depth = i.Type == ShotType.Slice || i.Type == ShotType.Lob ? $" · 길이 {i.Depth * 100f:0}%" : "";
            return $"{type} {AimName(i.Aim)}{depth}";
        }

        void EnsureStyles()
        {
            if (text != null) return;
            Font font = Font.CreateDynamicFontFromOSFont(
                new[] { "Malgun Gothic", "맑은 고딕", "Apple SD Gothic Neo", "Noto Sans CJK KR", "Arial" }, 18);
            text = new GUIStyle(GUI.skin.label) { font = font, fontSize = 20 };
            text.normal.textColor = Color.white;
            small = new GUIStyle(text) { fontSize = 15 };
            title = new GUIStyle(text) { fontSize = 24, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            banner = new GUIStyle(title) { fontSize = 36 };
            subBanner = new GUIStyle(title) { fontSize = 26 };
            subBanner.normal.textColor = new Color(1f, 0.92f, 0.6f);
            big = new GUIStyle(title) { fontSize = 34 };
            textCenter = new GUIStyle(text) { alignment = TextAnchor.MiddleCenter };
            smallCenter = new GUIStyle(small) { alignment = TextAnchor.MiddleCenter };
            smallRight = new GUIStyle(small) { alignment = TextAnchor.MiddleRight };
            button = new GUIStyle(GUI.skin.button) { font = font, fontSize = 18 };
            toolbar = new GUIStyle(GUI.skin.button) { font = font, fontSize = 15 };
        }

        static Vector2 ToGui(Vector2 screen) => new Vector2(screen.x, Screen.height - screen.y);

        static void Fill(Rect r, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        static void DrawLine(Vector2 a, Vector2 b, float width, Color color)
        {
            Matrix4x4 matrix = GUI.matrix;
            Vector2 d = b - a;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, a);
            Fill(new Rect(a.x, a.y - width / 2f, d.magnitude, width), color);
            GUI.matrix = matrix;
        }
    }
}
