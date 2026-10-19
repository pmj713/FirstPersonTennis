using System;
using System.Collections;
using UnityEngine;

namespace FirstPersonTennis
{
    public enum GameMode { Match, Practice, Watch, Tutorial }
    public enum MatchPhase { Menu, Serve, Rally, PointOver, MatchOver }
    public enum PointReason { Out, Net, DoubleBounce, Ace, DoubleFault }

    // 경기 진행과 판정: 서브(폴트·렛·더블 폴트), 랠리(아웃·네트·투바운드), 점수, 서버 교대.
    // 연습 모드는 1단계처럼 AI가 공을 주고 랠리만 한다. 관전 모드는 가까운 쪽도 AI가 맡아 AI끼리 경기한다.
    // 튜토리얼은 자리 잡기와 공 주기만 하고 판정은 TutorialDirector가 과제별로 한다 (AI는 받아치지 않음).
    public class MatchReferee : MonoBehaviour
    {
        [SerializeField] TennisConfig config;
        [SerializeField] TennisBall ball;
        [SerializeField] PlayerMovement player;
        [SerializeField] ShotGestureInput playerInput;
        [SerializeField] PlayerServe playerServe;
        [SerializeField] AutoSwing playerSwing;
        [SerializeField] AiOpponent ai;
        [SerializeField] AutoSwing aiSwing;
        [Tooltip("관전 모드에서 플레이어 대신 가까운 쪽을 맡는 AI (평소에는 꺼 둔다)")]
        [SerializeField] AiOpponent nearAi;
        [SerializeField] AutoSwing nearAiSwing;
        [Tooltip("켜면 시작 메뉴부터, 끄면 기본 설정으로 바로 경기를 시작한다")]
        [SerializeField] bool startInMenu = true;

        // 공이 이 시간(공 물리 기준) 넘게 판정 없이 굴러다니면 멈춘 곳으로 판정한다
        const float StuckSeconds = 10f;

        public TennisConfig Config => config;
        public TennisBall Ball => ball;
        public AiOpponent Ai => ai;
        public PlayerMovement Player => player;
        public PlayerServe PlayerServe => playerServe;
        public AiOpponent NearAi => nearAi;
        public bool Watching => Mode == GameMode.Watch;
        public GameMode Mode { get; private set; }
        public MatchPhase Phase { get; private set; } = MatchPhase.Menu;
        public TennisScore Score { get; private set; }
        public int ServeNumber { get; private set; } = 1;
        public bool Paused { get; private set; }
        public int PlayerSide => player.Side;
        public int AiSide => -player.Side;
        bool Judging => Mode != GameMode.Tutorial;
        static float ServeOffset => Court.SinglesHalfWidth * 0.15f;

        // 연습 모드 기록
        public int PracticePlayerPoints { get; private set; }
        public int PracticeAiPoints { get; private set; }
        public int RallyReturns { get; private set; }
        public int BestReturns { get; private set; }

        public event Action<int, PointReason> PointEnded; // 이긴 쪽, 이유
        public event Action<string> Called;               // 폴트, 렛 같은 심판 콜
        public event Action<string> ScoreCalled;          // 포인트 뒤 점수 콜
        public event Action MatchFinished;

        readonly int[] pointsWon = new int[2];
        readonly int[] aces = new int[2];
        readonly int[] doubleFaults = new int[2];
        readonly System.Random rng = new System.Random();
        bool crossedNet;
        long flightStartStep;
        float resumeTimeScale = 1f;

        public int PointsWon(int side) => pointsWon[I(side)];
        public int Aces(int side) => aces[I(side)];
        public int DoubleFaults(int side) => doubleFaults[I(side)];
        public string NameOf(int side) => Watching ? (side == PlayerSide ? "AI 1" : "AI 2") : side == PlayerSide ? "나" : "상대";

        // 프리셋 선수: 같은 선수를 양쪽이 동시에 골라도 된다
        public CharacterPreset PlayerCharacter { get; private set; }
        public CharacterPreset AiCharacter => ai.Character;
        public CharacterPreset CharacterOf(int side) =>
            side != PlayerSide ? AiCharacter : Watching ? nearAi.Character : PlayerCharacter;

