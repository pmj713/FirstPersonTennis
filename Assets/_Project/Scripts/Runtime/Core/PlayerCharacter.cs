using System;
using UnityEngine;

namespace FirstPersonTennis
{
    // 프리셋 선수. 스탯은 0~100(이동속도 효과 기준 0, 나머지 효과 기준 50)이고 고정이다(직접 배분하지 않음). 같은 선수를 여러 명이 동시에 고를 수 있다.
    [Serializable]
    public class CharacterPreset
    {
        public const int MaxStat = 100;
        public const int BaseStat = 50;

        public string name = "균형형";
        [Tooltip("메뉴에 보이는 한 줄 설명")]
        public string description = "입문용, 약점 없음";
        [Range(0, MaxStat)] public int power = BaseStat;
        [Range(0, MaxStat)] public int technique = BaseStat;
        [Range(0, MaxStat)] public int speed = BaseStat;

        [Header("AI가 이 선수를 쓸 때 성향 (배율, 1 = 난이도 기본값 그대로)")]
        public float sliceTendency = 1f;
        public float lobTendency = 1f;
        public float dropShotTendency = 1f;
        [Tooltip("첫 서브를 플랫으로 넣을 확률에 더한다")]
        [Range(-0.5f, 0.5f)] public float flatServeBias;
        [Tooltip("좌우로 노리는 폭 배율. 크면 코너를 과감하게, 작으면 안전하게")]
        [Range(0.5f, 1.3f)] public float aimBoldness = 1f;

        public static CharacterPreset[] DefaultRoster() => new[]
        {
            new CharacterPreset { name = "균형형", description = "입문용, 약점 없음" },
            new CharacterPreset
            {
                name = "공격형", description = "강한 서브·스트로크, 발이 느림, 잔디 유리", power = 80, technique = 40, speed = 30,
                sliceTendency = 0.8f, lobTendency = 0.8f, dropShotTendency = 0.8f, flatServeBias = 0.1f,
            },
            new CharacterPreset
            {
                name = "컨트롤형", description = "오차 작고 스핀 강함, 드롭샷·로브", power = 30, technique = 80, speed = 40,
                sliceTendency = 1.1f, lobTendency = 1f, dropShotTendency = 1.2f,
            },
            new CharacterPreset
            {
                name = "수비형", description = "넓은 수비, 클레이 유리", power = 30, technique = 40, speed = 80,
                sliceTendency = 1.1f, lobTendency = 1.1f, dropShotTendency = 0.8f, flatServeBias = -0.1f,
            },
        };
    }

    // 이동속도 효과는 0→100, 나머지 효과는 50→100에서 바뀌는 비율이다.
    [Serializable]
    public struct StatTuning
    {
        [Tooltip("힘 100일 때 스트로크·서브·스매시 속도 증가 (비율)")]
        public float powerSpeedAtMax;
        [Tooltip("기술 100일 때 타구 오차 감소 (비율)")]
        public float techniqueErrorAtMax;
        [Tooltip("기술 100일 때 사이드라인 여유(aimMargin)를 줄이는 비율. 높을수록 라인 가까이 노린다")]
        public float techniqueAimMarginAtMax;
        [Tooltip("기술 100일 때 스핀 증가 (비율)")]
        public float techniqueSpinAtMax;
        [Tooltip("속도 0이 기본 걷기·달리기 속도, 100일 때 속도 증가 (비율). 좌우로 흔드는 공을 따라가는 힘이 이 차이에서 나온다")]
        public float speedMoveAtMax;
        [Tooltip("속도 100일 때 스테미너 회복 증가 (비율)")]
        public float speedRecoveryAtMax;
        [Tooltip("속도 100일 때 미끄러운 바닥(클레이·잔디)에서 멈출 때 덜 미끄러지는 비율. 잘 움직이는 선수는 슬라이딩도 잘한다")]
        public float speedSlideAtMax;

        public static StatTuning Default => new StatTuning
        {
            powerSpeedAtMax = 0.1f, // 1포인트당 0.2%
            techniqueErrorAtMax = 0.4f, techniqueSpinAtMax = 0.25f, techniqueAimMarginAtMax = 0.8f,
            speedMoveAtMax = 0.15f, speedRecoveryAtMax = 0.25f, speedSlideAtMax = 0.5f, // 이동 1포인트당 0.15%
        };
    }

    // 스탯을 실제 배율로 바꾼 값. 타격(AutoSwing)과 이동(PlayerMovement, AiOpponent)이 쓴다.
    public struct PlayerStats
    {
        public float PowerScale;    // 샷 속도
        public float ErrorScale;    // 타구 오차
        public float SpinScale;     // 스핀
        public float AimMarginScale; // 사이드라인 여유 (작을수록 라인 가까이)
        public float MoveScale;     // 걷기·달리기 속도
        public float RecoveryScale; // 스테미너 회복
        public float SlideScale;    // 미끄러운 바닥에서 멈출 때 잃는 제동 (작을수록 덜 미끄러짐)

        public static PlayerStats Neutral => new PlayerStats
        {
            PowerScale = 1f, ErrorScale = 1f, SpinScale = 1f, AimMarginScale = 1f, MoveScale = 1f, RecoveryScale = 1f,
            SlideScale = 1f,
        };

        public static PlayerStats From(CharacterPreset c, StatTuning t)
        {
            if (c == null) return Neutral;
            // -1 (스탯 0) ~ 0 (기준 50) ~ +1 (스탯 100)
            float Level(int stat) => (Mathf.Clamp(stat, 0, CharacterPreset.MaxStat) - CharacterPreset.BaseStat) /
                                     (float)(CharacterPreset.MaxStat - CharacterPreset.BaseStat);
            return new PlayerStats
            {
                PowerScale = 1f + Level(c.power) * t.powerSpeedAtMax,
                ErrorScale = Mathf.Max(0.1f, 1f - Level(c.technique) * t.techniqueErrorAtMax),
                SpinScale = Mathf.Max(0.1f, 1f + Level(c.technique) * t.techniqueSpinAtMax),
                AimMarginScale = Mathf.Max(0.1f, 1f - Level(c.technique) * t.techniqueAimMarginAtMax),
                MoveScale = Mathf.Max(0.1f, 1f + Mathf.Clamp(c.speed, 0, CharacterPreset.MaxStat) / (float)CharacterPreset.MaxStat * t.speedMoveAtMax),
                RecoveryScale = Mathf.Max(0.1f, 1f + Level(c.speed) * t.speedRecoveryAtMax),
                SlideScale = Mathf.Max(0f, 1f - Level(c.speed) * t.speedSlideAtMax),
            };
        }
    }
}
