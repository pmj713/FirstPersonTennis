using UnityEngine;
using UnityEngine.InputSystem;

namespace FirstPersonTennis
{
    // 1인칭: 머리 위치에서 공을 따라본다. 회전 속도에 상한을 둬서 급회전(멀미)을 막는다.
    // V 키로 비교 테스트용 3인칭 디버그 카메라로 전환.
    [RequireComponent(typeof(Camera))]
    public class FirstPersonBallCamera : MonoBehaviour
    {
        [SerializeField] TennisConfig config;
        [SerializeField] PlayerMovement player;
        [SerializeField] TennisBall ball;
        [SerializeField] Renderer[] hideInFirstPerson;
        [SerializeField] float maxTurnSpeed = 300f;
        [SerializeField] float turnSharpness = 10f;
        [SerializeField] float horizontalFov = 90f;
        [SerializeField] float maxPitch = 75f;
        [SerializeField] Vector3 thirdPersonOffset = new Vector3(0f, 3.2f, -6.5f);

        public bool ThirdPerson { get; private set; }

        Camera cam;

        void Awake() => cam = GetComponent<Camera>();

        void Start() => ApplyVisibility();

        void LateUpdate()
        {
            Keyboard kb = Keyboard.current;
            if (kb != null && kb.vKey.wasPressedThisFrame)
            {
                ThirdPerson = !ThirdPerson;
                ApplyVisibility();
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

            Vector3 look = ball.IsLive ? ball.transform.position : new Vector3(0f, 1f, Court.HalfLength * 0.6f * facing);
            Vector3 dir = look - transform.position;
            if (dir.sqrMagnitude > 1e-4f)
            {
                Quaternion target = ClampPitch(Quaternion.LookRotation(dir));
                Quaternion smooth = Quaternion.Slerp(transform.rotation, target, 1f - Mathf.Exp(-turnSharpness * dt));
                transform.rotation = Quaternion.RotateTowards(transform.rotation, smooth, maxTurnSpeed * dt);
            }

            cam.fieldOfView = Camera.HorizontalToVerticalFieldOfView(horizontalFov, cam.aspect);
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
                if (r != null) r.enabled = ThirdPerson;
        }
    }
}