        // 관전 모드에서는 가까운 쪽 AI가 '내 선수'를 쓴다
        public void SetCharacters(int playerIndex, int aiIndex)
        {
            playerCharacterIndex = playerIndex;
            aiCharacterIndex = aiIndex;
            PlayerCharacter = config.Character(playerIndex);
            PlayerStats stats = config.StatsOf(PlayerCharacter);
            playerSwing.Stats = stats;
            player.Stats = stats;
            ai.SetCharacter(config.Character(aiIndex));
            if (nearAi != null) nearAi.SetCharacter(PlayerCharacter);
        }

        int playerCharacterIndex, aiCharacterIndex;

        void OnEnable()
        {
            ball.Launched += OnLaunched;
            ball.Bounced += OnBounced;
            ball.CrossedNet += OnCrossedNet;
            playerServe.Whiffed += OnServeWhiffed;
        }

        void OnDisable()
        {
            ball.Launched -= OnLaunched;
            ball.Bounced -= OnBounced;
            ball.CrossedNet -= OnCrossedNet;
            playerServe.Whiffed -= OnServeWhiffed;
            if (Paused) Time.timeScale = resumeTimeScale;
        }

        void Start()
        {
            config.SelectCourt(config.defaultCourt);
            SetCharacters(config.defaultPlayerCharacter, config.defaultAiCharacter);
            if (startInMenu) ShowMenu();
            else StartMatch(config.defaultRules, config.defaultDifficulty);
        }

        void Update()
        {
            if (!Judging || Phase != MatchPhase.Rally || !ball.IsLive || ball.LastHitterSide == 0) return;
            JudgeRolling();

            // 그래도 판정 없이 오래 남은 공 (안전장치)
            if (Phase != MatchPhase.Rally) return;
            if ((ball.StepIndex - flightStartStep) * BallPhysics.Step < StuckSeconds) return;
            int hitter = ball.LastHitterSide;
            if (ball.IsServe) Fault(false);
            else if (!crossedNet || Court.SideOf(ball.State.Position.z) == hitter) EndPoint(-hitter, PointReason.Net);
            else EndPoint(hitter, PointReason.DoubleBounce);
        }

        // ── 진행 ──────────────────────────────────────────────

        public void ShowMenu()
        {
            StopAllCoroutines();
            ResetPlayers();
            SetMode(GameMode.Match);
            Phase = MatchPhase.Menu;
            Score = null;
            float baseline = Court.HalfLength + 0.8f;
            player.ResetTo(new Vector3(0f, 0f, baseline * PlayerSide));
            ai.ResetTo(new Vector3(0f, 0f, baseline * AiSide));
            ball.Hold(ai.FeedContact);
            SetPaused(false);
            UpdateInput();
        }

        // firstServer: 0 = 코인 토스, 아니면 그 side가 먼저 서브
        public void StartMatch(MatchRules rules, AiDifficulty difficulty, int firstServer = 0) =>
            BeginMatch(GameMode.Match, rules, difficulty, firstServer);

        // AI끼리 경기 (관전, 밸런스 측정). 양쪽 모두 같은 난이도.
        public void StartWatch(MatchRules rules, AiDifficulty difficulty, int firstServer = 0)
        {
            if (nearAi == null)
            {
                Debug.LogWarning("[MatchReferee] 관전용 AI(nearAi)가 없어 일반 경기로 시작합니다. 씬을 다시 만들어 주세요.");
                BeginMatch(GameMode.Match, rules, difficulty, firstServer);
                return;
            }
            BeginMatch(GameMode.Watch, rules, difficulty, firstServer);
        }

