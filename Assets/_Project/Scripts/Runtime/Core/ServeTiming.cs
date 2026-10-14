using UnityEngine;

namespace FirstPersonTennis
{
    // 서브 토스와 타이밍 품질. 플레이어와 AI가 같은 규칙을 쓴다.
    public static class ServeTiming
    {
        // 손 높이에서 토스 최고 높이까지 올리는 수직 속도 (공기저항만큼 살짝 더)
        public static float TossSpeed(TennisConfig config)
        {
            float rise = Mathf.Max(0.5f, config.serveTossApex - config.serveHandHeight);
            return Mathf.Sqrt(2f * 9.81f * rise) * 1.02f;
        }

        // 1 = 이상적 높이에서 침, 0 = 창 밖
        public static float Quality(TennisConfig config, float height) =>
            1f - Mathf.Clamp01(Mathf.Abs(height - config.serveIdealHeight) / Mathf.Max(0.01f, config.serveTimingWindow));

        public static bool CanHit(TennisConfig config, float height) => height >= config.serveHitMinHeight;
    }
}
