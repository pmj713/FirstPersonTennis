using System;
using System.Collections;
using UnityEngine;

namespace FirstPersonTennis
{
    // 과제 종류: 이동 목표, 베이스라인에서 받기, 로브 받기(스매시 연습), 네트 앞 발리, 내 서브
    public enum TutorialDrill { Move, Baseline, Lob, Net, Serve }

    // 튜토리얼: 이동 → 선입력 → 구질 5종 → 발리 → 서브를 과제 하나씩 연습한다.
    // 심판은 자리와 공만 준비하고 (AI는 정해진 공을 주기만 함), 과제 성공은 여기서 본다:
    // 친 공이 상대 코트에 들어갔고 과제가 원하는 샷인지. 자동 기본 스트로크로 친 공은 세지 않는다.
    // 보조 표시(낙하 지점·범위 링·범위 알림)는 튜토리얼 동안 모두 켠다 (AssistVisuals가 Active를 본다).
    public class TutorialDirector : MonoBehaviour
    {
        [SerializeField] TennisConfig config;
        [SerializeField] MatchReferee referee;
        [SerializeField] TennisBall ball;
        [SerializeField] PlayerMovement player;
        [SerializeField] ShotGestureInput input;
        [SerializeField] AutoSwing swing;
        [SerializeField] PlayerServe serve;

        const float ReachRadius = 0.7f;   // 이동 목표 원 안으로 들어왔다고 보는 거리 (m)
        const float SprintNeeded = 0.4f;  // 달리기 과제: 가는 동안 이만큼 달려야 한다 (초)
        const float DrillTimeout = 10f;   // 공을 준 뒤 판정 없이 이만큼 지나면 다시 (초)
        const float ResultPause = 1.4f;   // 결과를 보여 주고 다음 공까지 (초)

        enum Outcome { None, In, Out, Net, Let, Passed, Whiff, Redo }

        sealed class Task
        {
            public string Text;
            public string Hint;
            public TutorialDrill Drill;
            public int Goal = 1;
            public Vector3[] Targets;           // 이동 과제의 목표 (차례로)
            public bool Sprint;                 // 이동 과제: 달려서 가야 한다
            public bool LetGo;                  // 치지 않고 우클릭으로 보내는 과제
            public Func<ShotRecord, bool> Wanted;
            public string Wrong;                // 넣었지만 원하는 샷이 아닐 때
        }

        sealed class Lesson
        {
            public string Title;
            public Task[] Tasks;
        }

        public bool Active => referee.Mode == GameMode.Tutorial;
        public bool Completed { get; private set; }
        public int LessonNumber => lessonIndex + 1;
        public int LessonCount => lessons?.Length ?? 0;
        public string LessonTitle => CurrentLesson?.Title ?? "";
        public int TaskNumber => taskIndex + 1;
        public int TaskCount => CurrentLesson?.Tasks.Length ?? 0;
        public string TaskText => CurrentTask?.Text ?? "";
        public string TaskHint => CurrentTask?.Hint ?? "";
        public TutorialDrill Drill => CurrentTask?.Drill ?? TutorialDrill.Move;
        public bool SprintRequired => CurrentTask?.Sprint ?? false;
        public int Progress { get; private set; }
        public int Goal => CurrentTask?.Goal ?? 0;
        public bool TargetActive { get; private set; }
        public Vector3 TargetPosition { get; private set; }

        public event Action<string> Said; // 결과·안내 한마디 (HUD 배너)

        Lesson[] lessons;
        int lessonIndex, taskIndex;
        Coroutine routine;
        bool skip;
        readonly System.Random rng = new System.Random(3);

        // 공 하나를 지켜보는 동안의 상태
        bool watching;
        Outcome outcome;
        bool hit, crossedNet, whiffed, intentSeen, skipSeen;
        ShotRecord shot;

        Lesson CurrentLesson => lessons != null && lessonIndex < lessons.Length ? lessons[lessonIndex] : null;
        Task CurrentTask => CurrentLesson != null && taskIndex < CurrentLesson.Tasks.Length ? CurrentLesson.Tasks[taskIndex] : null;

        void OnEnable()
        {
            ball.Launched += OnLaunched;
            ball.Bounced += OnBounced;
            ball.CrossedNet += OnCrossedNet;
            serve.Whiffed += OnServeWhiffed;
            swing.SwingResolved += OnSwingResolved;
        }

