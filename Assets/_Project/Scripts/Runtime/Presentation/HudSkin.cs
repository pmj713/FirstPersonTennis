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
            GUIUtility.RotateAroundPivot(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, a);
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
