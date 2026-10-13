using System;
using UnityEngine;

namespace FirstPersonTennis
{
    [Serializable]
    public struct SurfaceProfile
    {
        public string name;

        [Tooltip("수직 반발 계수. 클수록 높이 튄다.")]
        [Range(0.5f, 0.95f)] public float restitution;

        [Tooltip("바운드 마찰 계수. 클수록 바운드 후 느려지고 스핀 영향이 커진다.")]
        [Range(0f, 1.2f)] public float friction;

        public static SurfaceProfile Hard => new SurfaceProfile { name = "Hard", restitution = 0.73f, friction = 0.55f };
    }
}
