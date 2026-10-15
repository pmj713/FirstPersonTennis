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
        public bool InputEnabled { get; set; } = true;
        public PlayerStats Stats { get; set; } = PlayerStats.Neutral; // 속도 스탯: 걷기·달리기 속도, 스테미너 회복

        // 서브 준비 중: 베이스라인 뒤에서 좌우로만 움직인다
        bool serveLock;
        float serveMinX, serveMaxX, serveZ;

        void Start() => FaceNet();

        public void LockForServe(float minX, float maxX, float z)
        {
            serveLock = true;
            serveMinX = Mathf.Min(minX, maxX);
            serveMaxX = Mathf.Max(minX, maxX);
            serveZ = z;
            Velocity = Vector3.zero;
        }

        public void UnlockServe() => serveLock = false;

        void Update()
        {
            Vector2 input = Vector2.zero;
            bool sprintHeld = false;
            Keyboard kb = InputEnabled ? Keyboard.current : null;
            if (kb != null)
            {
                if (kb.wKey.isPressed) input.y += 1f;
                if (kb.sKey.isPressed) input.y -= 1f;
                if (kb.dKey.isPressed) input.x += 1f;
                if (kb.aKey.isPressed) input.x -= 1f;
                sprintHeld = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
            }
            if (input.sqrMagnitude > 1f) input.Normalize();
            if (serveLock)
            {
                input.y = 0f;
                sprintHeld = false;
            }

            float facing = Court.Facing(side);
            var wish = new Vector3(input.x * facing, 0f, input.y * facing);
            bool moving = input.sqrMagnitude > 0.01f;
            IsSprinting = sprintHeld && moving && !Exhausted;

            float speed = (IsSprinting ? config.runSpeed : config.walkSpeed) * Stats.MoveScale;
            Vector3 target = wish * speed;
            Velocity = Vector3.MoveTowards(Velocity, target, Court.MoveRate(Velocity, target, config.acceleration) * Time.deltaTime);
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
                float capacitySeconds = config.SprintDistance / (config.runSpeed * Stats.MoveScale);
                Stamina01 -= dt / capacitySeconds;
                if (Stamina01 <= 0f)
                {
                    Stamina01 = 0f;
                    Exhausted = true;
                }
            }
            else
            {
                Stamina01 = Mathf.Min(1f, Stamina01 + config.staminaRecoveryPerSecond * Stats.RecoveryScale * dt);
                if (Exhausted && Stamina01 >= config.staminaResumeThreshold) Exhausted = false;
            }
        }

        Vector3 Clamp(Vector3 p)
        {
            if (serveLock)
            {
                p.x = Mathf.Clamp(p.x, serveMinX, serveMaxX);
                p.z = serveZ;
                p.y = 0f;
                return p;
            }
            p.x = Mathf.Clamp(p.x, -Court.PlayableHalfWidth, Court.PlayableHalfWidth);
            float depth = Mathf.Clamp(p.z * side, 0.5f, Court.PlayableDepth);
            p.z = depth * side;
            p.y = 0f;
            return p;
        }

        void FaceNet() => transform.rotation = Quaternion.LookRotation(new Vector3(0f, 0f, Court.Facing(side)));
    }
}
