using UnityEngine;

namespace FirstPersonTennis
{
    // 관중 소리 (실제 녹음, Audio/Crowd, CC0). 실제 테니스처럼 랠리 중에는 조용하고, 포인트가 끝나면 박수가 난다.
    // 박수 크기: 에이스·위너·긴 랠리일수록 크게, 범실이면 작게, 더블 폴트면 없음. 게임·세트를 따면 크게, 세트·경기가 끝나면 환호까지.
    // 포인트 사이에는 웅성임이 커졌다가 서브를 준비하면 잦아들고, 박수도 서브 전에 사그라든다.
    // 튜토리얼은 웅성임만. 크기는 설정 화면의 관중 소리 크기를 따른다
    public class CrowdAudio : MonoBehaviour
    {
        [SerializeField] MatchReferee referee;
        [SerializeField] UmpireVoice umpire; // 심판이 말하는 동안 박수·웅성임을 조금 줄인다
        [Header("녹음 (Audio/Crowd, SOURCES.md 참고)")]
        [SerializeField] AudioClip[] ambience;       // 경기장 공기 (늘 낮게 겹쳐 깐다)
        [SerializeField] AudioClip[] chatter;        // 포인트 사이 웅성임
        [SerializeField] AudioClip[] applauseSmall;
        [SerializeField] AudioClip[] applauseMedium;
        [SerializeField] AudioClip[] applauseBig;
        [SerializeField] AudioClip[] cheers;         // 세트·경기를 땄을 때
        [SerializeField, Range(0f, 1f)] float volume = 0.8f; // 설정이 100%일 때

        const int ClapVoices = 3;
        const float ServeFade = 1.2f; // 서브 준비가 되면 박수가 이만큼(초)에 걸쳐 사그라든다

        AudioSource[] beds;
        AudioSource talk;
        readonly AudioSource[] claps = new AudioSource[ClapVoices];
        readonly float[] clapVolume = new float[ClapVoices];
        int nextClap;
        float talkLevel, clapFade = 1f, duck = 1f;
        readonly System.Random rng = new System.Random();

        int shots;               // 이번 포인트에서 친 횟수 (서브 포함)
        float pendingSize = -1f; // 곧 칠 박수의 크기 (0 작게 … 1 크게, 넘으면 환호), 없으면 음수
        bool pendingCheer;
        float pendingAt;

        // 테스트·확인용: 마지막으로 친 박수 (0 없음, 1 작게, 2 보통, 3 크게)와 환호 여부
        public int LastApplause { get; private set; }
        public bool LastCheered { get; private set; }
        public int ApplauseCount { get; private set; }
        public float TalkLevel => talkLevel;

        void Awake()
        {
            beds = new AudioSource[ambience != null ? ambience.Length : 0];
            for (int i = 0; i < beds.Length; i++)
            {
                beds[i] = Voice($"Ambience{i}", ambience[i], true);
                if (ambience[i] != null) beds[i].time = (float)rng.NextDouble() * ambience[i].length; // 고리 길이가 달라 겹침이 계속 바뀐다
            }
            talk = Voice("Chatter", chatter != null && chatter.Length > 0 ? chatter[rng.Next(chatter.Length)] : null, true);
            for (int i = 0; i < ClapVoices; i++) claps[i] = Voice($"Applause{i}", null, false);
        }

        AudioSource Voice(string name, AudioClip clip, bool loop)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            AudioSource s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.spatialBlend = 0f; // 경기장을 둘러싼 소리
            s.loop = loop;
            s.clip = clip;
            s.volume = 0f;
            if (clip != null && loop) s.Play();
            return s;
        }

        void OnEnable()
        {
            referee.Ball.Launched += OnLaunched;
            referee.Called += OnCalled;
            referee.PointEnded += OnPointEnded;
            referee.Scored += OnScored;
        }

        void OnDisable()
        {
            referee.Ball.Launched -= OnLaunched;
            referee.Called -= OnCalled;
            referee.PointEnded -= OnPointEnded;
            referee.Scored -= OnScored;
        }

        void OnLaunched(int side)
        {
            if (side != 0) shots++; // 토스는 빼고
        }

        void OnCalled(string call) => shots = 0; // 폴트·렛: 다음 서브부터 다시 센다

        void OnPointEnded(int winner, PointReason reason)
        {
            int rally = shots;
            shots = 0;
            if (referee.Mode == GameMode.Tutorial) return;
            pendingSize = ApplauseSize(reason, rally) * (referee.Mode == GameMode.Practice ? 0.6f : 1f);
            pendingCheer = false;
            pendingAt = Time.time + 0.25f + (float)rng.NextDouble() * 0.3f; // 사람들이 반응하는 데 걸리는 틈 (경기 시간: 멈추면 기다린다)
        }