        void BeginMatch(GameMode mode, MatchRules rules, AiDifficulty difficulty, int firstServer)
        {
            StopAllCoroutines();
            ResetPlayers();
            SetMode(mode);
            ai.SetDifficulty(difficulty);
            if (Watching) nearAi.SetDifficulty(difficulty);
            SetCharacters(playerCharacterIndex, aiCharacterIndex); // 켜진 관전 AI에도 다시 적용
            if (firstServer == 0) firstServer = rng.NextDouble() < 0.5 ? PlayerSide : AiSide;
            Score = new TennisScore(rules, firstServer);
            ServeNumber = 1;
            Array.Clear(pointsWon, 0, 2);
            Array.Clear(aces, 0, 2);
            Array.Clear(doubleFaults, 0, 2);
            SetPaused(false);
            Phase = MatchPhase.PointOver;
            UpdateInput();
            Called?.Invoke(Watching ? $"{NameOf(firstServer)} 서브로 시작" :
                firstServer == PlayerSide ? "내 서브로 시작" : "상대 서브로 시작");
            StartCoroutine(Serve(0.8f));
        }

        public void StartPractice(AiDifficulty difficulty)
        {
            StopAllCoroutines();
            ResetPlayers();
            SetMode(GameMode.Practice);
            ai.SetDifficulty(difficulty);
            Score = null;
            PracticePlayerPoints = PracticeAiPoints = RallyReturns = BestReturns = 0;
            SetPaused(false);
            Phase = MatchPhase.PointOver;
            UpdateInput();
            StartCoroutine(PracticeFeed(0.5f));
        }

        public void SetPaused(bool paused)
        {
            if (paused == Paused) return;
            if (paused)
            {
                resumeTimeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
                Time.timeScale = 0f;
            }
            else
            {
                Time.timeScale = resumeTimeScale;
            }
            Paused = paused;
            UpdateInput();
        }

        // 모드에 맞춰 관전 AI와 튜토리얼 AI(공만 주는 상대)를 켜고 끈다
        void SetMode(GameMode mode)
        {
            Mode = mode;
            SetWatching(mode == GameMode.Watch);
            ai.FeedOnly = mode == GameMode.Tutorial;
        }

        void UpdateInput()
        {
            bool enabled = !Paused && !Watching && Phase != MatchPhase.Menu && Phase != MatchPhase.MatchOver;
            player.InputEnabled = enabled;
            playerInput.InputEnabled = enabled;
        }

        // 관전: 가까운 쪽 AI를 켜고, 플레이어는 조작·스윙을 끈 채 가까운 베이스라인 뒤 관중 자리에 둔다 (카메라가 따라감)
        void SetWatching(bool on)
        {
            if (nearAi == null) on = false;
            if (nearAi != null && nearAi.gameObject.activeSelf != on) nearAi.gameObject.SetActive(on);
            player.enabled = !on;
            playerInput.enabled = !on;
            playerSwing.enabled = !on;
            ai.SetOpponent(on ? nearAi.transform : player.transform);
            if (on) player.transform.position = new Vector3(0f, 0f, (Court.HalfLength + 6f) * PlayerSide);
            UpdateInput();
        }

        // 가까운 쪽 선수 (평소 플레이어, 관전이면 AI)
        void PlaceNear(Vector3 position)
        {
            if (Watching) nearAi.ResetTo(position);
            else player.ResetTo(position);
        }

        void ResetPlayers()
        {
            playerServe.EndTurn();
            ai.CancelServe();
            playerSwing.CancelSwing();
            aiSwing.CancelSwing();
            if (nearAi != null && nearAi.isActiveAndEnabled) nearAi.CancelServe();
            if (nearAiSwing != null) nearAiSwing.CancelSwing();
        }

        IEnumerator Serve(float delay)
        {
            yield return new WaitForSeconds(delay);
            SetupServe();
        }

        // 서버는 베이스라인 뒤, 리시버는 대각선에 선다. 같은 포인트의 두 번째 서브도 여기서.
        void SetupServe()
        {
            ResetPlayers();
            int server = Score.Server;
            bool deuce = Score.ServeFromDeuce;
            crossedNet = false;
            Phase = MatchPhase.Serve;

            if (server == PlayerSide)
            {
                PlaceNear(Court.ServePosition(server, deuce, ServeOffset));
                ai.ResetTo(Court.ReceivePosition(-server, deuce));
                if (Watching) nearAi.BeginServe(deuce, ServeNumber == 2);
                else playerServe.BeginTurn(deuce, ServeNumber == 2);
            }
            else
            {
                ai.ResetTo(Court.ServePosition(server, deuce, ServeOffset));
                PlaceNear(Court.ReceivePosition(-server, deuce));
                ai.BeginServe(deuce, ServeNumber == 2);
            }
        }

