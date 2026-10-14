using UnityEngine;

namespace FirstPersonTennis
{
    // 기획서의 수치를 한곳에 모은 설정. 모두 플레이테스트로 조정할 초기값이다.
    [CreateAssetMenu(menuName = "FirstPersonTennis/Tennis Config", fileName = "TennisConfig")]
    public class TennisConfig : ScriptableObject
    {
        [Header("코트")]
        public SurfaceProfile surface = SurfaceProfile.Hard;
        [Tooltip("좌우 너비 배율. 1 = ITF 규격 (단식 8.23m, 복식 10.97m). Play를 누르면 반영된다.")]
        [Range(0.5f, 3f)] public float courtWidthScale = 1f;
        [Tooltip("앞뒤 길이 배율. 1 = ITF 규격 (23.77m). 샷 목표 깊이도 같이 늘어난다. Play를 누르면 반영된다.")]
        [Range(0.5f, 2f)] public float courtLengthScale = 1f;

        [Header("공")]
        [Tooltip("공 지름 (m). 실제 공은 0.067m. 보이는 크기와 바닥·네트 충돌 판정이 바뀌고, " +
                 "공기저항·스핀은 실제 공 그대로다. Play를 누르면 반영된다.")]
        [Range(0.03f, 0.3f)] public float ballDiameter = 0.067f;

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
        [Tooltip("스매시 입력일 때 닿는 최고 높이 (m). 키 1.8m + 팔과 라켓을 위로 뻗고 살짝 뛴 높이")]
        public float smashReachHeight = 3.5f;
        [Tooltip("스매시 입력일 때 좌우로 닿는 거리 (m). 일반 타격(hitReach)보다 넓다")]
        public float smashHitReach = 2.0f;
        [Tooltip("스매시 입력일 때 타점 앞뒤 허용 거리 (m). 일반 타격(contactDepthTolerance)보다 넓다")]
        public float smashDepthTolerance = 1.0f;
        [Tooltip("공중 스매시로 칠 수 있는 최저 높이 (m). smashMinHeight 아래에서 치면 강한 플랫으로 나간다")]
        public float smashAirMinHeight = 1.6f;
        [Tooltip("스매시 오차 계산에 쓰는 이상적 타점 높이 (m)")]
        public float smashIdealHeight = 2.6f;
        [Tooltip("스매시의 이상적 좌우 거리 (m). 오른쪽 어깨 위")]
        public float smashIdealLateral = 0.4f;
        [Tooltip("스매시로 친 공의 템포 배율. 공 템포에 곱한다 (0.7 × 1.5 = 1.05). 1이면 다른 샷과 같은 템포")]
        [Range(1f, 3f)] public float smashTempoBoost = 1.5f;

        [Header("스윙")]
        [Tooltip("스윙 시작부터 임팩트까지 (초)")]
        public float swingWindup = 0.25f;
        public float followThrough = 0.35f;
        [Tooltip("임팩트 몇 초 전부터 라켓을 뒤로 빼기 시작하는지")]
        public float takeBackLead = 0.9f;

        [Header("템포")]
        [Tooltip("공의 시간 배율. 공의 물리만 이만큼 느리게 진행한다. 궤적은 실제 테니스 그대로 낮게 유지되고 " +
                 "날아오는 시간만 길어진다 (1/배율). 선수 이동과 스윙은 실제 시간. 낮출수록 공이 떠 있는 느낌이 난다.")]
        [Range(0.4f, 2f)] public float ballTimeScale = 0.7f;

        [Header("구질")]
        [Tooltip("모든 샷의 속도 배율. 1 = 실제 랠리 속도. 스핀은 배율의 제곱으로 줄인다. " +
                 "낮추면 깊은 공을 보내려고 공이 높이 뜨므로, 템포는 공의 시간 배율로 조절하는 걸 권장.")]
        [Range(0.5f, 1.2f)] public float shotSpeedScale = 1f;
        public ShotProfile topspin = new ShotProfile { speed = 32f, shortSpeed = 32f, spin = 160f, minDepth = 7f, maxDepth = 9.3f };
        public ShotProfile slice = new ShotProfile { speed = 24f, shortSpeed = 11f, spin = -150f, minDepth = 2.2f, maxDepth = 9.5f };
        public ShotProfile lob = new ShotProfile { spin = 120f, minDepth = 8.5f, maxDepth = 10.8f, launchAngle = 50f };
        public ShotProfile smash = new ShotProfile { speed = 34f, shortSpeed = 34f, spin = 40f, minDepth = 7f, maxDepth = 9f };
        [Tooltip("좌우로 최대한 밀었을 때 목표 지점이 단식 사이드라인에서 안쪽으로 떨어진 거리 (m)")]
        public float aimMargin = 0.8f;

        [Header("발리")]
        [Tooltip("speed = 기본·깊은 발리, shortSpeed = 드롭 발리, minDepth = 드롭 발리 깊이, maxDepth = 깊은 발리 깊이")]
        public ShotProfile volley = new ShotProfile { speed = 24f, shortSpeed = 13f, spin = -60f, minDepth = 2.5f, maxDepth = 9f };
        [Tooltip("이보다 낮은 공은 발리하지 않는다 (m)")]
        public float volleyMinHeight = 0.2f;

