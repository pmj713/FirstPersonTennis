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
    }
}
