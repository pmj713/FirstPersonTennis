using UnityEngine;
using UnityEngine.InputSystem;

namespace FirstPersonTennis
{
    // 1인칭 거리감 보조: 공 그림자, 낙하 지점 마커, 타격 범위 링, 범위 안 알림(공 테두리 + 효과음).
    public class AssistVisuals : MonoBehaviour
    {
        [SerializeField] TennisConfig config;
        [SerializeField] TennisBall ball;
        [SerializeField] AutoSwing playerSwing;
        [SerializeField] Transform player;
        [SerializeField] Transform ballShadow;
        [SerializeField] Transform landingMarker;
        [SerializeField] Transform hitRangeRing;
        [SerializeField] GameObject ballHalo;
        [SerializeField] AudioFeedback audioFeedback;

        bool ringVisible;
        bool wasInZone;

        void Start() => ringVisible = config.showHitRangeRing;

        void LateUpdate()
        {
            Keyboard kb = Keyboard.current;
            if (kb != null && kb.rKey.wasPressedThisFrame) ringVisible = !ringVisible;

            Vector3 b = ball.transform.position;
            ballShadow.position = new Vector3(b.x, 0.015f, b.z);
            float size = Mathf.Lerp(0.09f, 0.16f, Mathf.Clamp01(b.y / 6f)) * (BallPhysics.Radius / BallPhysics.RealRadius);
            ballShadow.localScale = new Vector3(size, 1f, size);

            bool showLanding = config.showLandingMarker && playerSwing.HasLanding &&
                               Court.SideOf(playerSwing.LandingPoint.z) == playerSwing.Side;
            landingMarker.gameObject.SetActive(showLanding);
            if (showLanding)
                landingMarker.position = new Vector3(playerSwing.LandingPoint.x, 0.02f, playerSwing.LandingPoint.z);

            hitRangeRing.gameObject.SetActive(ringVisible);
            if (ringVisible)
            {
                hitRangeRing.position = player.position + Vector3.up * 0.018f;
                hitRangeRing.localScale = Vector3.one * config.hitReach;
            }

            bool inZone = config.hitZoneAlert && playerSwing.HasContact && playerSwing.ContactInHitZone;
            ballHalo.SetActive(inZone);
            if (inZone && !wasInZone && audioFeedback != null) audioFeedback.PlayAlert();
            wasInZone = inZone;
        }
    }
}
