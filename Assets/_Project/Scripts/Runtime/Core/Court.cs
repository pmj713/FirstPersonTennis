using UnityEngine;

namespace FirstPersonTennis
{
    // 코트 좌표계: 네트가 z = 0, 근거리(플레이어) 쪽이 z < 0, 원거리(상대) 쪽이 z > 0. 단위는 m.
    // side: -1 = 근거리, +1 = 원거리. 각 선수는 네트를 바라보므로 facing = -side.
    public static class Court
    {
        public const float HalfLength = 11.885f;
        public const float SinglesHalfWidth = 4.115f;
        public const float DoublesHalfWidth = 5.485f;
        public const float ServiceLine = 6.40f;
        public const float NetCenterHeight = 0.914f;
        public const float NetPostHeight = 1.07f;
        public const float NetPostX = 6.40f;

        const float LineTolerance = 0.02f;

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
