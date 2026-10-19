using UnityEngine;

namespace FirstPersonTennis
{
    // 공 모델을 공의 각속도대로 돌려 탑스핀·슬라이스를 눈으로 읽을 수 있게 한다.
    // 실제 회전(수백 rad/s)을 그대로 돌리면 프레임 사이에서 거꾸로 도는 것처럼 보여서, 방향은 두고 보이는 속도만 줄인다.
    public class BallSpinVisual : MonoBehaviour
    {
        [SerializeField] TennisBall ball;
        [Tooltip("화면에 보이는 최대 회전 속도 (rad/s). 이보다 빠르면 프레임 사이에서 거꾸로 도는 것처럼 보인다")]
        [SerializeField] float maxVisibleSpin = 25f;

        void Update()
        {
            if (ball == null || !ball.IsLive) return;
            Vector3 spin = ball.State.Spin;
            float rate = spin.magnitude;
            if (rate < 1e-3f) return;
            // 회전축·방향은 공 물리(BallPhysics)의 각속도와 같다: 표면 점의 속도 = spin × r
            float degrees = Mathf.Min(rate, maxVisibleSpin) * Mathf.Rad2Deg * Time.deltaTime * ball.TimeScale;
            transform.Rotate(spin / rate, degrees, Space.World);
        }
    }
}
