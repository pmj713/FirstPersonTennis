using UnityEngine;

namespace FirstPersonTennis
{
    // 설정 화면: 드래그 감도·데드존·카메라 회전 속도·시야각·배경음악과 경기장 소리(관중·심판) 크기. 값은 이 PC에 저장된다 (UserSettings).
    // 아래 연습 칸에서 드래그해 보면 지금 설정으로 어떤 구질이 나오는지 보인다. 경기 HUD와 메인 화면이 같이 쓴다
    public sealed class SettingsPanel
    {
        public bool IsOpen { get; private set; }

        bool padDragging;
        Vector2 padStart, padCurrent;
        float padMaxRise;
        string padResult = "";

        public void Open()
        {
            IsOpen = true;
            padDragging = false;
            padResult = "";
        }

        public void Close()
        {
            IsOpen = false;
            UserSettings.Save();
        }

        // defaultFov: 설정 파일의 기본 시야각 (사용자가 바꾸지 않았으면 이 값)
        public void Draw(float w, float h, HudSkin skin, float defaultFov)
        {
            HudSkin.Fill(new Rect(0f, 0f, w, h), new Color(0f, 0f, 0f, 0.6f));
            float pw = Mathf.Min(640f, w - 40f), ph = Mathf.Min(700f, h - 20f); // 슬라이더 다섯 줄(소리 크기 둘은 한 줄) + 연습 칸
            var panel = new Rect((w - pw) / 2f, (h - ph) / 2f, pw, ph);
            HudSkin.Fill(panel, new Color(0.05f, 0.08f, 0.12f, 0.95f));
            GUI.Label(new Rect(panel.x, panel.y + 14f, pw, 40f), "설정", skin.Big);

            // 바뀐 값만 저장한다 (안 건드린 값은 기본값을 계속 따른다)
            float x = panel.x + 30f, cw = pw - 60f, y = panel.y + 66f;
            float sensitivity = UserSettings.DragSensitivity;
            float v = Slider(skin, ref y, x, cw, "드래그 감도", $"{sensitivity:0.00}배",
                "클수록 짧게 끌어도 같은 구질·방향이 나와요", sensitivity,
                UserSettings.MinSensitivity, UserSettings.MaxSensitivity, 0.05f);
            if (!Mathf.Approximately(v, sensitivity)) UserSettings.DragSensitivity = v;

            float deadZone = UserSettings.DragDeadZone;
            v = Slider(skin, ref y, x, cw, "드래그 데드존", $"화면 높이의 {deadZone * 100f:0.0}%",
                "좌우로 이보다 짧게 끌면 방향 없이 가운데로 쳐요", deadZone,
                UserSettings.MinDeadZone, UserSettings.MaxDeadZone, 0.005f);
            if (!Mathf.Approximately(v, deadZone)) UserSettings.DragDeadZone = v;

            float turn = UserSettings.CameraTurnSpeed;
            v = Slider(skin, ref y, x, cw, "카메라 회전 속도", $"{turn:0.0}배",
                "공을 따라 고개를 돌리는 빠르기 (느릴수록 멀미가 덜해요)", turn,
                UserSettings.MinTurnSpeed, UserSettings.MaxTurnSpeed, 0.1f);
            if (!Mathf.Approximately(v, turn)) UserSettings.CameraTurnSpeed = v;

            float fov = UserSettings.HorizontalFov(defaultFov);
            v = Slider(skin, ref y, x, cw, "시야각", $"{fov:0}°",
                "넓을수록 주변이 보이지만 코트가 멀어 보여요 (플레이 중 [ ] 키)", fov, UserSettings.MinFov, UserSettings.MaxFov, 1f);
            if (!Mathf.Approximately(v, fov)) UserSettings.SetHorizontalFov(v);

            // 소리 크기 둘은 한 줄에 나란히
            float half = (cw - 30f) / 2f, rowY = y;
            float music = UserSettings.MusicVolume;
            v = Slider(skin, ref rowY, x, half, "배경음악 크기", Percent(music),
                "메인 화면 음악", music, 0f, 1f, 0.05f);
            if (!Mathf.Approximately(v, music)) UserSettings.MusicVolume = v;
            rowY = y;
            float crowd = UserSettings.CrowdVolume;
            v = Slider(skin, ref rowY, x + half + 30f, half, "경기장 소리 크기", Percent(crowd),
                "관중·심판 콜·선수 기합", crowd, 0f, 1f, 0.05f);
            if (!Mathf.Approximately(v, crowd)) UserSettings.CrowdVolume = v;
            y = rowY;

            DrawDragPad(skin, new Rect(x, y + 4f, cw, Mathf.Max(80f, panel.yMax - 76f - (y + 4f))));

            float by = panel.yMax - 62f;
            if (GUI.Button(new Rect(x, by, 160f, 44f), "기본값으로", skin.Button))
            {
                UserSettings.ResetToDefaults();
                padResult = "";
            }
            if (GUI.Button(new Rect(panel.xMax - 190f, by, 160f, 44f), "닫기 (Esc)", skin.Button)) Close();
        }

