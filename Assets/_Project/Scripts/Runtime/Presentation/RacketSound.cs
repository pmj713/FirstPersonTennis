using UnityEngine;

namespace FirstPersonTennis
{
    public enum HitSoundKind { Drive, Slice, Lob, Volley, Smash, Serve }

    // 라켓으로 공을 치는 소리를 코드로 합성한다 (오디오 파일 없음).
    // 맞는 순간의 딸깍 + 공이 찌그러지며 나는 "퍽"(1kHz 근처 대역 잡음, 음높이가 살짝 내려감)
    // + 줄의 "팅"(감쇠하는 두 음) + 프레임·손의 "둥"(190Hz 근처) + 슬라이스의 긁는 소리.
    // 구질마다 성분 비율이 다르고, 같은 구질도 변형 몇 개를 섞어 기계적으로 들리지 않게 한다
    public static class RacketSound
    {
        public const int SampleRate = 44100;
        public const float Duration = 0.24f;

        struct Recipe
        {
            public float Pop, PopFreq, PopQ, PopTau; // 공 "퍽"
            public float Click;                       // 맞는 순간 딸깍
            public float Ping, PingTau;               // 줄 "팅" (600Hz, 1.75kHz)
            public float Thump, ThumpTau;             // 프레임 "둥" (190Hz)
            public float Brush;                       // 슬라이스 긁힘
            public float Drive;                       // 세게 칠 때 살짝 찌그러뜨림 (tanh)
            public float Dull;                        // 마지막 저역 통과 (0 = 끔, 클수록 둔탁): 깎아 치는 공은 덜 날카롭게
        }

        static Recipe RecipeFor(HitSoundKind kind) => kind switch
        {
            HitSoundKind.Slice => new Recipe { Pop = 0.8f, PopFreq = 850f, PopQ = 2f, PopTau = 0.008f, Click = 0.12f, Ping = 0.12f, PingTau = 0.03f, Thump = 0.3f, ThumpTau = 0.02f, Brush = 0.9f, Drive = 1f, Dull = 0.75f },
            HitSoundKind.Lob => new Recipe { Pop = 0.7f, PopFreq = 950f, PopQ = 2.2f, PopTau = 0.009f, Click = 0.14f, Ping = 0.15f, PingTau = 0.035f, Thump = 0.25f, ThumpTau = 0.02f, Brush = 0.4f, Drive = 1f, Dull = 0.6f },
            HitSoundKind.Volley => new Recipe { Pop = 0.9f, PopFreq = 1050f, PopQ = 2.5f, PopTau = 0.006f, Click = 0.4f, Ping = 0.08f, PingTau = 0.015f, Thump = 0.45f, ThumpTau = 0.012f, Brush = 0f, Drive = 1.1f },
            HitSoundKind.Smash => new Recipe { Pop = 1.1f, PopFreq = 1400f, PopQ = 2f, PopTau = 0.011f, Click = 0.5f, Ping = 0.25f, PingTau = 0.04f, Thump = 0.5f, ThumpTau = 0.025f, Brush = 0f, Drive = 1.6f },
            HitSoundKind.Serve => new Recipe { Pop = 1.1f, PopFreq = 1350f, PopQ = 2.2f, PopTau = 0.011f, Click = 0.45f, Ping = 0.25f, PingTau = 0.04f, Thump = 0.45f, ThumpTau = 0.025f, Brush = 0f, Drive = 1.5f },
            _ => new Recipe { Pop = 1f, PopFreq = 1150f, PopQ = 2.5f, PopTau = 0.010f, Click = 0.35f, Ping = 0.22f, PingTau = 0.035f, Thump = 0.35f, ThumpTau = 0.022f, Brush = 0f, Drive = 1.2f },
        };

