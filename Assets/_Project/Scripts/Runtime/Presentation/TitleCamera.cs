using UnityEngine;

namespace FirstPersonTennis
{
    // 메인 화면 배경: 관중석 위 높이에서 코트를 바라보며 천천히 원(경기장 모양을 따라 타원)을 그린다. 높이도 조금 오르내린다.
    // 왼쪽 메뉴에 가리지 않게 코트가 화면 오른쪽으로 오도록 시선을 코트 중심의 왼쪽으로 둔다
    [RequireComponent(typeof(Camera))]
    public class TitleCamera : MonoBehaviour
    {
        [SerializeField] float sideMargin = 10f;      // 옆 펜스 밖으로 떨어진 거리 (m, 옆 관중석 위)
        [SerializeField] float endMargin = 10f;       // 끝 펜스 밖으로 떨어진 거리 (m, 끝 관중석 위)
        [SerializeField] float height = 18f;          // 관중석(약 6.5m)보다 높게
        [SerializeField] float heightSwing = 1.2f;
        [SerializeField] float degreesPerSecond = 6f; // 한 바퀴 1분
        [SerializeField] float startAngle = 215f;
        [SerializeField] float lookHeight = 0.6f;
        [SerializeField] float lookShift = 7f;        // 시선을 코트 중심에서 왼쪽으로 옮기는 거리 → 코트가 화면 오른쪽에
        [SerializeField] float fieldOfView = 50f;

        public float Angle { get; private set; }

        void Start()
        {
            Angle = startAngle;
            GetComponent<Camera>().fieldOfView = fieldOfView;
            Place();
        }

        void LateUpdate()
        {
            Angle = Mathf.Repeat(Angle + degreesPerSecond * Time.unscaledDeltaTime, 360f);
            Place();
        }

        void Place()
        {
            float a = Angle * Mathf.Deg2Rad;
            float rx = StadiumView.FenceHalfWidth + sideMargin, rz = StadiumView.FenceHalfLength + endMargin;
            var pos = new Vector3(Mathf.Sin(a) * rx, height + heightSwing * Mathf.Sin(Time.unscaledTime * 0.25f), Mathf.Cos(a) * rz);
            var center = new Vector3(0f, lookHeight, 0f);
            Vector3 flat = Vector3.ProjectOnPlane(center - pos, Vector3.up).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, flat);
            Vector3 target = center - right * lookShift;
            transform.SetPositionAndRotation(pos, Quaternion.LookRotation(target - pos));
        }
    }
}
