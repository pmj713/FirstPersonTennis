// 경기장 공기 루프 합성기: 녹음 대신 코드로 만든 바탕 소리 (실제 경기 녹음에는 심판 콜·기합이 섞여 있어서)
// 실행 (.NET 10, 이 폴더에서): dotnet run StadiumAir.cs -- ../../../Assets/_Project/Audio/Crowd/Ambience
// 낮은 바람(분홍 잡음, 1.2kHz 아래) + 먼 관중의 웅성임(300~900Hz 대역 잡음 여러 겹이 천천히 커졌다 작아짐).
// 좌우는 따로 만들어 넓게, 끝 2초를 처음에 겹쳐 이음매 없이 반복된다. 길이가 다른 둘을 겹쳐 틀어 반복이 덜 드러나게
using System;
using System.IO;

const int Rate = 48000;
string output = args.Length > 0 ? args[0] : "../../../Assets/_Project/Audio/Crowd/Ambience";
Directory.CreateDirectory(output);
Render(Path.Combine(output, "stadium_air_a.wav"), 11.0, 101);
Render(Path.Combine(output, "stadium_air_b.wav"), 14.5, 202);

static void Render(string path, double seconds, int seed)
{
    int loop = (int)(seconds * Rate), fade = 2 * Rate, n = loop + fade;
    var rng = new Random(seed);
    var l = new double[n];
    var r = new double[n];
    for (int ch = 0; ch < 2; ch++)
    {
        double[] x = ch == 0 ? l : r;
        // 바람: 분홍 잡음 (Paul Kellet) → 50Hz 고역, 1.2kHz 저역, 아주 느린 크기 변화
        double b0 = 0, b1 = 0, b2 = 0, b3 = 0, b4 = 0, b5 = 0, b6 = 0;
        var wind = new double[n];
        for (int i = 0; i < n; i++)
        {
            double w = rng.NextDouble() * 2 - 1;
            b0 = 0.99886 * b0 + w * 0.0555179; b1 = 0.99332 * b1 + w * 0.0750759; b2 = 0.96900 * b2 + w * 0.1538520;
            b3 = 0.86650 * b3 + w * 0.3104856; b4 = 0.55000 * b4 + w * 0.5329522; b5 = -0.7616 * b5 - w * 0.0168980;
            wind[i] = b0 + b1 + b2 + b3 + b4 + b5 + b6 + w * 0.5362; b6 = w * 0.115926;
        }
        wind = Biquad(Biquad(wind, 50, true), 1200, false);
        double drift = rng.NextDouble() * 6.28;
        for (int i = 0; i < n; i++) x[i] += wind[i] * 0.5 * (0.75 + 0.25 * Math.Sin(2 * Math.PI * 0.07 * i / Rate + drift));

        // 먼 웅성임: 대역 잡음 12겹, 겹마다 0.3~0.9초 주기로 천천히 커졌다 작아진다 (말소리가 될 만큼 또렷하지 않게)
        for (int v = 0; v < 12; v++)
        {
            var noise = new double[n];
            for (int i = 0; i < n; i++) noise[i] = rng.NextDouble() * 2 - 1;
            double center = 300 + rng.NextDouble() * 600;
            noise = BandPass(BandPass(noise, center, 1.2), center, 1.2);
            double rate = 1.1 + rng.NextDouble() * 2.2, phase = rng.NextDouble() * 6.28, env = 0;
            for (int i = 0; i < n; i++)
            {
                double target = Math.Max(0, Math.Sin(2 * Math.PI * rate * i / Rate + phase) * 0.5 + 0.5 * Math.Sin(2 * Math.PI * rate * 0.37 * i / Rate + phase * 2));
                env += (target - env) * 0.0004; // 부드럽게
                x[i] += noise[i] * env * 0.35;
            }
        }
    }
    // 이음매: 끝 2초를 처음 2초에 등전력으로 겹친다
    var outL = new double[loop];
    var outR = new double[loop];
    for (int i = 0; i < loop; i++)
    {
        outL[i] = l[i]; outR[i] = r[i];
        if (i < fade)
        {
            double t = (double)i / fade, gin = Math.Sin(t * Math.PI / 2), gout = Math.Cos(t * Math.PI / 2);
            outL[i] = l[i] * gin + l[loop + i] * gout;
            outR[i] = r[i] * gin + r[loop + i] * gout;
        }
    }
    // 평균 크기 -20dBFS (다른 관중 녹음과 같은 기준)
    double sum = 0;
    for (int i = 0; i < loop; i++) sum += outL[i] * outL[i] + outR[i] * outR[i];
    double gain = Math.Pow(10, -20 / 20.0) / Math.Sqrt(sum / (2 * loop));
    using var wr = new BinaryWriter(File.Create(path));
    int bytes = loop * 4;
    wr.Write("RIFF".ToCharArray()); wr.Write(36 + bytes); wr.Write("WAVE".ToCharArray());
    wr.Write("fmt ".ToCharArray()); wr.Write(16); wr.Write((short)1); wr.Write((short)2); wr.Write(Rate); wr.Write(Rate * 4); wr.Write((short)4); wr.Write((short)16);
    wr.Write("data".ToCharArray()); wr.Write(bytes);
    for (int i = 0; i < loop; i++)
    {
        wr.Write((short)(Math.Clamp(outL[i] * gain, -1, 1) * 32767));
        wr.Write((short)(Math.Clamp(outR[i] * gain, -1, 1) * 32767));
    }
    Console.WriteLine($"{path}: {seconds}s");
}

static double[] Biquad(double[] x, double f, bool high)
{
    double w = 2 * Math.PI * f / Rate, alpha = Math.Sin(w) / (2 * 0.7071), cos = Math.Cos(w), a0 = 1 + alpha;
    double b0 = (high ? (1 + cos) / 2 : (1 - cos) / 2) / a0, b1 = (high ? -(1 + cos) : (1 - cos)) / a0, b2 = b0, a1 = -2 * cos / a0, a2 = (1 - alpha) / a0;
    return Filter(x, b0, b1, b2, a1, a2);
}

static double[] BandPass(double[] x, double f, double q)
{
    double w = 2 * Math.PI * f / Rate, alpha = Math.Sin(w) / (2 * q), cos = Math.Cos(w), a0 = 1 + alpha;
    return Filter(x, alpha / a0, 0, -alpha / a0, -2 * cos / a0, (1 - alpha) / a0);
}

static double[] Filter(double[] x, double b0, double b1, double b2, double a1, double a2)
{
    var y = new double[x.Length]; double x1 = 0, x2 = 0, y1 = 0, y2 = 0;
    for (int i = 0; i < x.Length; i++) { double v = b0 * x[i] + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2; x2 = x1; x1 = x[i]; y2 = y1; y1 = v; y[i] = v; }
    return y;
}