        public static AudioClip CreateClip(HitSoundKind kind, int variant)
        {
            float[] data = Synthesize(kind, variant);
            AudioClip clip = AudioClip.Create($"Hit_{kind}_{variant}", data.Length, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        // variant마다 음높이 ±6%, 감쇠 ±10%, 잡음이 조금씩 다르다
        public static float[] Synthesize(HitSoundKind kind, int variant)
        {
            Recipe r = RecipeFor(kind);
            var rng = new System.Random(1000 + (int)kind * 37 + variant * 7919);
            float Vary(float amount) => 1f + ((float)rng.NextDouble() * 2f - 1f) * amount;
            float detune = Vary(0.06f), tauScale = Vary(0.1f);
            float popFreq = r.PopFreq * detune, ping1 = 600f * Vary(0.05f), ping2 = 1750f * Vary(0.05f), thumpFreq = 190f * Vary(0.08f);
            float popTau = r.PopTau * tauScale, pingTau = r.PingTau * tauScale, thumpTau = r.ThumpTau * tauScale;
            float phase1 = (float)rng.NextDouble() * 6.283f, phase2 = (float)rng.NextDouble() * 6.283f;

            int count = Mathf.CeilToInt(SampleRate * Duration);
            var data = new float[count];
            var band = new Biquad();
            float brushLow = 0f, dull = 0f;
            float dullKeep = 1f - r.Dull * 0.85f; // 한 극 저역 통과 계수 (Dull 0.75 ≈ 2kHz 위를 깎음)
            float peak = 0f;
            for (int i = 0; i < count; i++)
            {
                float t = (float)i / SampleRate;
                float noise = (float)rng.NextDouble() * 2f - 1f;
                if (i % 32 == 0) band.SetBandPass(popFreq * (1f - 0.15f * Mathf.Min(t / 0.02f, 1f)), r.PopQ); // 퍽: 음높이가 살짝 내려간다

                float click = noise * Mathf.Exp(-t / 0.0012f) * r.Click;
                float pop = band.Process(noise) * Mathf.Exp(-t / popTau) * r.Pop * 3f;
                float ping = (Mathf.Sin(2f * Mathf.PI * ping1 * t + phase1) * 0.7f + Mathf.Sin(2f * Mathf.PI * ping2 * t + phase2) * 0.3f)
                             * Mathf.Exp(-t / pingTau) * r.Ping;
                float thump = Mathf.Sin(2f * Mathf.PI * thumpFreq * t) * Mathf.Exp(-t / thumpTau) * r.Thump;
                brushLow += (noise - brushLow) * 0.12f; // 긁힘: 고음을 깎은 잡음(약 900Hz 아래), 2ms 올라가 35ms 동안 사라짐
                float brush = brushLow * (1f - Mathf.Exp(-t / 0.002f)) * Mathf.Exp(-t / 0.035f) * r.Brush;

                float s = (click + pop + ping + thump + brush) * (1f - Mathf.Exp(-t / 0.0002f)); // 시작 딸깍임 방지
                if (r.Dull > 0f) s = dull += (s - dull) * dullKeep;
                data[i] = s;
                peak = Mathf.Max(peak, Mathf.Abs(s));
            }

            // 가장 큰 값을 맞추고, 세게 치는 구질은 tanh로 살짝 눌러 단단하게
            float gain = peak > 1e-5f ? 0.9f / peak : 0f;
            float driveNorm = (float)System.Math.Tanh(r.Drive);
            for (int i = 0; i < count; i++)
                data[i] = (float)System.Math.Tanh(data[i] * gain * r.Drive) / driveNorm * 0.9f;
            // 끝 3ms는 0으로 서서히 (뚝 끊기는 소리 방지)
            int fade = SampleRate * 3 / 1000;
            for (int i = 0; i < fade; i++) data[count - 1 - i] *= (float)i / fade;
            return data;
        }

        // 막 친 공의 상태로 구질 소리를 고른다. volley = 지난 상대 타구가 바운드 없이 왔다
        public static HitSoundKind Classify(in BallState launch, bool serve, bool volley)
        {
            if (serve) return HitSoundKind.Serve;
            Vector3 v = launch.Velocity;
            var horizontal = new Vector3(v.x, 0f, v.z);
            float hs = Mathf.Max(0.01f, horizontal.magnitude);
            if (launch.Position.y > 2.3f && v.y < 0f) return HitSoundKind.Smash;
            if (volley) return HitSoundKind.Volley;
            if (v.y / hs > 0.47f) return HitSoundKind.Lob; // 25° 이상 띄움
            // 백스핀 비율 (BallPhysics.BackspinCheck와 같은 계산): +면 백스핀
            var spinSlip = new Vector3(BallPhysics.RealRadius * launch.Spin.z, 0f, -BallPhysics.RealRadius * launch.Spin.x);
            float backspin = Vector3.Dot(spinSlip, horizontal) / (hs * hs);
            return backspin > 0.03f ? HitSoundKind.Slice : HitSoundKind.Drive;
        }

        // 2차 대역 통과 필터 (RBJ 쿡북)
        sealed class Biquad
        {
            float b0, b1, b2, a1, a2, x1, x2, y1, y2;

            public void SetBandPass(float frequency, float q)
            {
                float w = 2f * Mathf.PI * Mathf.Clamp(frequency, 20f, SampleRate * 0.45f) / SampleRate;
                float alpha = Mathf.Sin(w) / (2f * q), cos = Mathf.Cos(w), a0 = 1f + alpha;
                b0 = alpha / a0;
                b1 = 0f;
                b2 = -alpha / a0;
                a1 = -2f * cos / a0;
                a2 = (1f - alpha) / a0;
            }

            public float Process(float x)
            {
                float y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
                x2 = x1; x1 = x;
                y2 = y1; y1 = y;
                return y;
            }
        }
    }
}
