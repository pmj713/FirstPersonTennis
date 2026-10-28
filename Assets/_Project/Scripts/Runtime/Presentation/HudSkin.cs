using UnityEngine;

namespace FirstPersonTennis
{
    // 경기 HUD와 메인 화면이 같이 쓰는 IMGUI 글꼴·스타일과 그리기 도구.
    // 스타일은 저장하지 않는다: 플레이 중 다시 컴파일되거나 씬을 다시 불러오면 글꼴이 내려가므로 Valid가 false면 다시 만든다
    public sealed class HudSkin
    {
        public Font Font;
        public GUIStyle Text, Small, Title, Banner, SubBanner, Big, TextCenter, SmallCenter, SmallRight, Button, Toolbar;

        public bool Valid => Text != null && Font != null;

        public static HudSkin Create()
        {
            var s = new HudSkin
            {
                Font = Font.CreateDynamicFontFromOSFont(
                    new[] { "Malgun Gothic", "맑은 고딕", "Apple SD Gothic Neo", "Noto Sans CJK KR", "Arial" }, 18),
            };
            s.Text = new GUIStyle(GUI.skin.label) { font = s.Font, fontSize = 20 };
            s.Text.normal.textColor = Color.white;
            s.Small = new GUIStyle(s.Text) { fontSize = 15 };
            s.Title = new GUIStyle(s.Text) { fontSize = 24, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            s.Banner = new GUIStyle(s.Title) { fontSize = 36 };
            s.SubBanner = new GUIStyle(s.Title) { fontSize = 26 };
            s.SubBanner.normal.textColor = new Color(1f, 0.92f, 0.6f);
            s.Big = new GUIStyle(s.Title) { fontSize = 34 };
            s.TextCenter = new GUIStyle(s.Text) { alignment = TextAnchor.MiddleCenter };
            s.SmallCenter = new GUIStyle(s.Small) { alignment = TextAnchor.MiddleCenter };
            s.SmallRight = new GUIStyle(s.Small) { alignment = TextAnchor.MiddleRight };
            s.Button = new GUIStyle(GUI.skin.button) { font = s.Font, fontSize = 18 };
            s.Toolbar = new GUIStyle(GUI.skin.button) { font = s.Font, fontSize = 15 };
            return s;
        }

        // ── 화면 크기·비율 ─────────────────────────────────────
        // UI는 픽셀 대신 UI 좌표로 그리고, GUI.matrix 배율로 화면에 맞춘다. 배율은 UI 좌표로 본 화면이 늘 1280×720 이상이
        // 되게 정한다: 그보다 작거나 비율이 다른 화면(4:3 등)은 전체를 줄이고, 1920×1080보다 큰 화면은 그만큼 키운다.
        // 그 사이(1280×720 ~ 1920×1080)는 1이라 글자가 선명하다. 배치는 1280×720 이상의 어떤 비율에서도 들어가게 짠다
        public const float MinWidth = 1280f, MinHeight = 720f, BaseWidth = 1920f, BaseHeight = 1080f;

        public static float ScaleFor(float screenWidth, float screenHeight)
        {
            float fit = Mathf.Min(screenWidth / MinWidth, screenHeight / MinHeight);
            if (fit < 1f) return Mathf.Max(0.1f, fit);
            return Mathf.Max(1f, Mathf.Min(screenWidth / BaseWidth, screenHeight / BaseHeight));
        }

        // OnGUI 처음에 부른다: 배율을 걸고 UI 좌표로 본 화면 크기를 돌려준다
        public static Vector2 BeginScreen()
        {
            float scale = ScaleFor(Screen.width, Screen.height);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            return new Vector2(Screen.width / scale, Screen.height / scale);
        }

        // 화면 픽셀 좌표(아래가 0, 입력 시스템) → UI 좌표
        public static Vector2 ScreenToGui(Vector2 screen) =>
            new Vector2(screen.x, Screen.height - screen.y) / ScaleFor(Screen.width, Screen.height);

        public static void Fill(Rect r, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        public static void DrawLine(Vector2 a, Vector2 b, float width, Color color)
        {
            Matrix4x4 matrix = GUI.matrix;
            Vector2 d = b - a;
            // a를 중심으로 돌린다. RotateAroundPivot은 배율이 걸려 있으면 중심이 어긋나서 직접 곱한다
            GUI.matrix = matrix * Matrix4x4.TRS(a, Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg), Vector3.one) *
                         Matrix4x4.Translate(-a);
            Fill(new Rect(a.x, a.y - width / 2f, d.magnitude, width), color);
            GUI.matrix = matrix;
        }

        // 구질 이름: 탑스핀 ← 30% · 슬라이스 길이 50% 처럼
        public static string Describe(ShotIntent i, bool volleyZone)
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

        public static string AimName(float aim) => aim < -0.1f ? $"← {Mathf.Abs(aim) * 100f:0}%" :
            aim > 0.1f ? $"→ {aim * 100f:0}%" : "가운데";
    }
}
