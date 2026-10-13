using UnityEngine;

namespace FirstPersonTennis
{
    // 기획서의 수치를 한곳에 모은 설정. 모두 플레이테스트로 조정할 초기값이다.
    [CreateAssetMenu(menuName = "FirstPersonTennis/Tennis Config", fileName = "TennisConfig")]
    public class TennisConfig : ScriptableObject
    {
        [Header("코트")]
        public SurfaceProfile surface = SurfaceProfile.Hard;

        [Header("이동")]
        public float walkSpeed = 4f;
        public float runSpeed = 7f;
        public float acceleration = 30f;
        public float eyeHeight = 1.7f;

        [Header("카메라")]
        [Tooltip("16:9 화면 기준 수평 시야각. 넓을수록 주변이 보이지만 코트가 길쭉하고 멀어 보인다.")]
        [Range(50f, 110f)] public float horizontalFov = 75f;

        [Header("스테미너")]
        [Tooltip("가득 찬 스테미너로 달릴 수 있는 거리 (m). 단식 중앙→사이드라인 4.1m + 여유")]
        public float sprintDistance = 5f;
        [Tooltip("걷거나 멈춰 있을 때 초당 회복 비율")]
        public float staminaRecoveryPerSecond = 0.3f;
        [Tooltip("바닥난 뒤 다시 달릴 수 있게 되는 비율")]
        public float staminaResumeThreshold = 0.5f;

        [Header("타격 범위")]
        [Tooltip("몸 중심에서 수평으로 닿는 거리 (m)")]
        public float hitReach = 1.5f;
        [Tooltip("타격 범위 바깥으로 이만큼까지는 헛스윙한다 (m)")]
        public float swingReachExtra = 0.5f;
        [Tooltip("이보다 낮은 공은 칠 수 없다 (m). 굴러가는 공만 빼고 다 칠 수 있게 낮게 둔다. 낮을수록 타구 오차가 커진다.")]
        public float minHitHeight = 0.06f;
        public float maxHitHeight = 2.5f;
        [Tooltip("몸 앞쪽 타점 평면까지 거리 (m)")]
        public float contactForward = 0.45f;
        [Tooltip("타점 평면 앞뒤로 허용하는 거리 (m)")]
        public float contactDepthTolerance = 0.6f;
        public float idealLateral = 0.8f;
        public float idealHeight = 1.0f;

        [Header("스매시")]
        [Tooltip("이 높이 이상에서 치면 스매시, 낮으면 강한 플랫 스트로크 (m)")]
        public float smashMinHeight = 2.0f;
        [Tooltip("스매시 입력일 때 닿는 최고 높이 (m). 키 1.8m + 팔과 라켓을 위로 뻗은 높이")]
        public float smashReachHeight = 3.2f;
        [Tooltip("스매시 오차 계산에 쓰는 이상적 타점 높이 (m)")]
        public float smashIdealHeight = 2.6f;

        [Header("스윙")]
        [Tooltip("스윙 시작부터 임팩트까지 (초)")]
        public float swingWindup = 0.25f;
        public float followThrough = 0.35f;
        [Tooltip("임팩트 몇 초 전부터 라켓을 뒤로 빼기 시작하는지")]
        public float takeBackLead = 0.9f;

        [Header("구질")]
        [Tooltip("모든 샷의 속도 배율. 1 = 실제 랠리 속도. 스핀은 배율의 제곱으로 줄여 궤적 모양을 유지한다. " +
                 "0.7 아래로 내리면 깊은 공을 보내려고 공이 높이 뜬다.")]
        [Range(0.5f, 1.2f)] public float shotSpeedScale = 0.75f;
        public ShotProfile topspin = new ShotProfile { speed = 27f, shortSpeed = 27f, spin = 230f, minDepth = 7f, maxDepth = 9.3f };
        public ShotProfile slice = new ShotProfile { speed = 21f, shortSpeed = 11f, spin = -150f, minDepth = 2.2f, maxDepth = 9.5f };
        public ShotProfile lob = new ShotProfile { spin = 120f, minDepth = 8.5f, maxDepth = 10.8f, launchAngle = 50f };
        public ShotProfile smash = new ShotProfile { speed = 34f, shortSpeed = 34f, spin = 40f, minDepth = 7f, maxDepth = 9f };
        [Tooltip("좌우로 최대한 밀었을 때 목표 x (m)")]
        public float maxAimX = 3.3f;

        [Header("타구 오차 (도)")]
        public float baseElevationError = 0.5f;
        public float elevationErrorPerBad = 2f;
        public float baseAzimuthError = 0.6f;
        public float azimuthErrorPerBad = 2.5f;

        [Header("보조")]
        [Tooltip("보조 많음: 선입력이 없으면 기본 스트로크를 자동으로 친다")]
        public bool autoStrokeWhenNoInput;
        public bool showLandingMarker = true;
        public bool showHitRangeRing = true;
        public bool hitZoneAlert = true;

        [Header("허수아비")]
        public float dummySpeed = 6.5f;
        public float dummyAcceleration = 25f;
        public float dummyReactionDelay = 0.2f;
        [Range(0f, 1f)] public float dummyErrorScale = 0.5f;
        [Range(0f, 1f)] public float dummySliceChance = 0.15f;
        [Range(0f, 1f)] public float dummyLobChance = 0.1f;
        [Tooltip("허수아비가 포인트를 시작할 때 보내는 공의 속도 배율")]
        public float feedSpeedScale = 0.75f;

        [Header("포인트")]
        public float pointResetDelay = 1.6f;
        public float feedDelay = 1f;
    }
}
