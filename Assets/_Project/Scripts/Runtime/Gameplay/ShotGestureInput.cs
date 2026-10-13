using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FirstPersonTennis
{
    // 좌클릭 → 드래그 → 놓기로 구질을 선입력한다. 놓는 순간 확정.
    // 가로축 = 방향, 세로축 = 구질. 입력은 상대가 친 순간부터 받는다.
    [DefaultExecutionOrder(-15)]
    public class ShotGestureInput : MonoBehaviour, IShotIntentSource
    {
        [SerializeField] TennisBall ball;
        [SerializeField] AutoSwing swing;

        // 모두 화면 높이 대비 비율 (해상도와 무관)
        public const float DeadZone = 0.03f;
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

        public event Action IntentRejected; // 입력 가능 시점이 아닐 때 드래그를 놓음

        float maxRise;

        void Update()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null) return;

            Vector2 position = mouse.position.ReadValue();
            float height = Mathf.Max(1, Screen.height);

            if (mouse.leftButton.wasPressedThisFrame)
            {
                IsDragging = true;
                DragStart = position;
                maxRise = 0f;
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
                    if (WindowOpen) SubmitIntent(DragPreview);
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
            if (smash)
            {
                intent.Type = ShotType.Smash;
            }
            else if (delta.y <= -DeadZone)
            {
                intent.Type = ShotType.Slice;
                intent.Depth = Mathf.Clamp01((-delta.y - DeadZone) / (FullDepth - DeadZone));
            }
            else if (delta.y >= DeadZone)
            {
                intent.Type = ShotType.Lob;
                intent.Depth = Mathf.Clamp01((delta.y - DeadZone) / (FullDepth - DeadZone));
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
