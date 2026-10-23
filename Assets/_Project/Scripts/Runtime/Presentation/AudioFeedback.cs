using UnityEngine;

namespace FirstPersonTennis
{
    // 효과음. 라켓 타구음과 바운드는 실제 녹음(Audio, CC0)을 쓰고, 녹음이 없으면 코드로 만든 소리를 쓴다.
    // 타구음은 구질별로 고르고(RacketSound가 판정), 바운드는 코트 종류에 따라 높이·크기를 바꾼다.
    // 둘 다 난 자리에서 3D로 들린다: 내 쪽은 가깝게, 상대 쪽은 앞에서 작게. 알림·득점은 코드로 만든 짧은 소리.
    // 선수 기합(Audio/Grunts, CC0 녹음)은 서브·스매시와 세게 친 드라이브에서 가끔: 친 자리에서, 내 기합은 작게, 두 쪽 목소리 높이를 조금 다르게
    [RequireComponent(typeof(AudioSource))]
    public class AudioFeedback : MonoBehaviour
    {
        [SerializeField] TennisBall ball;
        [SerializeField] MatchReferee referee;
        [Header("라켓 타구음 녹음 (비어 있으면 합성음)")]
        [SerializeField] AudioClip[] driveHits;  // 드라이브·로브
        [SerializeField] AudioClip[] sliceHits;  // 슬라이스·드롭샷
        [SerializeField] AudioClip[] serveHits;  // 서브·스매시
        [SerializeField] AudioClip[] volleyHits; // 발리
        [Header("바운드 녹음 (잔디 녹음을 코트마다 다르게 튼다)")]
        [SerializeField] AudioClip[] bounceClips;
        [Header("선수 기합 녹음 (비어 있으면 없음)")]
        [SerializeField] AudioClip[] grunts;

        const int Variants = 4;
        const int Voices = 6; // 타구·바운드가 번갈아 써서 넉넉히 (소리 도중 위치·높이가 바뀌지 않게)

        AudioSource source;
        AudioClip bounce, alert, point;
        AudioClip[,] hits;
        AudioSource[] voices;
        int nextVoice;
        readonly System.Random rng = new System.Random(17);

        int lastHitter;      // 지금 날고 있는 공을 친 쪽 (0 = 없음)
        int bouncesSinceHit; // 그 뒤 바운드 수 (0이면 다음 타구는 발리)

        public HitSoundKind LastHitKind { get; private set; }
        public int GruntCount { get; private set; }

        void Awake()
        {
            source = GetComponent<AudioSource>();
            bounce = Tone("Bounce", 170f, 0.10f, 40f, 0.45f, 2);
            alert = Tone("Alert", 1320f, 0.07f, 18f, 0f, 3);
            point = Tone("Point", 660f, 0.30f, 9f, 0f, 4);

            var kinds = (HitSoundKind[])System.Enum.GetValues(typeof(HitSoundKind));
            hits = new AudioClip[kinds.Length, Variants];
            foreach (HitSoundKind kind in kinds)
                for (int v = 0; v < Variants; v++) hits[(int)kind, v] = RacketSound.CreateClip(kind, v);

            // 친 자리·튄 자리에 두는 3D 소리 (카메라에 붙이지 않아 카메라가 움직여도 그 자리에서 난다)
            var holder = new GameObject("HitSounds").transform;
            voices = new AudioSource[Voices];
            for (int i = 0; i < Voices; i++)
            {
                var go = new GameObject($"Voice{i}");
                go.transform.SetParent(holder, false);
                AudioSource s = go.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.spatialBlend = 0.85f;     // 방향은 들리되 너무 한쪽으로 쏠리지 않게
                s.rolloffMode = AudioRolloffMode.Linear;
                s.minDistance = 1.5f;
                s.maxDistance = 90f;        // 코트 반대편(약 24m)에서도 70%쯤
                s.dopplerLevel = 0f;
                s.spread = 60f;
                voices[i] = s;
            }
        }

        void OnEnable()
        {
            ball.Launched += OnLaunched;
            ball.Bounced += OnBounced;
            referee.PointEnded += OnPointEnded;
        }

        void OnDisable()
        {
            ball.Launched -= OnLaunched;
            ball.Bounced -= OnBounced;
            referee.PointEnded -= OnPointEnded;
        }

        public void PlayAlert() => source.PlayOneShot(alert, 0.35f);

        void OnLaunched(int side)
        {
            if (side == 0) // 토스는 소리 없음
            {
                lastHitter = 0;
                return;
            }
            bool volley = lastHitter == -side && bouncesSinceHit == 0 && !ball.IsServe;
            lastHitter = side;
            bouncesSinceHit = 0;
            PlayHit(ball.State, ball.IsServe, volley, side);
        }

