using System.Collections;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FirstPersonTennis.Tests
{
    // AI끼리 경기(관전 모드): 서브부터 경기 끝까지 돌아가는지, 난이도·코트 성향이 실제로 나오는지, 선수 밸런스.
    public class AiTests
    {
        MatchReferee referee;
        int nearVolleys, farVolleys, exhaustedFrames, frames, approaches;
        bool nearWasAtNet, farWasAtNet;
        float minStamina;

        IEnumerator LoadMatch(CourtType court)
        {
            SceneManager.LoadScene("Match");
            yield return null;
            yield return null; // 씬 오브젝트의 Start(메뉴 표시)가 끝난 뒤에 조작한다
            referee = Object.FindAnyObjectByType<MatchReferee>();
            Assert.IsNotNull(referee, "Match 씬에 MatchReferee가 없음");
            Assert.IsNotNull(referee.NearAi, "관전용 AI가 없음: 씬을 다시 만들어야 한다");
            referee.Config.SelectCourt(court);
            nearVolleys = farVolleys = exhaustedFrames = frames = approaches = 0;
            nearWasAtNet = farWasAtNet = false;
            minStamina = 1f;
            referee.Ball.Launched += hitter =>
            {
                if (hitter == 0 || referee.Ball.IsServe) return;
                if (Mathf.Abs(referee.Ball.State.Position.z) > Court.ServiceLine) return; // 서비스라인 안에서 친 공 = 발리
                if (hitter == referee.PlayerSide) nearVolleys++;
                else farVolleys++;
            };
        }

        [TearDown]
        public void Restore()
        {
            Time.captureDeltaTime = 0f;
            Time.timeScale = 1f;
            Court.SetSurface(CourtType.Hard, SurfaceProfile.Hard);
        }

        IEnumerator PlayUntilOver(float timeout)
        {
            bool finished = false;
            referee.MatchFinished += () => finished = true;
            Time.captureDeltaTime = 1f / 60f;
            float start = Time.time;
            while (!finished && Time.time - start < timeout)
            {
                frames++;
                if (referee.NearAi.AtNet && !nearWasAtNet) approaches++;
                if (referee.Ai.AtNet && !farWasAtNet) approaches++;
                nearWasAtNet = referee.NearAi.AtNet;
                farWasAtNet = referee.Ai.AtNet;
                foreach (AiOpponent ai in new[] { referee.NearAi, referee.Ai })
                {
                    minStamina = Mathf.Min(minStamina, ai.Stamina01);
                    if (ai.Exhausted) exhaustedFrames++;
                }
                yield return null;
            }
            Assert.IsTrue(finished, "경기가 끝나지 않음");
        }

        [UnityTest]
        public IEnumerator AiWatchMatchPlaysToTheEnd()
        {
            yield return LoadMatch(CourtType.Hard);
            referee.SetCharacters(0, 0);
            referee.StartWatch(new MatchRules { format = MatchFormat.TiebreakOnly }, AiDifficulty.Normal);
            Assert.IsTrue(referee.Watching);
            Assert.IsFalse(referee.Player.InputEnabled, "관전 중에는 플레이어 입력을 막는다");

            yield return PlayUntilOver(900f);

            Vector2Int tb = referee.Score.LastTiebreakPoints;
            Debug.Log($"[AiWatch] 타이브레이크 {tb} · 발리 {nearVolleys}:{farVolleys} · 최저 스테미너 {minStamina:0.00} · 지친 시간 {exhaustedFrames * 100f / Mathf.Max(1, frames * 2):0}%");
            Assert.GreaterOrEqual(Mathf.Max(tb.x, tb.y), TennisScore.TiebreakTarget);
            Assert.Less(minStamina, 1f, "AI도 뛰면 스테미너를 쓴다");

            referee.ShowMenu();
            Assert.IsFalse(referee.NearAi.gameObject.activeSelf, "메뉴로 돌아가면 관전용 AI를 끈다");
        }

        // 어려움 난이도 + 잔디: 서브 앤 발리와 네트 대시가 실제로 나온다
        [UnityTest]
        public IEnumerator HardAiRushesTheNetOnGrass()
        {
            yield return LoadMatch(CourtType.Grass);
            referee.SetCharacters(1, 1);
            referee.StartWatch(new MatchRules { format = MatchFormat.TiebreakOnly }, AiDifficulty.Hard);
            yield return PlayUntilOver(900f);

            Debug.Log($"[AiNet] 잔디·어려움 네트 대시 {approaches}번 · 발리 {nearVolleys}:{farVolleys} · 지친 시간 {exhaustedFrames * 100f / Mathf.Max(1, frames * 2):0}%");
            Assert.Greater(approaches, 0, "네트 대시가 한 번도 없음"); // 발리까지 가는지는 상대 패싱샷과 코트 너비에 달려 기록만 남긴다
        }

        // 선수 조합별 포인트 승률과, 그 승률로 계산한 세트 승률. 기획서 목표: 조합별 승률 45~55%.
        // 코트마다 12분쯤 걸려서 평소에는 돌리지 않는다:
        // unity test --mode PlayMode --filter CharacterBalanceReport (하드), CharacterBalanceReportOn (클레이·잔디)
        [UnityTest, Explicit("선수 밸런스 측정은 오래 걸려 따로 돌린다"), Timeout(4 * 60 * 60 * 1000)]
        public IEnumerator CharacterBalanceReport() => BalanceReport(CourtType.Hard);

        [UnityTest, Explicit("선수 밸런스 측정은 오래 걸려 따로 돌린다"), Timeout(4 * 60 * 60 * 1000)]
        public IEnumerator CharacterBalanceReportOnClay() => BalanceReport(CourtType.Clay);

        [UnityTest, Explicit("선수 밸런스 측정은 오래 걸려 따로 돌린다"), Timeout(4 * 60 * 60 * 1000)]
        public IEnumerator CharacterBalanceReportOnGrass() => BalanceReport(CourtType.Grass);

        // 하드 코트, 어려움 난이도 AI끼리
        [UnityTest, Explicit("선수 밸런스 측정은 오래 걸려 따로 돌린다"), Timeout(4 * 60 * 60 * 1000)]
        public IEnumerator CharacterBalanceReportHardAi() => BalanceReport(CourtType.Hard, difficulty: AiDifficulty.Hard);

        // 공격형이 들어간 3개 조합만, 하드 코트·어려움 난이도. 힘 효과를 바꿀 때
        [UnityTest, Explicit("선수 밸런스 측정은 오래 걸려 따로 돌린다"), Timeout(4 * 60 * 60 * 1000)]
        public IEnumerator AttackTypeBalanceHardAi() => BalanceReport(CourtType.Hard, "공격형", AiDifficulty.Hard);

        // 수비형이 들어간 3개 조합만 하드·클레이·잔디에서 이어서 잰다 (약 30분)
        [UnityTest, Explicit("선수 밸런스 측정은 오래 걸려 따로 돌린다"), Timeout(4 * 60 * 60 * 1000)]
        public IEnumerator DefensiveTypeBalanceAllCourts()
        {
            foreach (CourtType court in new[] { CourtType.Hard, CourtType.Clay, CourtType.Grass })
                yield return BalanceReport(court, "수비형");
        }

        // 공격형이 들어간 3개 조합만 하드·클레이·잔디에서 이어서 잰다 (약 22분). 힘 효과를 바꿀 때
        [UnityTest, Explicit("선수 밸런스 측정은 오래 걸려 따로 돌린다"), Timeout(4 * 60 * 60 * 1000)]
        public IEnumerator AttackTypeBalanceAllCourts()
        {
            foreach (CourtType court in new[] { CourtType.Hard, CourtType.Clay, CourtType.Grass })
                yield return BalanceReport(court, "공격형");
        }

        // 조합마다 자리를 바꿔 두 번 재고(각 PointsPerSide 포인트), 끝나는 대로 바로 로그를 남긴다 (중간에 멈춰도 결과가 남음).
        // 순서는 라운드 로빈이라 앞쪽 절반만 재도 모든 선수가 한 번씩 나온다. focus가 있으면 그 선수가 들어간 조합만 잰다.
        IEnumerator BalanceReport(CourtType court, string focus = null, AiDifficulty difficulty = AiDifficulty.Normal)
        {
            const int PointsPerSide = 160;
            yield return LoadMatch(court);
            CharacterPreset[] roster = referee.Config.characters;
            var summary = new StringBuilder($"[Balance {court} {difficulty}] 앞 선수가 딴 포인트 % (자리 바꿔 평균, 계산한 세트 승률 %)\n");
            Time.captureDeltaTime = 1f / 60f;

            foreach ((int a, int b) in RoundRobin(roster.Length))
            {
                if (focus != null && roster[a].name != focus && roster[b].name != focus) continue;
                float aNear = 0f, bNear = 0f;
                yield return MeasurePoints(a, b, PointsPerSide, difficulty, p => aNear = p);
                yield return MeasurePoints(b, a, PointsPerSide, difficulty, p => bNear = p);
                float aWins = (aNear + (1f - bNear)) * 0.5f;
                string line = $"{roster[a].name} vs {roster[b].name}: {roster[a].name} {aWins * 100f:0.0}% " +
                              $"(세트 {SetWinProbability(aWins) * 100f:0}%, 가까운 쪽 {aNear * 100f:0}% · 먼 쪽 {(1f - bNear) * 100f:0}%)";
                Debug.Log($"[BalancePair {court}] {line}");
                summary.AppendLine(line);
            }
            Debug.Log(summary.ToString());
        }

        // near 선수(가까운 쪽 AI)가 far 선수를 상대로 딴 포인트 비율
        IEnumerator MeasurePoints(int near, int far, int points, AiDifficulty difficulty, System.Action<float> done)
        {
            int nearWins = 0, farWins = 0;
            void Count(int winner, PointReason reason)
            {
                if (winner == referee.PlayerSide) nearWins++;
                else farWins++;
            }
            referee.PointEnded += Count;
            referee.SetCharacters(near, far);
            referee.StartWatch(new MatchRules { format = MatchFormat.BestOfThree }, difficulty);
            while (nearWins + farWins < points)
            {
                if (referee.Phase == MatchPhase.MatchOver)
                    referee.StartWatch(new MatchRules { format = MatchFormat.BestOfThree }, difficulty);
                yield return null;
            }
            referee.PointEnded -= Count;
            done(nearWins / (float)(nearWins + farWins));
        }

        // 원형 라운드 로빈: 라운드마다 모든 선수가 한 번씩 나온다
        static List<(int, int)> RoundRobin(int count)
        {
            var seats = new List<int>();
            for (int i = 0; i < count; i++) seats.Add(i);
            if (seats.Count % 2 == 1) seats.Add(-1); // 쉬는 자리
            var pairs = new List<(int, int)>();
            int n = seats.Count;
            for (int round = 0; round < n - 1; round++)
            {
                for (int k = 0; k < n / 2; k++)
                {
                    int x = seats[k], y = seats[n - 1 - k];
                    if (x >= 0 && y >= 0) pairs.Add((Mathf.Min(x, y), Mathf.Max(x, y)));
                }
                int last = seats[n - 1];
                seats.RemoveAt(n - 1);
                seats.Insert(1, last);
            }
            return pairs;
        }

        // 포인트마다 독립으로 p 확률로 딴다고 보고 6게임 세트(6-6 타이브레이크)를 이길 확률
        static float SetWinProbability(float p)
        {
            float q = 1f - p;
            float game = p * p * p * p * (1f + 4f * q + 10f * q * q) + 20f * Mathf.Pow(p * q, 3f) * (p * p / (1f - 2f * p * q));
            float tiebreak = RaceWinByTwo(p, 7);
            var memo = new Dictionary<(int, int), float>();
            float Set(int a, int b)
            {
                if (a >= 6 && a - b >= 2) return 1f;
                if (b >= 6 && b - a >= 2) return 0f;
                if (a == 6 && b == 6) return tiebreak;
                if (memo.TryGetValue((a, b), out float v)) return v;
                v = game * Set(a + 1, b) + (1f - game) * Set(a, b + 1);
                memo[(a, b)] = v;
                return v;
            }
            return Set(0, 0);
        }

        static float RaceWinByTwo(float p, int target)
        {
            var memo = new Dictionary<(int, int), float>();
            float deuce = p * p / (p * p + (1f - p) * (1f - p));
            float Race(int a, int b)
            {
                if (a >= target && a - b >= 2) return 1f;
                if (b >= target && b - a >= 2) return 0f;
                if (a >= target - 1 && a == b) return deuce;
                if (memo.TryGetValue((a, b), out float v)) return v;
                v = p * Race(a + 1, b) + (1f - p) * Race(a, b + 1);
                memo[(a, b)] = v;
                return v;
            }
            return Race(0, 0);
        }
    }
}