        IEnumerator PracticeFeed(float delay)
        {
            yield return new WaitForSeconds(delay);
            ResetPlayers();
            float baseline = Court.HalfLength + 0.8f;
            player.ResetTo(new Vector3(0f, 0f, baseline * PlayerSide));
            ai.ResetTo(new Vector3(0f, 0f, baseline * AiSide));
            ball.Hold(ai.FeedContact);
            RallyReturns = 0;

            yield return new WaitForSeconds(config.feedDelay);
            crossedNet = false;
            Phase = MatchPhase.Rally;
            ai.Feed();
        }

        // ── 튜토리얼 ──────────────────────────────────────────

        public void StartTutorial()
        {
            StopAllCoroutines();
            SetMode(GameMode.Tutorial);
            Score = null;
            SetPaused(false);
            float baseline = Court.HalfLength + 0.8f;
            SetupDrill(new Vector3(0f, 0f, baseline * PlayerSide), new Vector3(0f, 0f, baseline * AiSide));
        }

        // 연습 한 번 준비: 두 선수를 세우고 공을 AI 손에 둔다
        public void SetupDrill(Vector3 playerPosition, Vector3 aiPosition)
        {
            ResetPlayers();
            player.ResetTo(playerPosition);
            ai.ResetTo(aiPosition);
            ball.Hold(ai.FeedContact);
            Phase = MatchPhase.PointOver;
            UpdateInput();
        }

        // AI가 정해진 구질로 공을 준다
        public void FeedDrill(ShotIntent intent)
        {
            crossedNet = false;
            Phase = MatchPhase.Rally;
            ai.Feed(intent);
        }

        // 내 서브 연습 한 번: 서브 자리에 서고 AI는 대각선 리시브 자리에 선다
        public void SetupServeDrill(bool deuce)
        {
            ResetPlayers();
            crossedNet = false;
            ServeNumber = 1;
            player.ResetTo(Court.ServePosition(PlayerSide, deuce, ServeOffset));
            ai.ResetTo(Court.ReceivePosition(AiSide, deuce));
            Phase = MatchPhase.Serve;
            playerServe.BeginTurn(deuce, false);
            UpdateInput();
        }

        // ── 판정 ──────────────────────────────────────────────

        void OnLaunched(int hitter)
        {
            if (hitter == 0) return; // 토스
            crossedNet = false;
            flightStartStep = ball.StepIndex;
            if (Phase == MatchPhase.Serve && ball.IsServe) Phase = MatchPhase.Rally;
            if (Phase == MatchPhase.Rally && hitter == PlayerSide)
            {
                RallyReturns++;
                BestReturns = Mathf.Max(BestReturns, RallyReturns);
            }
        }

        void OnCrossedNet() => crossedNet = true;

        void OnServeWhiffed()
        {
            if (Judging && Phase == MatchPhase.Serve) Fault(true);
        }

        void OnBounced(Vector3 point) => Judge(point, ball.BouncesSinceHit);

        // bounce: 친 뒤 몇 번째 바운드인지 (1 = 첫 바운드)
        void Judge(Vector3 point, int bounce)
        {
            if (!Judging || Phase != MatchPhase.Rally) return;
            int hitter = ball.LastHitterSide;
            if (hitter == 0) return;
            int receiver = -hitter;

            if (ball.IsServe)
            {
                if (bounce == 1)
                {
                    bool good = crossedNet && Court.IsInServiceBox(point, receiver, ball.ServeToDeuce);
                    if (!good) Fault(false);
                    else if (ball.TouchedNet) Let();
                    // 들어갔으면 리시버가 받아야 한다
                }
                else if (bounce == 2)
                {
                    EndPoint(hitter, PointReason.Ace);
                }
                return;
            }

            if (bounce == 1)
            {
                if (!crossedNet || Court.SideOf(point.z) == hitter) EndPoint(receiver, PointReason.Net);
                else if (!Court.IsInSingles(point, receiver)) EndPoint(receiver, PointReason.Out);
            }
            else if (bounce == 2)
            {
                EndPoint(hitter, PointReason.DoubleBounce);
            }
        }