        void PlayHit(in BallState launch, bool serve, bool volley, int side)
        {
            HitSoundKind kind = RacketSound.Classify(launch, serve, volley);
            LastHitKind = kind;
            float speed01 = Mathf.InverseLerp(8f, 40f, launch.Velocity.magnitude);
            AudioSource voice = voices[nextVoice];
            nextVoice = (nextVoice + 1) % voices.Length;
            voice.transform.position = launch.Position;
            voice.pitch = Mathf.Lerp(0.93f, 1.07f, speed01) * (1f + ((float)rng.NextDouble() - 0.5f) * 0.03f); // 빠를수록 밝게
            float volume = Mathf.Lerp(0.45f, 1f, speed01);
            AudioClip[] recorded = RecordedFor(kind);
            if (kind == HitSoundKind.Lob) // 로브는 드라이브 녹음을 부드럽게
            {
                voice.pitch *= 0.95f;
                volume *= 0.75f;
            }
            AudioClip clip = recorded != null && recorded.Length > 0
                ? recorded[rng.Next(recorded.Length)]
                : hits[(int)kind, rng.Next(Variants)];
            voice.PlayOneShot(clip, volume);
            if (rng.NextDouble() < GruntChance(kind, speed01)) Grunt(launch.Position, side);
        }

        // 기합 확률: 서브·스매시는 거의 늘, 드라이브는 빠를수록 (느린 공은 없음). 슬라이스·로브·발리는 조용히 친다
        public static float GruntChance(HitSoundKind kind, float speed01) => kind switch
        {
            HitSoundKind.Serve or HitSoundKind.Smash => 0.85f,
            HitSoundKind.Drive => speed01 < 0.45f ? 0f : Mathf.Lerp(0.3f, 0.7f, Mathf.InverseLerp(0.45f, 1f, speed01)),
            _ => 0f,
        };

        void Grunt(Vector3 at, int side)
        {
            if (grunts == null || grunts.Length == 0) return;
            AudioClip clip = grunts[rng.Next(grunts.Length)];
            if (clip == null) return;
            bool mine = referee != null && !referee.Watching && side == referee.PlayerSide;
            AudioSource voice = voices[nextVoice];
            nextVoice = (nextVoice + 1) % voices.Length;
            voice.transform.position = at + Vector3.up * 0.4f; // 입 높이쯤
            voice.pitch = (side < 0 ? 1f : 0.93f) * (1f + ((float)rng.NextDouble() - 0.5f) * 0.05f); // 두 선수 목소리를 조금 다르게
            voice.PlayOneShot(clip, (mine ? 0.3f : 0.6f) * UserSettings.CrowdVolume);
            GruntCount++;
        }

        AudioClip[] RecordedFor(HitSoundKind kind) => kind switch
        {
            HitSoundKind.Slice => sliceHits,
            HitSoundKind.Volley => volleyHits != null && volleyHits.Length > 0 ? volleyHits : driveHits,
            HitSoundKind.Serve or HitSoundKind.Smash => serveHits,
            _ => driveHits,
        };

        void OnBounced(Vector3 at)
        {
            bouncesSinceHit++;
            if (bounceClips == null || bounceClips.Length == 0)
            {
                source.PlayOneShot(bounce, VolumeAt(at) * 0.8f);
                return;
            }
            // 튀어 오르는 속도로 세기를 정한다 (굴러가듯 낮게 튀면 작게)
            float strength = Mathf.InverseLerp(0.3f, 6f, ball.State.Velocity.y);
            (float pitch, float volume) = BounceTone(Court.Type);
            AudioSource voice = voices[nextVoice];
            nextVoice = (nextVoice + 1) % voices.Length;
            voice.transform.position = at;
            voice.pitch = pitch * (1f + ((float)rng.NextDouble() - 0.5f) * 0.06f);
            voice.PlayOneShot(bounceClips[rng.Next(bounceClips.Length)], volume * Mathf.Lerp(0.3f, 1f, strength));
        }

        // 녹음은 잔디라서 하드는 높고 또렷하게, 클레이는 낮고 부드럽게 바꿔 튼다
        static (float pitch, float volume) BounceTone(CourtType court) => court switch
        {
            CourtType.Hard => (1.12f, 0.75f),
            CourtType.Clay => (0.88f, 0.55f),
            _ => (1f, 0.6f),
        };

        void OnPointEnded(int winner, PointReason reason)
        {
            lastHitter = 0;
            source.PlayOneShot(point, 0.4f);
        }

        float VolumeAt(Vector3 at) => Mathf.Clamp(1f - Vector3.Distance(at, transform.position) / 35f, 0.15f, 1f);

        static AudioClip Tone(string name, float frequency, float duration, float decay, float noise, int seed)
        {
            const int Rate = 44100;
            int count = Mathf.CeilToInt(Rate * duration);
            var data = new float[count];
            var rng = new System.Random(seed);
            for (int i = 0; i < count; i++)
            {
                float t = (float)i / Rate;
                float tone = Mathf.Sin(2f * Mathf.PI * frequency * t) * (1f - noise);
                float hiss = ((float)rng.NextDouble() * 2f - 1f) * noise;
                data[i] = (tone + hiss) * Mathf.Exp(-t * decay) * 0.6f;
            }
            AudioClip clip = AudioClip.Create(name, count, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
