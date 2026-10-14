using UnityEngine;

namespace FirstPersonTennis
{
    // 선입력을 주는 쪽 (플레이어 마우스 제스처, 허수아비 AI)
    public interface IShotIntentSource
    {
        bool TryGetIntent(out ShotIntent intent);
        bool SkipRequested { get; }
        void OnSwingCommitted();
        void OnNewIncomingBall();
    }

    // 서브 차례일 때 마우스 입력을 받는 쪽 (누르면 토스, 놓으면 타격)
    public interface IServeInput
    {
        bool WantsInput { get; }
        void OnPress();
        void OnRelease(ShotIntent intent);
    }

    // 타구 품질 계산에 쓰는 이동 상태
    public interface IMover
    {
        Vector3 Velocity { get; }
        bool Exhausted { get; }
    }
}
