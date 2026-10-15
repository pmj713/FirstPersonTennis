using UnityEngine;

namespace FirstPersonTennis
{
    // 코트 좌표계: 네트가 z = 0, 근거리(플레이어) 쪽이 z < 0, 원거리(상대) 쪽이 z > 0. 단위는 m.
    // side: -1 = 근거리, +1 = 원거리. 각 선수는 네트를 바라보므로 facing = -side.
    // 크기 배율은 TennisConfig에서 정하고 ApplyGlobals()로 들어온다. 기본은 ITF 규격.
    public static class Court
    {
        public const float ItfHalfLength = 11.885f;
        public const float ItfSinglesHalfWidth = 4.115f;
        public const float ItfDoublesHalfWidth = 5.485f;
        public const float ItfServiceLine = 6.40f;
        public const float NetCenterHeight = 0.914f;
        public const float NetPostHeight = 1.07f;

        const float NetPostOutside = 0.914f; // 기둥은 복식 사이드라인 밖 0.914m
        const float LineTolerance = 0.02f;

        public static float WidthScale { get; private set; } = 1f;
        public static float LengthScale { get; private set; } = 1f;

        public static float HalfLength => ItfHalfLength * LengthScale;
        public static float SinglesHalfWidth => ItfSinglesHalfWidth * WidthScale;
        public static float DoublesHalfWidth => ItfDoublesHalfWidth * WidthScale;
        public static float ServiceLine => ItfServiceLine * LengthScale;
        public static float NetPostX => DoublesHalfWidth + NetPostOutside;

        // 선수가 움직일 수 있는 범위
        public static float PlayableHalfWidth => DoublesHalfWidth + 3.5f;
        public static float PlayableDepth => HalfLength + 5f;

        public static void Configure(float widthScale, float lengthScale)
        {
            WidthScale = Mathf.Max(0.1f, widthScale);
            LengthScale = Mathf.Max(0.1f, lengthScale);
        }

        // 지금 경기하는 코트 바닥. 공 물리·궤적 예측·샷 계획·선수 이동이 모두 이 값을 쓴다.
        public static CourtType Type { get; private set; } = CourtType.Hard;
        public static SurfaceProfile Surface { get; private set; } = SurfaceProfile.Hard;
        public static event System.Action SurfaceChanged;

        public static void SetSurface(CourtType type, SurfaceProfile surface)
        {
            Type = type;
            Surface = surface;
            SurfaceChanged?.Invoke();
        }

        // 바닥에 따른 가감속 (m/s²): 지금 속도를 줄이는 쪽이면 제동 배율, 아니면 출발 배율
        public static float MoveRate(Vector3 velocity, Vector3 target, float acceleration)
        {
            bool slowing = Vector3.Dot(target - velocity, velocity) < 0f;
            float grip = slowing ? Surface.braking : Surface.traction;
            return acceleration * (grip > 0f ? grip : 1f);
        }

        // 바닥에서 낼 수 있는 제동 (m/s²)
        public static float BrakeRate(float acceleration) =>
            acceleration * (Surface.braking > 0f ? Surface.braking : 1f);

        public static float NetHeightAt(float x) =>
            Mathf.Lerp(NetCenterHeight, NetPostHeight, Mathf.Clamp01(Mathf.Abs(x) / NetPostX));

        public static int SideOf(float z) => z < 0f ? -1 : 1;

        public static float Facing(int side) => -side;

        // 단식 코트 안에 떨어졌는지. 라인에 닿으면 인.
        public static bool IsInSingles(Vector3 point, int side)
        {
            if (Mathf.Abs(point.x) > SinglesHalfWidth + LineTolerance) return false;
            float depth = point.z * side;
            return depth > 0f && depth <= HalfLength + LineTolerance;
        }

        // 리시버 쪽 서비스 박스 (x 범위, 네트에서의 깊이 범위). 듀스 코트 = 리시버 기준 오른쪽.
        public static void ServiceBox(int receiverSide, bool deuce, out float xMin, out float xMax)
        {
            float rightSign = Facing(receiverSide); // 리시버의 오른쪽이 월드 +x인지
            float sign = deuce ? rightSign : -rightSign;
            xMin = sign > 0f ? 0f : -SinglesHalfWidth;
            xMax = sign > 0f ? SinglesHalfWidth : 0f;
        }

        public static bool IsInServiceBox(Vector3 point, int receiverSide, bool deuce)
        {
            ServiceBox(receiverSide, deuce, out float xMin, out float xMax);
            if (point.x < xMin - LineTolerance || point.x > xMax + LineTolerance) return false;
            float depth = point.z * receiverSide;
            return depth > 0f && depth <= ServiceLine + LineTolerance;
        }

        // 서버가 서는 자리 (베이스라인 뒤, 듀스면 서버 기준 오른쪽)
        public static Vector3 ServePosition(int serverSide, bool deuce, float offsetFromCenter)
        {
            float rightSign = Facing(serverSide);
            float x = (deuce ? rightSign : -rightSign) * offsetFromCenter;
            return new Vector3(x, 0f, (HalfLength + 0.3f) * serverSide);
        }

        // 리시버가 기다리는 자리 (대각선, 베이스라인 뒤)
        public static Vector3 ReceivePosition(int receiverSide, bool deuce)
        {
            float rightSign = Facing(receiverSide);
            float x = (deuce ? rightSign : -rightSign) * SinglesHalfWidth * 0.55f;
            return new Vector3(x, 0f, (HalfLength + 0.8f) * receiverSide);
        }
    }
}