        void OnDisable()
        {
            ball.Launched -= OnLaunched;
            ball.Bounced -= OnBounced;
            ball.CrossedNet -= OnCrossedNet;
            serve.Whiffed -= OnServeWhiffed;
            swing.SwingResolved -= OnSwingResolved;
        }

        void Update()
        {
            if (routine != null && !Active) Stop(); // 메뉴로 나가거나 다른 모드를 시작함
        }

        public void Begin()
        {
            Stop();
            lessons = BuildLessons();
            lessonIndex = taskIndex = 0;
            Completed = false;
            referee.StartTutorial();
            routine = StartCoroutine(Run());
        }

        // 지금 과제를 건너뛴다 (일시정지 메뉴)
        public void SkipTask() => skip = true;

        void Stop()
        {
            if (routine != null) StopCoroutine(routine);
            routine = null;
            watching = false;
            TargetActive = false;
            Completed = false;
        }

        IEnumerator Run()
        {
            for (lessonIndex = 0; lessonIndex < lessons.Length; lessonIndex++)
            {
                Say($"{LessonNumber}단계 · {LessonTitle}");
                for (taskIndex = 0; taskIndex < CurrentLesson.Tasks.Length; taskIndex++)
                {
                    Task task = CurrentTask;
                    Progress = 0;
                    skip = false;
                    yield return task.Drill switch
                    {
                        TutorialDrill.Move => RunMove(task),
                        TutorialDrill.Serve => RunServes(task),
                        _ => RunShots(task),
                    };
                    if (!skip) yield return Wait(0.8f);
                }
            }
            lessonIndex = lessons.Length - 1;
            taskIndex = CurrentLesson.Tasks.Length - 1;
            Completed = true;
            referee.SetupDrill(BaselineSpot(player.Side), BaselineSpot(-player.Side));
            Say("튜토리얼 완료!");
            routine = null;
        }

        // ── 과제 진행 ─────────────────────────────────────────

        // 목표 원에 차례로 들어간다. 달리기 과제는 가는 동안 달려야 센다 (아니면 다음 원으로 다시)
        IEnumerator RunMove(Task task)
        {
            int next = 0;
            float sprinted = 0f;
            while (Progress < task.Goal && !skip)
            {
                TargetActive = true;
                TargetPosition = task.Targets[next % task.Targets.Length];
                if (player.IsSprinting) sprinted += Time.deltaTime;
                Vector3 to = TargetPosition - player.transform.position;
                to.y = 0f;
                if (to.magnitude <= ReachRadius)
                {
                    next++;
                    if (task.Sprint && sprinted < SprintNeeded)
                    {
                        Say("Shift를 누른 채 달려서 가 보세요");
                    }
                    else
                    {
                        Progress++;
                        Say(Progress < task.Goal ? $"좋아요 ({Progress}/{task.Goal})" : "성공!");
                    }
                    sprinted = 0f;
                }
                yield return null;
            }
            TargetActive = false;
        }

        IEnumerator RunShots(Task task)
        {
            while (Progress < task.Goal && !skip)
            {
                Vector3 spot = task.Drill == TutorialDrill.Net ? NetSpot(player.Side) : BaselineSpot(player.Side);
                referee.SetupDrill(spot, BaselineSpot(-player.Side));
                yield return Wait(config.feedDelay);
                if (skip) break;

                Watch();
                referee.FeedDrill(FeedFor(task.Drill));
                float start = Time.time;
                while (outcome == Outcome.None && !skip)
                {
                    if (input.HasIntent) intentSeen = true;
                    if (input.SkipRequested) skipSeen = true;
                    if (Time.time - start > DrillTimeout) outcome = Outcome.Redo;
                    yield return null;
                }
                if (skip) break;
                Judge(task);
                yield return Wait(ResultPause);
            }
            watching = false;
        }

        // 듀스·애드 코트를 번갈아 서브한다. 토스 전에는 기다리고, 친 뒤에는 시간 제한
        IEnumerator RunServes(Task task)
        {
            bool deuce = true;
            while (Progress < task.Goal && !skip)
            {
                referee.SetupServeDrill(deuce);
                Watch();
                float launched = -1f;
                while (outcome == Outcome.None && !skip)
                {
                    if (launched < 0f && hit) launched = Time.time;
                    if (launched >= 0f && Time.time - launched > DrillTimeout) outcome = Outcome.Redo;
                    yield return null;
                }
                if (skip) break;
                Judge(task);
                deuce = !deuce;
                yield return Wait(ResultPause);
            }
            watching = false;
        }

