using System;
using System.Collections;
using UnityEngine;

namespace FirstPersonTennis
{
    public enum PointReason { Out, Net, Missed }

    // 1단계용 랠리 판정: 아웃, 네트, 투바운드만 본다. 서브와 정식 점수는 2단계.
    public class RallyReferee : MonoBehaviour
    {
        [SerializeField] TennisConfig config;
        [SerializeField] TennisBall ball;
        [SerializeField] PlayerMovement player;
        [SerializeField] DummyOpponent dummy;

        public int PlayerPoints { get; private set; }
        public int DummyPoints { get; private set; }
        public int PlayerReturns { get; private set; }
        public int BestReturns { get; private set; }
        public bool PointInProgress { get; private set; }

        public event Action<int, PointReason> PointEnded; // 이긴 쪽 side, 이유

        bool crossedNet;

        void OnEnable()
        {
            ball.Launched += OnLaunched;
            ball.Bounced += OnBounced;
            ball.CrossedNet += OnCrossedNet;
        }

        void OnDisable()
        {
            ball.Launched -= OnLaunched;
            ball.Bounced -= OnBounced;
            ball.CrossedNet -= OnCrossedNet;
        }

        void Start() => StartCoroutine(NextPoint(0.5f));

        void OnLaunched(int hitter)
        {
            crossedNet = false;
            if (PointInProgress && hitter == player.Side)
            {
                PlayerReturns++;
                BestReturns = Mathf.Max(BestReturns, PlayerReturns);
            }
        }

        void OnCrossedNet() => crossedNet = true;

        void OnBounced(Vector3 point)
        {
            if (!PointInProgress) return;
            int hitter = ball.LastHitterSide;
            if (hitter == 0) return;
            int receiver = -hitter;

            if (ball.BouncesSinceHit == 1)
            {
                if (!crossedNet || Court.SideOf(point.z) == hitter) End(receiver, PointReason.Net);
                else if (!Court.IsInSingles(point, receiver)) End(receiver, PointReason.Out);
            }
            else if (ball.BouncesSinceHit == 2)
            {
                End(hitter, PointReason.Missed);
            }
        }

        void End(int winner, PointReason reason)
        {
            PointInProgress = false;
            if (winner == player.Side) PlayerPoints++;
            else DummyPoints++;
            PointEnded?.Invoke(winner, reason);
            StartCoroutine(NextPoint(config.pointResetDelay));
        }

        IEnumerator NextPoint(float delay)
        {
            yield return new WaitForSeconds(delay);
            float baseline = Court.HalfLength + 0.8f;
            player.ResetTo(new Vector3(0f, 0f, baseline * player.Side));
            dummy.ResetTo(new Vector3(0f, 0f, -baseline * player.Side));
            ball.Hold(dummy.FeedContact);
            PlayerReturns = 0;

            yield return new WaitForSeconds(config.feedDelay);
            PointInProgress = true;
            dummy.Feed();
        }
    }
}
