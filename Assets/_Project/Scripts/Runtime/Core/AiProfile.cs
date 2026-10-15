using System;
using UnityEngine;

namespace FirstPersonTennis
{
    public enum AiDifficulty { Easy, Normal, Hard }

    // AI 난이도별 성향. 오차·반응·이동은 숫자로, 샷 선택은 확률로 둔다.
    [Serializable]
    public struct AiProfile
    {
        [Tooltip("상대가 친 뒤 움직이기 시작할 때까지 (초)")]
        public float reactionDelay;
        public float speed;
        public float acceleration;
        [Tooltip("타구 오차 배율. 플레이어 = 1")]
        [Range(0f, 1.5f)] public float errorScale;
        [Tooltip("상대가 없는 쪽으로 노리는 정도. 0 = 무작위, 1 = 항상 빈 곳")]
        [Range(0f, 1f)] public float smartAim;
        [Range(0f, 1f)] public float sliceChance;
        [Range(0f, 1f)] public float lobChance;
        [Tooltip("상대가 베이스라인에 있을 때 짧은 슬라이스(드롭샷)를 칠 확률")]
        [Range(0f, 1f)] public float dropShotChance;
        [Tooltip("상대가 네트에 붙었을 때 로브로 넘길 확률")]
        [Range(0f, 1f)] public float lobVsNetChance;
        [Tooltip("공 위치를 잘못 읽어 엉뚱한 곳으로 가는 확률")]
        [Range(0f, 1f)] public float misjudgeChance;
        [Tooltip("서브를 놓는 높이의 흔들림 (m)")]
        public float serveTimingError;
        [Tooltip("첫 서브를 플랫으로 넣을 확률 (나머지는 슬라이스와 킥이 반반)")]
        [Range(0f, 1f)] public float firstServeFlatChance;

        [Tooltip("코트 안쪽 짧은 공을 친 뒤 네트로 들어갈 확률")]
        [Range(0f, 1f)] public float netRushChance;
        [Tooltip("서브 후 바로 네트로 들어갈 확률 (서브 앤 발리)")]
        [Range(0f, 1f)] public float serveVolleyChance;
        [Tooltip("친 뒤 돌아갈 자리를 고르는 수준. 0 = 가운데로, 1 = 내가 보낸 공 쪽으로 치우쳐 상대 각도의 가운데를 막음")]
        [Range(0f, 1f)] public float anticipation;
        [Tooltip("친 뒤 돌아가는 속도 (걷기 속도 배율). 낮으면 늦게 따라간다")]
        [Range(0.3f, 1.5f)] public float recoverySpeed;
    }
}