        IEnumerator Wait(float seconds)
        {
            for (float t = 0f; t < seconds && !skip; t += Time.deltaTime) yield return null;
        }

        // ── 판정 ──────────────────────────────────────────────

        void Watch()
        {
            watching = true;
            outcome = Outcome.None;
            hit = crossedNet = whiffed = intentSeen = skipSeen = false;
        }

        void OnLaunched(int hitter)
        {
            if (!watching || hitter == 0) return;
            crossedNet = false;
            if (hitter == player.Side)
            {
                hit = true;
                shot = swing.LastShot;
            }
        }

        void OnCrossedNet()
        {
            if (watching) crossedNet = true;
        }

        void OnSwingResolved(bool connected)
        {
            if (watching && !connected) whiffed = true;
        }

        void OnServeWhiffed()
        {
            if (watching && outcome == Outcome.None) outcome = Outcome.Whiff;
        }

        // 내가 친 공은 첫 바운드로, 상대가 준 공은 내 코트 두 번째 바운드(지나감)로 끝난다
        void OnBounced(Vector3 point)
        {
            if (!watching || outcome != Outcome.None) return;
            int hitter = ball.LastHitterSide;
            if (hitter == player.Side && ball.BouncesSinceHit == 1)
            {
                int receiver = -hitter;
                bool inside = ball.IsServe ? Court.IsInServiceBox(point, receiver, ball.ServeToDeuce) : Court.IsInSingles(point, receiver);
                if (!crossedNet || Court.SideOf(point.z) == hitter) outcome = Outcome.Net;
                else if (!inside) outcome = Outcome.Out;
                else if (ball.IsServe && ball.TouchedNet) outcome = Outcome.Let;
                else outcome = Outcome.In;
            }
            else if (hitter == -player.Side)
            {
                if (Court.SideOf(point.z) != player.Side) outcome = Outcome.Redo; // 준 공이 네트에 걸림
                else if (ball.BouncesSinceHit >= 2) outcome = Outcome.Passed;
            }
        }

        void Judge(Task task)
        {
            watching = false;
            switch (outcome)
            {
                case Outcome.In:
                    if (shot.Auto) Say("자동 스트로크는 세지 않아요 — 직접 입력해 보세요");
                    else if (task.Wanted != null && !task.Wanted(shot)) Say(task.Wrong);
                    else Succeed(task);
                    break;
                case Outcome.Out:
                    Say("아웃 — 다시 해 봐요");
                    break;
                case Outcome.Net:
                    Say("네트 — 다시 해 봐요");
                    break;
                case Outcome.Let:
                    Say("렛 (네트에 맞고 들어감) — 다시");
                    break;
                case Outcome.Whiff:
                    Say("헛스윙 — 공이 내려올 때 초록 칸에서 놓으세요");
                    break;
                case Outcome.Passed:
                    if (task.LetGo && skipSeen) Succeed(task);
                    else if (task.LetGo) Say("좌클릭으로 예약한 뒤 우클릭으로 취소해 보세요");
                    else if (whiffed) Say("헛스윙 — 공이 몸 옆으로 오게 서 보세요");
                    else if (!intentSeen) Say("입력이 없어 치지 않았어요 — 상대가 친 뒤 좌클릭");
                    else Say("놓침 — 노란 원 뒤로 가서 기다려 보세요");
                    break;
                default:
                    Say("다시 한 번");
                    break;
            }
        }

        void Succeed(Task task)
        {
            Progress++;
            Say(Progress < task.Goal ? $"좋아요 ({Progress}/{task.Goal})" : "성공!");
        }

        void Say(string message) => Said?.Invoke(message);

        // ── 자리와 공 ─────────────────────────────────────────

        static Vector3 BaselineSpot(int side) => new Vector3(0f, 0f, (Court.HalfLength + 0.8f) * side);
        static Vector3 NetSpot(int side) => new Vector3(0f, 0f, Court.ServiceLine * 0.5f * side);