        // 같은 프레임에 PointEnded 뒤에 온다 (연습 모드는 오지 않음)
        void OnScored(PointOutcome outcome)
        {
            if (pendingSize < 0f) return;
            if (outcome.MatchWon) { pendingSize = 1f; pendingCheer = true; }
            else if (outcome.SetWon) { pendingSize = Mathf.Max(pendingSize + 0.4f, 0.8f); pendingCheer = true; }
            else if (outcome.GameWon) pendingSize += 0.15f;
        }

        // 포인트가 끝난 이유와 랠리 길이(친 횟수)로 정하는 박수 크기
        public static float ApplauseSize(PointReason reason, int shots)
        {
            float size = reason switch
            {
                PointReason.DoubleFault => 0f,
                PointReason.Ace => 0.6f,
                PointReason.DoubleBounce => 0.45f, // 받지 못한 공 = 위너
                _ => 0.25f,                        // 아웃·네트 = 범실
            };
            if (reason != PointReason.DoubleFault) size += Mathf.Clamp01((shots - 3) / 12f) * 0.5f; // 길게 주고받을수록
            return size;
        }

        // 0 없음, 1 작게, 2 보통, 3 크게
        public static int ApplauseLevel(float size) => size < 0.12f ? 0 : size < 0.4f ? 1 : size < 0.72f ? 2 : 3;

        void Update()
        {
            float dt = Time.deltaTime; // 경기 시간: 일시정지 중에는 웅성임·박수가 그대로 멈춰 있다
            MatchPhase phase = referee.Phase;
            float master = volume * UserSettings.CrowdVolume * (referee.Paused ? 0.35f : 1f);

            if (pendingSize >= 0f && Time.time >= pendingAt)
            {
                Applaud(pendingSize, pendingCheer);
                pendingSize = -1f;
            }

            // 서브를 준비하면 "조용히" — 웅성임과 박수가 잦아든다
            bool quiet = phase == MatchPhase.Serve || phase == MatchPhase.Rally;
            float talkTarget = phase switch
            {
                MatchPhase.Rally => 0.08f,
                MatchPhase.Serve => 0.15f,
                MatchPhase.PointOver => 0.7f,
                MatchPhase.MatchOver => 0.8f,
                _ => 0.45f,
            };
            talkLevel = Mathf.MoveTowards(talkLevel, talkTarget, dt * (talkTarget < talkLevel ? 0.6f : 0.3f));
            if (quiet && clapFade > 0f)
            {
                clapFade = Mathf.MoveTowards(clapFade, 0f, dt / ServeFade);
                if (clapFade <= 0f) foreach (AudioSource s in claps) s.Stop(); // 폴트 뒤에 다시 커지지 않게 끝낸다
            }

            foreach (AudioSource bed in beds) bed.volume = master * (phase == MatchPhase.Rally ? 0.3f : 0.4f);
            duck = Mathf.MoveTowards(duck, umpire != null && umpire.Speaking ? 0.6f : 1f, dt * 3f);
            talk.volume = master * talkLevel * 0.5f * duck;
            for (int i = 0; i < ClapVoices; i++) claps[i].volume = master * clapVolume[i] * clapFade * duck;
        }

        void Applaud(float size, bool cheer)
        {
            int level = ApplauseLevel(size);
            LastApplause = level;
            LastCheered = cheer;
            if (level == 0 && !cheer) return;
            ApplauseCount++;
            AudioClip[] pool = level == 1 ? applauseSmall : level == 2 ? applauseMedium : applauseBig;
            float loud = level == 1 ? Mathf.Lerp(0.45f, 0.65f, Mathf.InverseLerp(0.12f, 0.4f, size))
                : level == 2 ? Mathf.Lerp(0.65f, 0.85f, Mathf.InverseLerp(0.4f, 0.72f, size))
                : Mathf.Lerp(0.85f, 1f, Mathf.InverseLerp(0.72f, 1.1f, size));
            Play(pool, loud);
            if (cheer) Play(cheers, 0.7f);
            clapFade = 1f;
        }

        void Play(AudioClip[] pool, float loud)
        {
            AudioClip clip = pool != null && pool.Length > 0 ? pool[rng.Next(pool.Length)] : null;
            if (clip == null) return; // 녹음을 지웠는데 씬을 아직 다시 만들지 않았을 때
            int i = nextClap;
            nextClap = (nextClap + 1) % ClapVoices;
            AudioSource s = claps[i];
            s.clip = clip;
            s.pitch = 1f + ((float)rng.NextDouble() - 0.5f) * 0.04f;
            clapVolume[i] = loud;
            s.Play();
        }
    }
}
