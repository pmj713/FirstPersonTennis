using UnityEngine;
using UnityEngine.InputSystem;

namespace FirstPersonTennis
{
    // WASD 이동은 코트 기준(W = 네트 쪽). 카메라 방향과 무관하다.
    [DefaultExecutionOrder(-20)]
    public class PlayerMovement : MonoBehaviour, IMover
    {
        [SerializeField] TennisConfig config;
        [SerializeField] int side = -1;

        public int Side => side;
        public Vector3 Velocity { get; private set; }
        public float Stamina01 { get; private set; } = 1f;
        public bool Exhausted { get; private set; }
        public bool IsSprinting { get; private set; }

        void Start() => FaceNet();

        void Update()
        {
            Vector2 input = Vector2.zero;
            bool sprintHeld = false;
            Keyboard kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.wKey.isPressed) input.y += 1f;
                if (kb.sKey.isPressed) input.y -= 1f;
                if (kb.dKey.isPressed) input.x += 1f;
                if (kb.aKey.isPressed) input.x -= 1f;
                sprintHeld = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
            }
            if (input.sqrMagnitude > 1f) input.Normalize();

            float facing = Court.Facing(side);
            var wish = new Vector3(input.x * facing, 0f, input.y * facing);
            bool moving = input.sqrMagnitude > 0.01f;
            IsSprinting = sprintHeld && moving && !Exhausted;

            float speed = IsSprinting ? config.runSpeed : config.walkSpeed;
            Velocity = Vector3.MoveTowards(Velocity, wish * speed, config.acceleration * Time.deltaTime);
            UpdateStamina(Time.deltaTime);

            transform.position = Clamp(transform.position + Velocity * Time.deltaTime);
        }

        public void ResetTo(Vector3 position)
        {
            transform.position = position;
            Velocity = Vector3.zero;
            Stamina01 = 1f;
            Exhausted = false;
            FaceNet();
        }

        void UpdateStamina(float dt)
        {
            if (IsSprinting)
            {
                float capacitySeconds = config.sprintDistance / config.runSpeed;
                Stamina01 -= dt / capacitySeconds;
                if (Stamina01 <= 0f)
                {
                    Stamina01 = 0f;
                    Exhausted = true;
                }
            }
            else
            {
                Stamina01 = Mathf.Min(1f, Stamina01 + config.staminaRecoveryPerSecond * dt);
                if (Exhausted && Stamina01 >= config.staminaResumeThreshold) Exhausted = false;
            }
        }

        Vector3 Clamp(Vector3 p)
        {
            p.x = Mathf.Clamp(p.x, -Court.PlayableHalfWidth, Court.PlayableHalfWidth);
            float depth = Mathf.Clamp(p.z * side, 0.5f, Court.PlayableDepth);
            p.z = depth * side;
            p.y = 0f;
            return p;
        }

        void FaceNet() => transform.rotation = Quaternion.LookRotation(new Vector3(0f, 0f, Court.Facing(side)));
    }
}
