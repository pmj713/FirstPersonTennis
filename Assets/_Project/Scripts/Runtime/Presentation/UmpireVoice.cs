using System.Collections.Generic;
using UnityEngine;

namespace FirstPersonTennis
{
    // 심판 콜 (Audio/Umpire, 음성 합성으로 만든 영어 콜. 원본: ArtSource/Audio/Umpire).
    // 판정은 바로: 아웃 "Out!", 폴트 "Fault", 렛 "Let. First service". 점수는 포인트가 끝나고 조금 뒤 경기장 확성기로:
    // 서버 점수 먼저 "Fifteen, love", "Deuce", "Advantage, server", "Game", "Game and set", "Game, set, and match", 타이브레이크 "Three, two".
    // 선수 이름 대신 서버·리시버로 부른다. 크기는 설정 화면의 경기장 소리 크기를 따른다
    [RequireComponent(typeof(AudioSource))]
    public class UmpireVoice : MonoBehaviour
    {
        [SerializeField] MatchReferee referee;
        [SerializeField] AudioClip[] calls; // 파일 이름이 콜 이름 (point_1_0, deuce, tb_3_2, num_8 …)
        [SerializeField, Range(0f, 1f)] float volume = 0.85f;

        const float ScoreDelay = 0.8f; // 포인트가 끝나고 점수를 부르기까지 (박수가 시작된 뒤)
        const float Gap = 0.08f;       // 이어 부르는 콜 사이

        readonly Dictionary<string, AudioClip> byName = new Dictionary<string, AudioClip>();
        readonly Queue<(string name, float at)> queue = new Queue<(string, float)>();
        AudioSource source;
        float busyUntil;

        public bool Speaking => source != null && source.isPlaying;
        public string LastCall { get; private set; } = "";
        public int CallCount { get; private set; }

        void Awake()
        {
            source = GetComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f; // 경기장 확성기
            if (calls != null)
                foreach (AudioClip clip in calls)
                    if (clip != null) byName[clip.name] = clip;
        }

        void OnEnable()
        {
            referee.Called += OnCalled;
            referee.PointEnded += OnPointEnded;
            referee.Scored += OnScored;
        }

        void OnDisable()
        {
            referee.Called -= OnCalled;
            referee.PointEnded -= OnPointEnded;
            referee.Scored -= OnScored;
        }

        // 심판 콜 문구(MatchReferee)에 맞춰: 폴트·헛스윙 폴트 → Fault, 렛 → 몇 번째 서브인지
        void OnCalled(string call)
        {
            if (call.EndsWith("폴트")) Say("fault", 0.05f);
            else if (call.StartsWith("렛")) Say(call.Contains("첫") ? "let_first" : "let_second", 0.15f);
        }

        void OnPointEnded(int winner, PointReason reason)
        {
            if (reason == PointReason.Out) Say("out", 0f);
            else if (reason == PointReason.DoubleFault) Say("fault", 0.05f);
        }

        void OnScored(PointOutcome outcome)
        {
            float at = ScoreDelay;
            foreach (string name in CallFor(referee.Score, outcome))
            {
                Say(name, at);
                at = 0f; // 둘째 말부터는 앞의 말이 끝나는 대로
            }
        }

        // 점수가 오른 뒤 부를 콜 (서버 점수 먼저). 타이브레이크 8점 이상은 숫자 둘을 이어 부른다
        public static List<string> CallFor(TennisScore score, PointOutcome outcome)
        {
            var result = new List<string>();
            if (outcome.MatchWon) result.Add("game_set_match");
            else if (outcome.SetWon) result.Add("game_set");
            else if (outcome.TiebreakStarted) result.Add("game_tiebreak");
            else if (outcome.GameWon) result.Add("game");
            else
            {
                int server = score.Server, s = score.Points(server), r = score.Points(-server);
                if (score.InTiebreak)
                {
                    if (s <= 7 && r <= 7) result.Add($"tb_{s}_{r}");
                    else
                    {
                        result.Add($"num_{Mathf.Min(s, 15)}");
                        result.Add(s == r ? "word_all" : $"num_{Mathf.Min(r, 15)}");
                    }
                }
                else if (s >= 3 && r >= 3) result.Add(s == r ? "deuce" : s > r ? "advantage_server" : "advantage_receiver");
                else result.Add($"point_{s}_{r}");
            }
            return result;
        }

        void Say(string name, float delay)
        {
            if (!byName.ContainsKey(name)) return;
            queue.Enqueue((name, Time.time + delay));
        }

        void Update()
        {
            source.volume = volume * UserSettings.CrowdVolume;
            if (referee.Phase == MatchPhase.Menu) queue.Clear(); // 메뉴로 나가면 남은 콜은 버린다
            if (queue.Count == 0 || Time.time < busyUntil || Time.time < queue.Peek().at) return;
            (string name, _) = queue.Dequeue();
            AudioClip clip = byName[name];
            source.PlayOneShot(clip);
            LastCall = name;
            CallCount++;
            busyUntil = Time.time + clip.length - 0.6f + Gap; // 끝의 잔향(0.7초)은 다음 말과 겹쳐도 된다
        }
    }
}
