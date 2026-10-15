using System;
using UnityEngine;

namespace FirstPersonTennis
{
    public enum CourtType { Hard, Clay, Grass }

    // 코트 바닥 한 종류: 공이 튀는 방식, 선수가 움직이는 느낌, 코트 색
    [Serializable]
    public struct SurfaceProfile
    {
        public string name;

        [Tooltip("수직 반발 계수. 클수록 높이 튄다.")]
        [Range(0.5f, 0.95f)] public float restitution;

        [Tooltip("바운드 마찰 계수. 클수록 바운드 후 느려지고 스핀 영향이 커진다.")]
        [Range(0f, 1.2f)] public float friction;

        [Tooltip("바운드 뒤 수평 속도를 이만큼만 남긴다 (마찰과 별개). 1 = 그대로, 클레이처럼 바닥이 무르면 1보다 작다.")]
        [Range(0.6f, 1f)] public float speedRetention;

        [Tooltip("출발·가속 배율. 1 = 하드 코트 기준")]
        [Range(0.3f, 1.2f)] public float traction;

        [Tooltip("멈추거나 방향을 바꿀 때 감속 배율. 작을수록 미끄러진다.")]
        [Range(0.2f, 1.2f)] public float braking;

        [Tooltip("공이 떨어진 자리에 자국이 남는다 (클레이)")]
        public bool leavesMarks;

        public Color courtColor;
        public Color surroundColor;

        [Header("AI 코트 성향 (난이도·선수 확률에 더함, 0 = 그대로)")]
        public float aiNetRushBonus;
        public float aiServeVolleyBonus;
        public float aiSliceBonus;
        public float aiLobBonus;
        public float aiDropShotBonus;

        public static SurfaceProfile Hard => new SurfaceProfile
        {
            name = "하드", restitution = 0.73f, friction = 0.55f, speedRetention = 1f, traction = 1f, braking = 1f,
            courtColor = new Color(0.13f, 0.32f, 0.58f), surroundColor = new Color(0.16f, 0.36f, 0.27f),
        };

        // 높이 튀고 바운드 뒤 많이 느려진다. 멈출 때 미끄러진다.
        public static SurfaceProfile Clay => new SurfaceProfile
        {
            name = "클레이", restitution = 0.82f, friction = 0.9f, speedRetention = 0.9f, traction = 0.9f, braking = 0.45f,
            leavesMarks = true,
            aiNetRushBonus = -0.1f, aiServeVolleyBonus = -0.05f, aiLobBonus = 0.04f, aiDropShotBonus = 0.04f, // 베이스라인 랠리
            courtColor = new Color(0.72f, 0.36f, 0.22f), surroundColor = new Color(0.64f, 0.32f, 0.2f),
        };

        // 낮게 깔리고 바운드 뒤에도 빠르다. 전체적으로 미끄럽다.
        public static SurfaceProfile Grass => new SurfaceProfile
        {
            name = "잔디", restitution = 0.62f, friction = 0.3f, speedRetention = 1f, traction = 0.75f, braking = 0.6f,
            aiNetRushBonus = 0.15f, aiServeVolleyBonus = 0.2f, aiSliceBonus = 0.1f, aiLobBonus = -0.03f, aiDropShotBonus = -0.02f, // 서브 앤 발리
            courtColor = new Color(0.3f, 0.52f, 0.22f), surroundColor = new Color(0.22f, 0.43f, 0.18f),
        };
    }
}