        static string Percent(float volume) => volume > 0f ? $"{volume * 100f:0}%" : "끔";

        // 이름·값 한 줄, 설명 한 줄, 슬라이더. step 단위로 맞춘다
        static float Slider(HudSkin skin, ref float y, float x, float width, string label, string value, string hint,
            float current, float min, float max, float step)
        {
            GUI.Label(new Rect(x, y - 2f, width, 30f), label, skin.Text); // 빌드의 기본 스킨은 여백이 커서 26 높이면 글자 아래가 잘린다
            GUI.Label(new Rect(x, y, width, 26f), value, skin.SmallRight);
            GUI.Label(new Rect(x, y + 24f, width, 22f), hint, skin.Small);
            float v = GUI.HorizontalSlider(new Rect(x, y + 50f, width, 20f), current, min, max);
            y += 78f;
            return Mathf.Clamp(Mathf.Round(v / step) * step, min, max);
        }

        // 연습 칸: 경기와 같은 계산(감도 곱하기, 데드존)으로 드래그를 구질로 바꿔 보여 준다. 화면 위쪽이 + 방향
        void DrawDragPad(HudSkin skin, Rect pad)
        {
            HudSkin.Fill(pad, new Color(1f, 1f, 1f, 0.06f));
            Event e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && pad.Contains(e.mousePosition))
            {
                padDragging = true;
                padStart = padCurrent = e.mousePosition;
                padMaxRise = 0f;
                e.Use();
            }
            else if (padDragging && e.type == EventType.MouseDrag)
            {
                padCurrent = e.mousePosition;
                e.Use();
            }

            ShotIntent intent = default;
            if (padDragging)
            {
                Vector2 d = padCurrent - padStart;
                Vector2 delta = new Vector2(d.x, -d.y) / Mathf.Max(1, Screen.height) * UserSettings.DragSensitivity;
                padMaxRise = Mathf.Max(padMaxRise, delta.y);
                intent = ShotGestureInput.Classify(delta, padMaxRise, UserSettings.DragDeadZone);
                HudSkin.DrawLine(padStart, padCurrent, 4f, new Color(1f, 1f, 1f, 0.85f));
            }
            if (padDragging && e.type == EventType.MouseUp)
            {
                padDragging = false;
                padResult = "결과: " + HudSkin.Describe(intent, false);
                e.Use();
            }

            string line = padDragging ? "드래그 중: " + HudSkin.Describe(intent, false) :
                padResult.Length > 0 ? padResult : "여기서 좌클릭 드래그해 보기 — 지금 설정으로 나오는 구질과 방향";
            GUI.Label(new Rect(pad.x + 10f, pad.y + pad.height / 2f - 30f, pad.width - 20f, 60f), line, skin.TextCenter); // 두 줄까지
        }
    }
}
