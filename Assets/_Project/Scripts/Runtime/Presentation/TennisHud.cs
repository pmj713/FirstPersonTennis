using UnityEngine;
using UnityEngine.InputSystem;

namespace FirstPersonTennis
{
    // 1단계 프로토타입용 IMGUI HUD. 정식 UI는 이후 단계에서 교체.
    public class TennisHud : MonoBehaviour
    {
        [SerializeField] TennisConfig config;
        [SerializeField] RallyReferee referee;
        [SerializeField] PlayerMovement player;
        [SerializeField] ShotGestureInput input;
        [SerializeField] AutoSwing playerSwing;
        [SerializeField] FirstPersonBallCamera cameraRig;

        static readonly string[] HelpLines =
        {
            "WASD  이동 (W = 네트 쪽)",
            "Shift  달리기",
            "좌클릭 드래그  ←→ 방향 · ↓ 슬라이스 · ↑ 로브 · ↑↓ 스매시",
            "클릭만  탑스핀 가운데",
            "우클릭  이번 공 보내기",
            "V 시점 전환 · R 범위 링 · T 슬로모션 · H 도움말",
        };

        GUIStyle text, small, title, banner, textCenter, smallCenter, smallRight;
        string message;
        float messageUntil;
        bool showHelp = true;

        void OnEnable()
        {
            referee.PointEnded += OnPointEnded;
            playerSwing.SwingResolved += OnSwingResolved;
            input.IntentRejected += OnIntentRejected;
        }

        void OnDisable()
        {
            referee.PointEnded -= OnPointEnded;
            playerSwing.SwingResolved -= OnSwingResolved;
            input.IntentRejected -= OnIntentRejected;
        }

        void OnIntentRejected() => Show(playerSwing.IsSwinging ? "이미 스윙 중" : "상대가 친 뒤에 입력하세요");

        void Update()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null) return;
            if (kb.hKey.wasPressedThisFrame) showHelp = !showHelp;
            if (kb.tKey.wasPressedThisFrame) Time.timeScale = Time.timeScale < 1f ? 1f : 0.5f;
        }

        void OnPointEnded(int winner, PointReason reason)
        {
            bool won = winner == player.Side;
            string what = reason switch
            {
                PointReason.Out => won ? "상대 샷 아웃" : "아웃",
                PointReason.Net => won ? "상대 샷 네트" : "네트",
                _ => won ? "위너" : "놓침",
            };
            Show(won ? $"{what} — 득점" : $"{what} — 실점");
        }

        void OnSwingResolved(bool hit)
        {
            if (!hit) Show("헛스윙");
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

            // 점수
            Fill(new Rect(w / 2f - 200f, 10f, 400f, 66f), new Color(0f, 0f, 0f, 0.45f));
            GUI.Label(new Rect(w / 2f - 200f, 12f, 400f, 34f), $"나 {referee.PlayerPoints}  :  {referee.DummyPoints} 허수아비", title);
            GUI.Label(new Rect(w / 2f - 200f, 46f, 400f, 26f), $"이번 랠리 {referee.PlayerReturns}회 · 최고 {referee.BestReturns}회", smallCenter);

            if (Time.unscaledTime < messageUntil)
                GUI.Label(new Rect(0f, h * 0.22f, w, 52f), message, banner);

            DrawStamina(new Rect(20f, h - 52f, 240f, 16f));
            DrawInputState(w, h);

            if (input.IsDragging)
                DrawLine(ToGui(input.DragStart), ToGui(input.DragCurrent), 4f, new Color(1f, 1f, 1f, 0.85f));

            GUI.Label(new Rect(w - 330f, 12f, 320f, 24f),
                (cameraRig.ThirdPerson ? "3인칭 (디버그)" : "1인칭") + (Time.timeScale < 1f ? " · 슬로모션" : ""),
                smallRight);

            if (showHelp)
            {
                Fill(new Rect(10f, 10f, 470f, 24f * HelpLines.Length + 12f), new Color(0f, 0f, 0f, 0.35f));
                for (int i = 0; i < HelpLines.Length; i++)
                    GUI.Label(new Rect(20f, 16f + 24f * i, 460f, 24f), HelpLines[i], small);
            }
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
            if (input.IsDragging) state = $"드래그 중: {Describe(input.DragPreview)}";
            else if (playerSwing.IsSwinging) state = "스윙!";
            else if (input.HasIntent) state = $"선입력: {Describe(input.Intent)}";
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
                string stroke = playerSwing.PredictedStroke == StrokeSide.Forehand ? "포핸드" : "백핸드";
                GUI.Label(new Rect(w / 2f - 260f, h - 56f, 520f, 28f),
                    $"{stroke} · 타점까지 {playerSwing.TimeToContact:0.00}초 · {zone}", smallCenter);
            }
        }

        static string Describe(ShotIntent i)
        {
            string type = i.Type switch
            {
                ShotType.Slice => "슬라이스",
                ShotType.Lob => "로브",
                ShotType.Smash => "스매시",
                _ => "탑스핀",
            };
            string dir = i.Aim < -0.1f ? $"← {Mathf.Abs(i.Aim) * 100f:0}%" :
                i.Aim > 0.1f ? $"→ {i.Aim * 100f:0}%" : "가운데";
            string depth = i.Type == ShotType.Slice || i.Type == ShotType.Lob ? $" · 길이 {i.Depth * 100f:0}%" : "";
            return $"{type} {dir}{depth}";
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
            textCenter = new GUIStyle(text) { alignment = TextAnchor.MiddleCenter };
            smallCenter = new GUIStyle(small) { alignment = TextAnchor.MiddleCenter };
            smallRight = new GUIStyle(small) { alignment = TextAnchor.MiddleRight };
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
