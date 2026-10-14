using UnityEngine;

namespace FirstPersonTennis
{
    // 프로토타입용 효과음. 오디오 에셋 없이 코드로 만든 짧은 소리를 쓴다.
    [RequireComponent(typeof(AudioSource))]
    public class AudioFeedback : MonoBehaviour
    {
        [SerializeField] TennisBall ball;
        [SerializeField] MatchReferee referee;

        AudioSource source;
        AudioClip hit, bounce, alert, point;

        void Awake()
        {
            source = GetComponent<AudioSource>();
            hit = Tone("Hit", 520f, 0.12f, 38f, 0.55f, 1);
            bounce = Tone("Bounce", 170f, 0.10f, 40f, 0.45f, 2);
            alert = Tone("Alert", 1320f, 0.07f, 18f, 0f, 3);
            point = Tone("Point", 660f, 0.30f, 9f, 0f, 4);
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
            if (side != 0) source.PlayOneShot(hit, VolumeAt(ball.State.Position)); // 토스는 소리 없음
        }

        void OnBounced(Vector3 at) => source.PlayOneShot(bounce, VolumeAt(at) * 0.8f);

        void OnPointEnded(int winner, PointReason reason) => source.PlayOneShot(point, 0.4f);

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