        // 아주 낮게 닿은 공은 튀지 않고 구른다 (바운드 이벤트 없음). 구르기 시작하면 남은 바운드를 한꺼번에 본다.
        void JudgeRolling()
        {
            BallState s = ball.State;
            if (s.Position.y > BallPhysics.Radius + 0.002f || Mathf.Abs(s.Velocity.y) > 0.01f) return;
            int next = ball.BouncesSinceHit + 1;
            Judge(s.Position, Mathf.Min(next, 2));
            if (Phase == MatchPhase.Rally && next < 2) Judge(s.Position, 2);
        }

        void Fault(bool whiff)
        {
            int server = Score != null ? Score.Server : ball.LastHitterSide;
            if (ServeNumber == 1)
            {
                ServeNumber = 2;
                Phase = MatchPhase.PointOver;
                Called?.Invoke(whiff ? "헛스윙 — 폴트" : "폴트");
                StartCoroutine(Serve(config.faultDelay));
                return;
            }
            doubleFaults[I(server)]++;
            EndPoint(-server, PointReason.DoubleFault);
        }

        void Let()
        {
            Phase = MatchPhase.PointOver;
            Called?.Invoke(ServeNumber == 1 ? "렛 — 첫 서브 다시" : "렛 — 두 번째 서브 다시");
            StartCoroutine(Serve(config.faultDelay));
        }

        void EndPoint(int winner, PointReason reason)
        {
            Phase = MatchPhase.PointOver;
            PointEnded?.Invoke(winner, reason);

            if (Mode == GameMode.Practice)
            {
                if (winner == PlayerSide) PracticePlayerPoints++;
                else PracticeAiPoints++;
                StartCoroutine(PracticeFeed(config.pointResetDelay));
                return;
            }

            pointsWon[I(winner)]++;
            if (reason == PointReason.Ace) aces[I(winner)]++;
            PointOutcome outcome = Score.AddPoint(winner);
            ServeNumber = 1;
            ScoreCalled?.Invoke(DescribeScore(outcome));

            if (outcome.MatchWon)
            {
                Phase = MatchPhase.MatchOver;
                UpdateInput();
                MatchFinished?.Invoke();
                return;
            }
            StartCoroutine(Serve(config.pointResetDelay + (outcome.GameWon ? 0.8f : 0f)));
        }

        // 포인트 뒤 심판 점수 콜. 서버 점수를 먼저 부른다.
        public string DescribeScore(PointOutcome outcome)
        {
            string change = outcome.EndChange ? " · 코트 체인지" : "";
            int winner = outcome.GameWinner;
            if (outcome.MatchWon) return $"게임 · 세트 · 매치 — {NameOf(winner)}";
            if (outcome.SetWon)
            {
                Vector2Int set = Score.CompletedSets[Score.CompletedSets.Count - 1];
                return $"세트 — {NameOf(winner)} ({set.x}-{set.y}){change}";
            }
            if (outcome.TiebreakStarted) return $"게임 — {NameOf(winner)} · 타이브레이크{change}";
            if (outcome.GameWon) return $"게임 — {NameOf(winner)}{change}";

            int server = Score.Server;
            if (Score.InTiebreak) return $"{Score.Points(server)}-{Score.Points(-server)}{change}";
            if (Score.IsDeuce) return "듀스";
            string a = Score.PointLabel(server), b = Score.PointLabel(-server);
            if (a == "AD") return $"어드밴티지 {NameOf(server)}";
            if (b == "AD") return $"어드밴티지 {NameOf(-server)}";
            if (a == b) return a == "0" ? "0-0" : $"{a} 올";
            return $"{a}-{b}";
        }

        static int I(int side) => side < 0 ? 0 : 1;
    }
}
