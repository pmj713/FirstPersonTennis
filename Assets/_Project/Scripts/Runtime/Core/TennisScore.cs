using System;
using System.Collections.Generic;
using UnityEngine;

namespace FirstPersonTennis
{
    public enum MatchFormat { TiebreakOnly, ShortSet, OneSet, BestOfThree }

    [Serializable]
    public struct MatchRules
    {
        public MatchFormat format;
        [Tooltip("듀스에서 다음 1점으로 게임을 결정")]
        public bool noAd;
        [Tooltip("복식 (팀마다 두 명, 복식 코트). 점수 규칙은 단식과 같다")]
        public bool doubles;

        public int GamesPerSet => format == MatchFormat.ShortSet ? 4 : 6;
        public int SetsToWin => format == MatchFormat.BestOfThree ? 2 : 1;
    }

    public struct PointOutcome
    {
        public bool GameWon;
        public bool SetWon;
        public bool MatchWon;
        public bool TiebreakStarted;
        public bool EndChange;
        public int GameWinner;
    }

    // 실제 테니스 점수 규칙 (ITF). side: -1 = 근거리, +1 = 원거리.
    public sealed class TennisScore
    {
        public const int TiebreakTarget = 7;

        readonly MatchRules rules;
        readonly int[] points = new int[2];
        readonly int[] games = new int[2];
        readonly int[] sets = new int[2];
        readonly List<Vector2Int> completedSets = new List<Vector2Int>(); // x = 근거리 게임, y = 원거리 게임
        int tiebreakFirstServer;
        int tiebreakPointsPlayed;

        public TennisScore(MatchRules rules, int firstServer)
        {
            this.rules = rules;
            Server = firstServer;
            if (rules.format == MatchFormat.TiebreakOnly) StartTiebreak();
        }

        public MatchRules Rules => rules;
        public int Server { get; private set; }
        public int Receiver => -Server;
        public bool InTiebreak { get; private set; }
        public bool MatchOver { get; private set; }
        public int Winner { get; private set; }
        public IReadOnlyList<Vector2Int> CompletedSets => completedSets;
        public Vector2Int LastTiebreakPoints { get; private set; }

        public int Points(int side) => points[I(side)];
        public int Games(int side) => games[I(side)];
        public int Sets(int side) => sets[I(side)];

        // 이번 포인트를 듀스 코트(서버 기준 오른쪽)에서 서브하는지. 게임·타이브레이크 모두 포인트 합이 짝수면 듀스 코트.
        public bool ServeFromDeuce => (points[0] + points[1]) % 2 == 0;

        public bool IsDeuce => !InTiebreak && points[0] >= 3 && points[0] == points[1];

        public string PointLabel(int side)
        {
            int me = Points(side), them = Points(-side);
            if (InTiebreak) return me.ToString();
            if (me >= 3 && them >= 3) return me > them ? "AD" : "40";
            return me switch { 0 => "0", 1 => "15", 2 => "30", _ => "40" };
        }

        public PointOutcome AddPoint(int winner)
        {
            var outcome = new PointOutcome();
            if (MatchOver) return outcome;
            points[I(winner)]++;
            int won = Points(winner), lost = Points(-winner);

            if (InTiebreak)
            {
                tiebreakPointsPlayed++;
                if (tiebreakPointsPlayed % 2 == 1) Server = -Server; // 첫 포인트 뒤, 이후 두 포인트마다 교대
                if (tiebreakPointsPlayed % 6 == 0) outcome.EndChange = true;
                if (won >= TiebreakTarget && won - lost >= 2)
                {
                    LastTiebreakPoints = new Vector2Int(points[0], points[1]);
                    WinGame(winner, true, ref outcome);
                }
                return outcome;
            }

            bool gameWon = won >= 4 && (won - lost >= 2 || (rules.noAd && lost >= 3));
            if (gameWon) WinGame(winner, false, ref outcome);
            return outcome;
        }

        void WinGame(int winner, bool tiebreak, ref PointOutcome outcome)
        {
            outcome.GameWon = true;
            outcome.GameWinner = winner;
            points[0] = points[1] = 0;
            games[I(winner)]++;

            int gamesWon = Games(winner), gamesLost = Games(-winner);
            int gamesInSet = games[0] + games[1];
            int perSet = rules.GamesPerSet;

            if (tiebreak)
            {
                InTiebreak = false;
                Server = -tiebreakFirstServer; // 다음 세트는 타이브레이크에서 먼저 리시브한 쪽이 서브
            }
            else
            {
                Server = -Server;
            }

            bool setWon = tiebreak || (gamesWon >= perSet && gamesWon - gamesLost >= 2);
            if (gamesInSet % 2 == 1) outcome.EndChange = true;

            if (!setWon)
            {
                if (gamesWon == perSet && gamesLost == perSet)
                {
                    StartTiebreak();
                    outcome.TiebreakStarted = true;
                }
                return;
            }

            outcome.SetWon = true;
            completedSets.Add(new Vector2Int(games[0], games[1]));
            sets[I(winner)]++;
            games[0] = games[1] = 0;

            if (sets[I(winner)] >= rules.SetsToWin || rules.format == MatchFormat.TiebreakOnly)
            {
                MatchOver = true;
                Winner = winner;
                outcome.MatchWon = true;
            }
        }

        void StartTiebreak()
        {
            InTiebreak = true;
            tiebreakFirstServer = Server;
            tiebreakPointsPlayed = 0;
        }

        static int I(int side) => side < 0 ? 0 : 1;
    }
}
