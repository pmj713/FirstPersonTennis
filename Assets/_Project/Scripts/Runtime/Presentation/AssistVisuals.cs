using UnityEngine;

namespace FirstPersonTennis
{
    // 1인칭 거리감 보조: 공 그림자(항상), 낙하 지점 마커와 범위 안 알림(공 테두리 + 효과음).
    // 마커와 알림은 쉬움 난이도와 튜토리얼에서만 켠다 (MatchReferee.AssistDisplays). 튜토리얼 이동 과제의 목표 원도 여기서 그린다.
    public class AssistVisuals : MonoBehaviour
    {
        [SerializeField] MatchReferee referee;
        [SerializeField] TennisBall ball;
        [SerializeField] AutoSwing playerSwing;
        [SerializeField] Transform ballShadow;
        [SerializeField] Transform landingMarker;
        [SerializeField] GameObject ballHalo;
        [SerializeField] AudioFeedback audioFeedback;
        [SerializeField] TutorialDirector tutorial;
        [SerializeField] Transform tutorialTarget;

        bool wasInZone;

        void LateUpdate()
        {
            Vector3 b = ball.transform.position;
            ballShadow.position = new Vector3(b.x, 0.015f, b.z);
            float size = Mathf.Lerp(0.09f, 0.16f, Mathf.Clamp01(b.y / 6f)) * (BallPhysics.Radius / BallPhysics.RealRadius);
            ballShadow.localScale = new Vector3(size, 1f, size);

            bool assisted = referee.AssistDisplays;
            bool showLanding = assisted && playerSwing.HasLanding &&
                               Court.SideOf(playerSwing.LandingPoint.z) == playerSwing.Side;
            landingMarker.gameObject.SetActive(showLanding);
            if (showLanding)
                landingMarker.position = new Vector3(playerSwing.LandingPoint.x, 0.02f, playerSwing.LandingPoint.z);

            tutorialTarget.gameObject.SetActive(tutorial.TargetActive);
            if (tutorial.TargetActive) tutorialTarget.position = tutorial.TargetPosition + Vector3.up * 0.022f;

            bool inZone = assisted && playerSwing.HasContact && playerSwing.ContactInHitZone;
            ballHalo.SetActive(inZone);
            if (inZone && !wasInZone && audioFeedback != null) audioFeedback.PlayAlert();
            wasInZone = inZone;
        }
    }
}
