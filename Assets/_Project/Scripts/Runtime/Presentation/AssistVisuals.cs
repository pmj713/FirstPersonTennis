using UnityEngine;
using UnityEngine.InputSystem;

namespace FirstPersonTennis
{
    // 1인칭 거리감 보조: 공 그림자, 낙하 지점 마커, 타격 범위 링, 범위 안 알림(공 테두리 + 효과음).
    // 튜토리얼 동안은 보조 많음(설정과 상관없이 모두 켬)이고, 이동 과제의 목표 원도 여기서 그린다.
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
        [SerializeField] TutorialDirector tutorial;
        [SerializeField] Transform tutorialTarget;

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

            bool full = tutorial.Active;
            bool showLanding = (config.showLandingMarker || full) && playerSwing.HasLanding &&
                               Court.SideOf(playerSwing.LandingPoint.z) == playerSwing.Side;
            landingMarker.gameObject.SetActive(showLanding);
            if (showLanding)
                landingMarker.position = new Vector3(playerSwing.LandingPoint.x, 0.02f, playerSwing.LandingPoint.z);

            bool showRing = ringVisible || full;
            hitRangeRing.gameObject.SetActive(showRing);
            if (showRing)
            {
                hitRangeRing.position = player.position + Vector3.up * 0.018f;
                hitRangeRing.localScale = Vector3.one * config.hitReach;
            }

            tutorialTarget.gameObject.SetActive(tutorial.TargetActive);
            if (tutorial.TargetActive) tutorialTarget.position = tutorial.TargetPosition + Vector3.up * 0.022f;

            bool inZone = (config.hitZoneAlert || full) && playerSwing.HasContact && playerSwing.ContactInHitZone;
            ballHalo.SetActive(inZone);
            if (inZone && !wasInZone && audioFeedback != null) audioFeedback.PlayAlert();
            wasInZone = inZone;
        }
    }
}