        // 연습용 공: 스트로크는 조금 느린 탑스핀, 스매시는 내 코트 깊숙이 떨어지는 로브, 발리는 몸 쪽으로 오는 낮은 공
        ShotIntent FeedFor(TutorialDrill drill)
        {
            float pace = config.feedSpeedScale > 0f ? config.feedSpeedScale : 1f;
            return drill switch
            {
                TutorialDrill.Lob => new ShotIntent { Type = ShotType.Lob, Aim = Range(-0.2f, 0.2f), Depth = 0.3f },
                TutorialDrill.Net => new ShotIntent { Type = ShotType.Topspin, Aim = Range(-0.1f, 0.1f), Depth = 1f, SpeedScale = 0.8f * pace },
                _ => new ShotIntent { Type = ShotType.Topspin, Aim = Range(-0.3f, 0.3f), Depth = 1f, SpeedScale = 0.85f * pace },
            };
        }

        float Range(float min, float max) => min + (max - min) * (float)rng.NextDouble();

        // ── 과제 목록 ─────────────────────────────────────────

        Lesson[] BuildLessons()
        {
            int side = player.Side;
            float right = Court.Facing(side);
            float hl = Court.HalfLength, hw = Court.SinglesHalfWidth, sl = Court.ServiceLine;
            // x: 내 기준 오른쪽(+), depth: 네트에서 내 쪽으로 떨어진 거리
            Vector3 Spot(float x, float depth) => new Vector3(x * right, 0f, depth * side);
            float smashHeight = config.smashMinHeight;

            return new[]
            {
                new Lesson
                {
                    Title = "이동",
                    Tasks = new[]
                    {
                        new Task
                        {
                            Text = "W A S D로 초록 원까지 걸어가기", Drill = TutorialDrill.Move, Goal = 3,
                            Hint = "W는 카메라가 어디를 보든 항상 네트 쪽 · 원에 들어가면 다음 원이 나와요",
                            Targets = new[] { Spot(0.4f * hw, hl - 4f), Spot(-0.4f * hw, hl - 4f), Spot(0f, hl) },
                        },
                        new Task
                        {
                            Text = "Shift를 누른 채 달려서 먼 원까지 가기", Drill = TutorialDrill.Move, Sprint = true,
                            Hint = "달리면 빠르지만 스테미너(왼쪽 아래)를 써요 · 바닥나면 회복될 때까지 걷기만 할 수 있어요",
                            Targets = new[] { Spot(0.9f * hw, sl), Spot(-0.9f * hw, sl) },
                        },
                    },
                },
                new Lesson
                {
                    Title = "선입력",
                    Tasks = new[]
                    {
                        new Task
                        {
                            Text = "상대가 친 뒤 좌클릭해서 받아넘기기", Drill = TutorialDrill.Baseline, Goal = 2,
                            Hint = "좌클릭은 스윙이 아니라 예약 · 클릭만 하면 탑스핀, 스윙은 공이 오면 자동\n" +
                                   "노란 원 = 공이 떨어질 곳, 그 뒤에서 기다리기 · 칠 수 있는 범위에 오면 공 테두리가 빛나요",
                        },
                        new Task
                        {
                            Text = "좌클릭으로 예약한 뒤 우클릭으로 취소하고 공 보내기", Drill = TutorialDrill.Baseline, LetGo = true,
                            Hint = "아웃될 공은 치지 말고 보내세요 · 스윙이 시작되기 전까지는 다시 입력하면 마지막 입력으로 바뀌어요",
                            Wanted = _ => false, Wrong = "이번 공은 치지 말고 우클릭으로 보내 보세요",
                        },
                    },
                },
                new Lesson
                {
                    Title = "구질 5종",
                    Tasks = new[]
                    {
                        new Task
                        {
                            Text = "클릭만 → 탑스핀 (가운데로 깊게)", Drill = TutorialDrill.Baseline,
                            Hint = "드래그하지 않고 누른 자리에서 바로 놓기",
                            Wanted = s => s.Intent.Type == ShotType.Topspin && Mathf.Abs(s.Intent.Aim) < 0.2f,
                            Wrong = "드래그 없이 클릭만 해 보세요",
                        },
                        new Task
                        {
                            Text = "← 드래그 → 왼쪽으로", Drill = TutorialDrill.Baseline,
                            Hint = "옆으로 길게 끌수록 각이 커져요 · 각을 크게 줄수록 오차도 커져요",
                            Wanted = s => s.Intent.Aim <= -0.3f, Wrong = "왼쪽으로 더 길게 끌어 보세요",
                        },
                        new Task
                        {
                            Text = "→ 드래그 → 오른쪽으로", Drill = TutorialDrill.Baseline,
                            Hint = "방향은 화면이 아니라 상대 코트 기준이에요",
                            Wanted = s => s.Intent.Aim >= 0.3f, Wrong = "오른쪽으로 더 길게 끌어 보세요",
                        },
                        new Task
                        {
                            Text = "↓ 길게 드래그 → 슬라이스 (낮게 깔림)", Drill = TutorialDrill.Baseline,
                            Hint = "아래로 길게 끌수록 깊게 떨어져요 · 회전이 반대라 낮게 튀어요",
                            Wanted = s => s.Intent.Type == ShotType.Slice && s.Intent.Depth >= 0.5f,
                            Wrong = "아래로 더 길게 끌어 보세요",
                        },
                        new Task
                        {
                            Text = "↓ 짧게 드래그 → 드롭샷 (네트 앞에 떨어짐)", Drill = TutorialDrill.Baseline,
                            Hint = "아래로 조금만 끌기 · 상대가 베이스라인 뒤에 있을 때 좋아요",
                            Wanted = s => s.Intent.Type == ShotType.Slice && s.Intent.Depth < 0.35f,
                            Wrong = "아래로 조금만 끌어 보세요 (길면 슬라이스)",
                        },
                        new Task
                        {
                            Text = "↑ 드래그 → 로브 (높이 띄움)", Drill = TutorialDrill.Baseline,
                            Hint = "위로 길게 끌수록 깊게 · 짧게 올리면 실수 방지로 탑스핀이 나가요",
                            Wanted = s => s.Intent.Type == ShotType.Lob, Wrong = "위로 더 길게 끌어 보세요",
                        },
                        new Task
                        {
                            Text = "높이 뜬 공 → ↑ 올렸다가 ↓ 내리기 = 스매시", Drill = TutorialDrill.Lob,
                            Hint = "노란 원 쪽으로 가서 공 아래에 서기 · 머리 위에서 쳐야 스매시",
                            Wanted = s => s.Intent.Type == ShotType.Smash && s.Contact.y >= smashHeight,
                            Wrong = "머리 위에서 스매시로 쳐 보세요 (↑ 올렸다가 ↓)",
                        },
                    },
                },
                new Lesson
                {
                    Title = "발리",
                    Tasks = new[]
                    {
                        new Task
                        {
                            Text = "네트 앞에서 바운드 전에 받아넘기기", Drill = TutorialDrill.Net, Goal = 2,
                            Hint = "서비스라인 안쪽에서는 바운드 전에 닿는 공을 발리로 쳐요 · 클릭만 하면 기본 발리",
                            Wanted = s => s.Volley && s.Intent.Type != ShotType.Smash,
                            Wrong = "바운드 전에 쳐야 발리예요 — 네트 앞에 서 있어 보세요",
                        },
                        new Task
                        {
                            Text = "↓ 드래그 → 드롭 발리", Drill = TutorialDrill.Net,
                            Hint = "드래그 길이와 상관없이 네트 바로 너머로 짧게 떨어져요 · ↑는 깊은 발리",
                            Wanted = s => s.Volley && s.Intent.Type == ShotType.Slice,
                            Wrong = "바운드 전에 ↓ 드래그로 쳐 보세요",
                        },
                    },
                },
                new Lesson
                {
                    Title = "서브",
                    Tasks = new[]
                    {
                        new Task
                        {
                            Text = "서브 넣기", Drill = TutorialDrill.Serve, Goal = 2,
                            Hint = "A/D로 위치 · 누르면 토스 · 오른쪽 막대의 초록 칸에서 놓기\n대각선 건너편 서비스 박스에 넣어요",
                            Wanted = s => s.Serve,
                        },
                        new Task
                        {
                            Text = "↓ 슬라이스 서브나 ↑ 킥 서브 넣기", Drill = TutorialDrill.Serve,
                            Hint = "토스한 뒤 놓기 전에 드래그 · 슬라이스는 옆으로 휘고, 킥은 높이 튀어 안전해요 · ←→ 코너",
                            Wanted = s => s.Serve && s.ServeType != ServeType.Flat,
                            Wrong = "놓기 전에 위나 아래로 드래그해 보세요",
                        },
                    },
                },
            };
        }
    }
}
