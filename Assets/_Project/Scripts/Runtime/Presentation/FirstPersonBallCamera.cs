using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace FirstPersonTennis
{
    // 1인칭: 머리 위치에서 공을 따라본다. 회전 속도에 상한을 둬서 급회전(멀미)을 막는다.
    // 복식에서는 우리 코트 쪽에서 파트너가 받거나 친 공은 상대 코트 쪽을 보면서 공 쪽으로 조금만 고개를 돌린다.
    // V: 직접 플레이는 1/3인칭, AI 관전은 AI 1 → AI 2 → 심판. [ ]: 시야각 조절.
    // 시야각은 16:9 기준 수평값이고 세로 시야각을 고정하므로, 창이 넓으면 옆이 더 보이고 좁아도 왜곡되지 않는다.
    [RequireComponent(typeof(Camera))]
    public class FirstPersonBallCamera : MonoBehaviour
    {
        [SerializeField] TennisConfig config;
        [SerializeField] PlayerMovement player;
        [SerializeField] TennisBall ball;
        [Tooltip("튜토리얼 이동 과제에서는 공 대신 목표 원을 본다")]
        [SerializeField] TutorialDirector tutorial;
        [Tooltip("1인칭에서 안 보이게 할 내 몸 (그림자는 남긴다)")]
        [SerializeField] Renderer[] hideInFirstPerson;
        [SerializeField] float maxTurnSpeed = 300f;
        [SerializeField] float turnSharpness = 10f;
        [SerializeField] float maxPitch = 75f;
        [SerializeField] Vector3 thirdPersonOffset = new Vector3(0f, 3.2f, -6.5f);

        public enum WatchView { Ai1, Ai2, Umpire }
        public WatchView CurrentWatchView { get; private set; }
        public static WatchView NextWatchView(WatchView view) => (WatchView)(((int)view + 1) % 3);
        public bool ThirdPerson { get; private set; }
        public string ViewLabel => watching ? CurrentWatchView switch
        {
            WatchView.Ai1 => "AI 1 시점", WatchView.Ai2 => "AI 2 시점", _ => "심판 시점",
        } : ThirdPerson ? "3인칭 (디버그)" : "1인칭";
        MatchReferee referee;
        bool watching, snapWatch;
        Renderer[] hiddenAi;
        ShadowCastingMode[] originalShadows;
        public float HorizontalFov => UserSettings.HorizontalFov(config.horizontalFov); // 설정 화면에서도 바꾼다
        public bool FollowingBall { get; private set; }

        const float FovStep = 5f;
        const float ReferenceAspect = 16f / 9f;

        Camera cam;
        AutoSwing swing;   // 내 선수의 스윙: 복식 팀원과 서브 리시버 여부
        PlayerServe serve;
        bool ballIsMine;   // 복식: 지금 오는 공을 내가 받는지 (한 번 내 공이 되면 그 공이 끝날 때까지)

        void Awake()
        {
            cam = GetComponent<Camera>();
            swing = player.GetComponent<AutoSwing>();
            serve = player.GetComponent<PlayerServe>();
        }

        void OnEnable() { if (cam != null) { snapWatch = true; ApplyVisibility(); } }

        void Start()
        {
            foreach (var match in FindObjectsByType<MatchReferee>())
                if (match.Player == player) { referee = match; break; }
            ApplyVisibility();
        }

        void UpdateWatchMode()
        {
            bool next = referee != null && referee.Watching;
            if (next == watching) return;
            watching = next;
            CurrentWatchView = WatchView.Ai1;
            snapWatch = true;
            ApplyVisibility();
        }

        void LateUpdate()
        {
            UpdateWatchMode();
            Keyboard kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.vKey.wasPressedThisFrame)
                {
                    if (watching) { CurrentWatchView = NextWatchView(CurrentWatchView); snapWatch = true; }
                    else ThirdPerson = !ThirdPerson;
                    ApplyVisibility();
                }
                if (kb.leftBracketKey.wasPressedThisFrame) ChangeFov(-FovStep);
                if (kb.rightBracketKey.wasPressedThisFrame) ChangeFov(FovStep);
            }

            if (watching)
            {
                UpdateWatchCamera();
                cam.fieldOfView = Camera.HorizontalToVerticalFieldOfView(HorizontalFov, ReferenceAspect);
                return;
            }
            float dt = Time.unscaledDeltaTime;
            float facing = Court.Facing(player.Side);
            Vector3 body = player.transform.position;

            if (ThirdPerson)
            {
                Vector3 desired = body + new Vector3(thirdPersonOffset.x * facing, thirdPersonOffset.y, thirdPersonOffset.z * facing);
                transform.position = Vector3.Lerp(transform.position, desired, 1f - Mathf.Exp(-8f * dt));
            }
            else
            {
                transform.position = body + Vector3.up * config.eyeHeight;
            }

            // 볼 방향: 튜토리얼 목표 > 공 (복식에서 내 공이 아니면 상대 코트 쪽과 공 사이를 정한 비율만큼)
            float follow = BallFollow();
            FollowingBall = follow >= 1f;
            bool lookAtTarget = tutorial != null && tutorial.TargetActive;
            Vector3 forward = new Vector3(0f, 1f, Court.HalfLength * 0.6f * facing) - transform.position;
            Vector3 dir = lookAtTarget ? tutorial.TargetPosition - transform.position :
                follow > 0f ? ball.transform.position - transform.position : forward;
            if (dir.sqrMagnitude > 1e-4f && forward.sqrMagnitude > 1e-4f)
            {
                Quaternion look = Quaternion.LookRotation(dir);
                if (!lookAtTarget && follow < 1f) look = Quaternion.Slerp(Quaternion.LookRotation(forward), look, follow);
                Quaternion target = ClampPitch(look);
                float turn = UserSettings.CameraTurnSpeed;
                Quaternion smooth = Quaternion.Slerp(transform.rotation, target, 1f - Mathf.Exp(-turnSharpness * turn * dt));
                transform.rotation = Quaternion.RotateTowards(transform.rotation, smooth, maxTurnSpeed * turn * dt);
            }

            cam.fieldOfView = Camera.HorizontalToVerticalFieldOfView(HorizontalFov, ReferenceAspect);
        }

        // 공을 얼마나 따라볼지: 1 = 공을 본다, 0 = 상대 코트 쪽 (공이 없을 때).
        // 단식·관전은 공이 날고 있으면 늘 1. 복식도 상대 코트 위의 공과 내 서브 토스, 내가 받을 서브,
        // 팀원보다 내가 떨어질 자리에 가까운 공은 1이고, 우리 코트 쪽에서 팀원이 받거나 친 공(팀원의 토스 포함)만
        // 설정한 비율만큼 공 쪽으로 고개를 돌린다 (뒤·옆에서 팀원이 칠 때 고개가 완전히 돌아가지 않게)
        float BallFollow()
        {
            if (!ball.IsLive) return 0f;
            AutoSwing mate = swing != null ? swing.Teammate : null;
            if (mate == null || !mate.isActiveAndEnabled) return 1f;
            if (serve != null && serve.MyTurn) return 1f;
            bool incoming = ball.LastHitterSide == -player.Side;
            if (!incoming) ballIsMine = false;
            if (Court.SideOf(ball.transform.position.z) != player.Side) return 1f;
            float partial = config.doublesBallFollow;
            if (!incoming) return partial;
            if (ball.IsServe) return swing.MayReturnServe ? 1f : partial;
            if (!ballIsMine)
            {
                Vector3 reference = swing.HasLanding ? swing.LandingPoint : ball.transform.position;
                ballIsMine = Flat(reference - player.transform.position) <= Flat(reference - mate.transform.position);
            }
            return ballIsMine ? 1f : partial;
        }

        static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;

        void ChangeFov(float delta)
        {
            UserSettings.SetHorizontalFov(HorizontalFov + delta);
            UserSettings.Save();
        }

        Quaternion ClampPitch(Quaternion q)
        {
            Vector3 e = q.eulerAngles;
            float pitch = e.x > 180f ? e.x - 360f : e.x;
            return Quaternion.Euler(Mathf.Clamp(pitch, -maxPitch, maxPitch), e.y, 0f);
        }

        void UpdateWatchCamera()
        {
            AiOpponent targetAi = CurrentWatchView == WatchView.Ai1 ? referee.NearAi : referee.Ai;
            bool umpire = CurrentWatchView == WatchView.Umpire;
            if (!umpire && targetAi == null) return;
            Vector3 position = umpire
                ? new Vector3(Court.NetPostX + 1.0f, 2.7f, 0f)
                : targetAi.transform.position + Vector3.up * config.eyeHeight;
            Vector3 target = ball.IsLive ? ball.transform.position : umpire
                ? new Vector3(0f, 0.6f, 0f)
                : new Vector3(0f, 1f, Court.HalfLength * 0.6f * Court.Facing(
                    CurrentWatchView == WatchView.Ai1 ? referee.PlayerSide : referee.AiSide));
            transform.position = position;
            Vector3 direction = target - position;
            if (direction.sqrMagnitude > 1e-4f)
            {
                Quaternion look = ClampPitch(Quaternion.LookRotation(direction));
                float dt = Time.unscaledDeltaTime, turn = UserSettings.CameraTurnSpeed;
                Quaternion smooth = Quaternion.Slerp(transform.rotation, look, 1f - Mathf.Exp(-turnSharpness * turn * dt));
                transform.rotation = snapWatch ? look : Quaternion.RotateTowards(transform.rotation, smooth, maxTurnSpeed * turn * dt);
                snapWatch = false;
            }
            FollowingBall = ball.IsLive;
        }

        void RestoreAiVisibility()
        {
            if (hiddenAi == null) return;
            for (int i = 0; i < hiddenAi.Length; i++)
                if (hiddenAi[i] != null) hiddenAi[i].shadowCastingMode = originalShadows[i];
            hiddenAi = null;
            originalShadows = null;
        }

        void OnDisable()
        {
            RestoreAiVisibility();
            foreach (Renderer r in hideInFirstPerson ?? System.Array.Empty<Renderer>())
                if (r != null) r.shadowCastingMode = ShadowCastingMode.On;
        }

        void ApplyVisibility()
        {
            RestoreAiVisibility();
            foreach (Renderer r in hideInFirstPerson ?? System.Array.Empty<Renderer>())
                if (r != null) r.shadowCastingMode = !watching && ThirdPerson ? ShadowCastingMode.On : ShadowCastingMode.ShadowsOnly;
            if (!watching || CurrentWatchView == WatchView.Umpire) return;
            AiOpponent ai = CurrentWatchView == WatchView.Ai1 ? referee.NearAi : referee.Ai;
            if (ai == null) return;
            hiddenAi = ai.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            originalShadows = new ShadowCastingMode[hiddenAi.Length];
            for (int i = 0; i < hiddenAi.Length; i++)
            {
                originalShadows[i] = hiddenAi[i].shadowCastingMode;
                hiddenAi[i].shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            }
        }
    }
}
