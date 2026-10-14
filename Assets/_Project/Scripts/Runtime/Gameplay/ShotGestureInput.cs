using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FirstPersonTennis
{
    // 좌클릭 → 드래그 → 놓기로 구질을 선입력한다. 놓는 순간 확정.
    // 가로축 = 방향, 세로축 = 구질. 입력은 상대가 친 순간부터 받는다.
    // 내 서브 차례에는 같은 입력을 서브(누르면 토스, 놓으면 타격)로 넘긴다.
    [DefaultExecutionOrder(-15)]
    public class ShotGestureInput : MonoBehaviour, IShotIntentSource
    {
        [SerializeField] TennisBall ball;
        [SerializeField] AutoSwing swing;

        // 길이는 모두 화면 높이 대비 비율 (해상도와 무관)
        public const float DeadZone = 0.03f;       // 좌우 방향
        public const float SliceThreshold = 0.05f;
        public const float LobThreshold = 0.08f;   // 로브는 실수로 나가면 치명적이라 더 길게 밀어야 한다
        public const float MinVerticalAngle = 25f; // 이보다 눕힌 드래그는 좌우 방향으로만 본다 (도)
        public const float FullAim = 0.25f;
        public const float FullDepth = 0.3f;
        public const float SmashStroke = 0.08f;

        public bool HasIntent { get; private set; }
        public ShotIntent Intent { get; private set; }
        public bool SkipRequested { get; private set; }
        public bool IsDragging { get; private set; }
        public Vector2 DragStart { get; private set; }
        public Vector2 DragCurrent { get; private set; }
        public ShotIntent DragPreview { get; private set; }

        public bool WindowOpen => ball.IsLive && ball.LastHitterSide == -swing.Side && !swing.IsSwinging;
        public bool ServeMode => serve != null && serve.WantsInput;
        public bool InputEnabled { get; set; } = true;

        public event Action IntentRejected; // 입력 가능 시점이 아닐 때 드래그를 놓음

        IServeInput serve;
        float maxRise;

        void Awake() => serve = GetComponent<IServeInput>();

        void Update()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null || !InputEnabled)
            {
                IsDragging = false;
                return;
            }

            Vector2 position = mouse.position.ReadValue();
            float height = Mathf.Max(1, Screen.height);

            if (mouse.leftButton.wasPressedThisFrame)
            {
                IsDragging = true;
                DragStart = position;
                maxRise = 0f;
                if (ServeMode) serve.OnPress();
            }

            if (IsDragging)
            {
                DragCurrent = position;
                Vector2 delta = (position - DragStart) / height;
                maxRise = Mathf.Max(maxRise, delta.y);
                DragPreview = Classify(delta, maxRise);

                if (mouse.leftButton.wasReleasedThisFrame)
                {
                    IsDragging = false;
                    if (ServeMode) serve.OnRelease(DragPreview);
                    else if (WindowOpen) SubmitIntent(DragPreview);
                    else IntentRejected?.Invoke();
                }
            }

            if (mouse.rightButton.wasPressedThisFrame && WindowOpen) RequestSkip();
        }

        public void SubmitIntent(ShotIntent intent)
        {
            Intent = intent;
            HasIntent = true;
            SkipRequested = false;
        }

        public void RequestSkip()
        {
            HasIntent = false;
            SkipRequested = true;
        }

        // delta: 드래그 이동량 (화면 높이 대비), maxRise: 드래그 중 가장 높이 올라간 양
        public static ShotIntent Classify(Vector2 delta, float maxRise)
        {
            var intent = new ShotIntent { Type = ShotType.Topspin, Depth = 1f };
            if (Mathf.Abs(delta.x) >= DeadZone) intent.Aim = Mathf.Clamp(delta.x / FullAim, -1f, 1f);

            bool smash = maxRise >= SmashStroke && maxRise - delta.y >= SmashStroke;
            bool steep = Mathf.Abs(delta.y) >= Mathf.Abs(delta.x) * Mathf.Tan(MinVerticalAngle * Mathf.Deg2Rad);
            if (smash)
            {
                intent.Type = ShotType.Smash;
            }
            else if (steep && delta.y <= -SliceThreshold)
            {
                intent.Type = ShotType.Slice;
                intent.Depth = Mathf.Clamp01((-delta.y - SliceThreshold) / (FullDepth - SliceThreshold));
            }
            else if (steep && delta.y >= LobThreshold)
            {
                intent.Type = ShotType.Lob;
                intent.Depth = Mathf.Clamp01((delta.y - LobThreshold) / (FullDepth - LobThreshold));
            }
            return intent;
        }

        public bool TryGetIntent(out ShotIntent intent)
        {
            intent = Intent;
            return HasIntent;
        }

        public void OnSwingCommitted() => HasIntent = false;

        public void OnNewIncomingBall()
        {
            HasIntent = false;
            SkipRequested = false;
        }
    }
}
