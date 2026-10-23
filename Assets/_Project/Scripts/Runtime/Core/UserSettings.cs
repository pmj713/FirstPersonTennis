using UnityEngine;

namespace FirstPersonTennis
{
    // 플레이어가 설정 화면에서 고르는 값. 이 PC에 저장되어(PlayerPrefs) 다음 실행에도 유지된다.
    // 게임 설계 값(속도·구질·AI 등)은 TennisConfig에 있다.
    public static class UserSettings
    {
        public const float MinSensitivity = 0.5f, MaxSensitivity = 2f;
        public const float DefaultDeadZone = 0.03f, MinDeadZone = 0.01f, MaxDeadZone = 0.05f; // 화면 높이 대비
        public const float MinTurnSpeed = 0.5f, MaxTurnSpeed = 2f;
        public const float MinFov = 50f, MaxFov = 110f;

        static readonly Setting sensitivity = new Setting("DragSensitivity");
        static readonly Setting deadZone = new Setting("DragDeadZone");
        static readonly Setting turnSpeed = new Setting("CameraTurnSpeed");
        static readonly Setting fov = new Setting("HorizontalFov");
        static readonly Setting music = new Setting("MusicVolume");
        static readonly Setting crowd = new Setting("CrowdVolume");

        // 드래그 감도: 드래그 길이에 곱한다. 클수록 짧게 끌어도 같은 구질·방향이 나온다
        public static float DragSensitivity
        {
            get => sensitivity.Get(1f);
            set => sensitivity.Set(Mathf.Clamp(value, MinSensitivity, MaxSensitivity));
        }

        // 드래그 데드존: 좌우로 이보다 짧게 끌면 방향 없이 가운데로 친다
        public static float DragDeadZone
        {
            get => deadZone.Get(DefaultDeadZone);
            set => deadZone.Set(Mathf.Clamp(value, MinDeadZone, MaxDeadZone));
        }

        // 카메라가 공을 따라 고개를 돌리는 빠르기 배율
        public static float CameraTurnSpeed
        {
            get => turnSpeed.Get(1f);
            set => turnSpeed.Set(Mathf.Clamp(value, MinTurnSpeed, MaxTurnSpeed));
        }

        // 16:9 기준 수평 시야각. 고른 적이 없으면 설계 기본값(TennisConfig.horizontalFov)
        public static float HorizontalFov(float defaultFov) => fov.Get(defaultFov);

        public static void SetHorizontalFov(float value) => fov.Set(Mathf.Clamp(value, MinFov, MaxFov));

        // 배경음악 크기 배율 (0 = 끔, 1 = 곡을 만든 크기)
        public static float MusicVolume
        {
            get => music.Get(1f);
            set => music.Set(Mathf.Clamp01(value));
        }

        // 경기장 소리(관중·심판 콜·선수 기합) 크기 배율 (0 = 끔, 1 = 기본)
        public static float CrowdVolume
        {
            get => crowd.Get(1f);
            set => crowd.Set(Mathf.Clamp01(value));
        }

        public static void ResetToDefaults()
        {
            foreach (Setting s in new[] { sensitivity, deadZone, turnSpeed, fov, music, crowd }) s.Reset();
            PlayerPrefs.Save();
        }

        // 설정 화면을 닫을 때 디스크에 쓴다
        public static void Save() => PlayerPrefs.Save();

        // 값 하나: 처음 읽을 때 한 번만 PlayerPrefs를 본다
        sealed class Setting
        {
            readonly string key;
            bool loaded, has;
            float value;

            public Setting(string name) => key = "FirstPersonTennis." + name;

            public float Get(float fallback)
            {
                if (!loaded)
                {
                    loaded = true;
                    has = PlayerPrefs.HasKey(key);
                    if (has) value = PlayerPrefs.GetFloat(key);
                }
                return has ? value : fallback;
            }

            public void Set(float v)
            {
                loaded = has = true;
                value = v;
                PlayerPrefs.SetFloat(key, v);
            }

            public void Reset()
            {
                PlayerPrefs.DeleteKey(key);
                loaded = true;
                has = false;
            }
        }
    }
}