        [Header("서브")]
        public ShotProfile flatServe = new ShotProfile { speed = 46f, spin = 40f };
        public ShotProfile sliceServe = new ShotProfile { speed = 40f, spin = 80f, sideSpin = 120f };
        public ShotProfile kickServe = new ShotProfile { speed = 34f, spin = 260f, sideSpin = -40f };
        [Tooltip("손에서 토스한 공이 올라가는 최고 높이 (m)")]
        public float serveTossApex = 3.15f;
        public float serveHandHeight = 1.5f;
        [Tooltip("가장 좋은 타점 높이 (m). 여기서 놓으면 타이밍 품질 최고")]
        public float serveIdealHeight = 2.9f;
        [Tooltip("이상적 높이에서 이만큼 벗어나면 타이밍 품질 0 (m)")]
        public float serveTimingWindow = 0.6f;
        [Tooltip("이보다 낮게 떨어진 뒤 놓으면 헛스윙 → 폴트 (m)")]
        public float serveHitMinHeight = 2.0f;
        [Tooltip("이 높이 아래로 떨어질 때까지 치지 않으면 다시 토스 (m)")]
        public float serveRetossHeight = 1.0f;
        [Tooltip("서비스라인에서 안쪽으로 노리는 거리 (m)")]
        public float serveDepthInset = 0.9f;
        [Tooltip("서비스 박스 옆 라인에서 안쪽으로 노리는 거리 (m)")]
        public float serveAimInset = 0.5f;
        public float secondServeSpeedScale = 0.85f;
        public float secondServeErrorScale = 0.6f;
        [Tooltip("서브 기본 오차 (도)")]
        public float serveBaseError = 0.5f;
        [Tooltip("타이밍 품질이 0일 때 더해지는 오차 (도)")]
        public float serveErrorPerBad = 2.5f;

        [Header("낮은 공")]
        [Tooltip("이 높이 아래로 오는 공은 높이 때문에 생기는 오차를 줄인다 (m)")]
        public float lowBallHeight = 1.3f;
        [Tooltip("낮은 공의 높이 오차 배율. 0 = 높이로 인한 오차 없음, 1 = 줄이지 않음")]
        [Range(0f, 1f)] public float lowBallErrorScale = 0.2f;

        [Header("타구 오차 (도)")]
        public float baseElevationError = 0.5f;
        public float elevationErrorPerBad = 2f;
        public float baseAzimuthError = 0.6f;
        public float azimuthErrorPerBad = 2.5f;
        [Tooltip("좌우 방향을 끝까지 줬을 때 타구 품질에 더해지는 벌점. 0이면 방향이 정확도에 영향 없음 " +
                 "(참고: 뛰면서 침 0.35, 품질 '좋음'은 합계 0.2 미만)")]
        [Range(0f, 0.5f)] public float aimErrorPenalty = 0.1f;

        [Header("보조")]
        [Tooltip("보조 많음: 선입력이 없으면 기본 스트로크를 자동으로 친다")]
        public bool autoStrokeWhenNoInput;
        public bool showLandingMarker = true;
        [Tooltip("위에서 본 코트 미니맵 (플레이 중 M 키로 켜고 끔)")]
        public bool showMinimap = true;
        public bool showHitRangeRing = true;
        public bool hitZoneAlert = true;

        [Header("AI")]
        public AiProfile aiEasy = new AiProfile
        {
            reactionDelay = 0.4f, speed = 5f, acceleration = 18f, errorScale = 0.9f, smartAim = 0.15f,
            sliceChance = 0.05f, lobChance = 0.04f, dropShotChance = 0f, lobVsNetChance = 0.3f,
            misjudgeChance = 0.2f, serveTimingError = 0.2f, firstServeFlatChance = 0.3f,
        };
        public AiProfile aiNormal = new AiProfile
        {
            reactionDelay = 0.25f, speed = 6f, acceleration = 24f, errorScale = 0.6f, smartAim = 0.5f,
            sliceChance = 0.15f, lobChance = 0.06f, dropShotChance = 0.04f, lobVsNetChance = 0.5f,
            misjudgeChance = 0.08f, serveTimingError = 0.13f, firstServeFlatChance = 0.5f,
        };
        public AiProfile aiHard = new AiProfile
        {
            reactionDelay = 0.12f, speed = 6.8f, acceleration = 30f, errorScale = 0.35f, smartAim = 0.85f,
            sliceChance = 0.2f, lobChance = 0.06f, dropShotChance = 0.07f, lobVsNetChance = 0.6f,
            misjudgeChance = 0.02f, serveTimingError = 0.08f, firstServeFlatChance = 0.6f,
        };
        [Tooltip("연습 모드에서 AI가 포인트를 시작할 때 보내는 공의 속도 배율. 낮추면 깊이 보내려고 공이 높이 뜬다.")]
        public float feedSpeedScale = 1f;

        [Header("경기")]
        public MatchRules defaultRules = new MatchRules { format = MatchFormat.OneSet };
        public AiDifficulty defaultDifficulty = AiDifficulty.Normal;
        public float pointResetDelay = 1.6f;
        [Tooltip("폴트·렛 뒤 다음 서브까지 (초)")]
        public float faultDelay = 1.1f;
        [Tooltip("연습 모드에서 공을 주기 전 대기 (초)")]
        public float feedDelay = 1f;

        public AiProfile AiProfileFor(AiDifficulty difficulty) => difficulty switch
        {
            AiDifficulty.Easy => aiEasy,
            AiDifficulty.Hard => aiHard,
            _ => aiNormal,
        };

        // Inspector에서 값이 바뀔 때 (에디터 전용). 코트와 공이 바로 다시 그려진다.
        public static event System.Action<TennisConfig> Changed;

        void OnValidate() => Changed?.Invoke(this);

        // 코트 크기와 공 크기를 전역(Court, BallPhysics)에 적용한다. 코트를 만들 때와 공이 생길 때 호출된다.
        public void ApplyGlobals()
        {
            Court.Configure(courtWidthScale, courtLengthScale);
            BallPhysics.SetRadius(ballDiameter * 0.5f);
        }
    }
}
