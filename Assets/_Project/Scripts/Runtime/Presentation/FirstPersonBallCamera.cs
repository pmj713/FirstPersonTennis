using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace FirstPersonTennis
{
    // 1인칭: 머리 위치에서 공을 따라본다. 회전 속도에 상한을 둬서 급회전(멀미)을 막는다.
    // V 키로 비교 테스트용 3인칭 디버그 카메라로 전환. [ ] 키로 시야각 조절.
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

        public bool ThirdPerson { get; private set; }
        public float HorizontalFov => UserSettings.HorizontalFov(config.horizontalFov); // 설정 화면에서도 바꾼다

        const float FovStep = 5f;
        const float ReferenceAspect = 16f / 9f;

        Camera cam;

        void Awake() => cam = GetComponent<Camera>();

        void Start() => ApplyVisibility();

        void LateUpdate()
        {
            Keyboard kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.vKey.wasPressedThisFrame)
                {
                    ThirdPerson = !ThirdPerson;
                    ApplyVisibility();
                }
                if (kb.leftBracketKey.wasPressedThisFrame) ChangeFov(-FovStep);
                if (kb.rightBracketKey.wasPressedThisFrame) ChangeFov(FovStep);
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

            Vector3 look = tutorial != null && tutorial.TargetActive ? tutorial.TargetPosition :
                ball.IsLive ? ball.transform.position : new Vector3(0f, 1f, Court.HalfLength * 0.6f * facing);
            Vector3 dir = look - transform.position;
            if (dir.sqrMagnitude > 1e-4f)
            {
                Quaternion target = ClampPitch(Quaternion.LookRotation(dir));
                float turn = UserSettings.CameraTurnSpeed;
                Quaternion smooth = Quaternion.Slerp(transform.rotation, target, 1f - Mathf.Exp(-turnSharpness * turn * dt));
                transform.rotation = Quaternion.RotateTowards(transform.rotation, smooth, maxTurnSpeed * turn * dt);
            }

            cam.fieldOfView = Camera.HorizontalToVerticalFieldOfView(HorizontalFov, ReferenceAspect);
        }

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

        void ApplyVisibility()
        {
            foreach (Renderer r in hideInFirstPerson)
                if (r != null) r.shadowCastingMode = ThirdPerson ? ShadowCastingMode.On : ShadowCastingMode.ShadowsOnly;
        }
    }
}
