// 메인 화면 배경음악 작곡·합성기. 녹음이나 외부 음원 없이 이 코드만으로 곡을 만든다 (고치고 싶으면 여기를 고쳐 다시 실행)
// 실행 (.NET 10, 이 폴더에서): dotnet run TitleTheme.cs   (저장소 루트에는 Unity가 만든 .csproj가 있어 루트에서는 안 된다)
// 결과: Assets/_Project/Audio/Music/TitleTheme.wav (44.1kHz 스테레오 16bit, 끝과 처음이 이어져 끊김 없이 반복된다)
//
// D장조 104BPM 32마디 (약 74초)
//   1~8   그루브: 패드·베이스·아르페지오·드럼
//   9~16  주선율 A (일렉 피아노 + 부드러운 리드)
//   17~24 주선율 B (한 옥타브 위로 올라가는 변주)
//   25~32 쉬어 가기: 드럼이 빠졌다가 다시 쌓이고, 마지막 마디의 스네어 롤·라이저가 1마디로 이어진다
// 코드 진행 (8마디 반복): Gmaj7 | Aadd9 | F#m7 | Bm7 | Em7 | F#m7 | Gmaj7 | A7sus4 A7

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

const int Rate = Synth.Rate;
const double Bpm = 104;
const int Bars = 32;
const double Beat = 60.0 / Bpm;
const double Step = Beat / 4; // 16분음표
string output = args.Length > 0 ? args[0] : "../../Assets/_Project/Audio/Music/TitleTheme.wav";

int loopLen = (int)Math.Round(Bars * 16 * Step * Rate);
int total = loopLen + Rate * 4; // 끝에서 울리는 소리는 처음으로 감는다

var dry = new Bus(total);
var ducked = new Bus(total); // 킥에 맞춰 살짝 눌리는 패드·아르페지오
var verbSend = new Bus(total);
var echoSend = new Bus(total);
var rng = new Random(2026);
var kicks = new List<double>();

// 8마디 코드: 베이스 음(MIDI)과 구성음(음이름 0=C … 11=B)
var chords = new (int bass, int[] tones)[]
{
    (43, new[] { 7, 11, 2, 6 }), // Gmaj7
    (45, new[] { 9, 1, 4, 11 }), // Aadd9
    (42, new[] { 6, 9, 1, 4 }),  // F#m7
    (47, new[] { 11, 2, 6, 9 }), // Bm7
    (40, new[] { 4, 7, 11, 2 }), // Em7
    (42, new[] { 6, 9, 1, 4 }),  // F#m7
    (43, new[] { 7, 11, 2, 6 }), // Gmaj7
    (45, new[] { 9, 2, 4, 7 }),  // A7sus4 (뒤 두 박은 A7)
};
(int bass, int[] tones) a7 = (45, new[] { 9, 1, 4, 7 });
(int bass, int[] tones) ChordAt(int bar, double step) => bar % 8 == 7 && step >= 8 ? a7 : chords[bar % 8];

// 주선율: 마디마다 (8분음표 칸, 길이(칸), 음 MIDI)
var melodyA = new (int at, int len, int note)[][]
{
    new[] { (1, 1, 71), (2, 1, 74), (3, 3, 78), (6, 2, 76) },
    new[] { (0, 2, 71), (2, 1, 73), (3, 1, 74), (4, 4, 76) },
    new[] { (1, 1, 69), (2, 1, 73), (3, 3, 76), (6, 1, 74), (7, 1, 73) },
    new[] { (0, 4, 74), (4, 2, 73), (6, 2, 71) },
    new[] { (1, 1, 71), (2, 1, 74), (3, 3, 79), (6, 1, 78), (7, 1, 76) },
    new[] { (0, 2, 78), (2, 1, 76), (3, 2, 73), (5, 3, 69) },
    new[] { (0, 1, 71), (1, 1, 74), (2, 2, 78), (4, 2, 81), (6, 1, 79), (7, 1, 78) },
    new[] { (0, 3, 76), (3, 1, 74), (4, 4, 73) },
};
var melodyB = new (int at, int len, int note)[][]
{
    new[] { (0, 3, 83), (3, 1, 81), (4, 2, 78), (6, 2, 74) },
    new[] { (0, 2, 76), (2, 1, 78), (3, 1, 76), (4, 4, 73) },
    new[] { (0, 3, 85), (3, 1, 83), (4, 2, 81), (6, 2, 78) },
    new[] { (0, 2, 78), (2, 2, 81), (4, 4, 83) },
    new[] { (0, 3, 79), (3, 1, 78), (4, 2, 76), (6, 1, 74), (7, 1, 76) },
    new[] { (0, 3, 78), (3, 1, 76), (4, 2, 73), (6, 2, 76) },
    new[] { (0, 2, 74), (2, 2, 78), (4, 2, 81), (6, 2, 83) },
    new[] { (0, 2, 81), (2, 1, 79), (3, 1, 78), (4, 4, 76) },
};

float[] kick = Synth.Kick();
float[] clap = Synth.Clap(11);
float[] snare = Synth.Snare(12);
float[] crash = Synth.Crash(13);
float[][] hats = Enumerable.Range(0, 4).Select(i => Synth.Hat(false, 20 + i)).ToArray();
float[] openHat = Synth.Hat(true, 30);
float[][] shakers = Enumerable.Range(0, 3).Select(i => Synth.Shaker(40 + i)).ToArray();

double T(int bar, double step) => (bar * 16 + step) * Step;
float Vary(float gain) => gain * (0.92f + 0.16f * (float)rng.NextDouble());

void Play(double time, float[] s, float gain, float pan, float reverb, float delay = 0f, bool duck = false)
{
    int at = (int)Math.Round(time * Rate);
    if (at < 0) at += loopLen; // 첫 박보다 살짝 앞선 소리는 곡 끝에 (반복되면 바로 앞)
    (duck ? ducked : dry).Add(at, s, gain, pan);
    if (reverb > 0f) verbSend.Add(at, s, gain * reverb, pan);
    if (delay > 0f) echoSend.Add(at, s, gain * delay, pan);
}

for (int bar = 0; bar < Bars; bar++)
{
    int section = bar / 8; // 0 그루브, 1 주선율 A, 2 주선율 B, 3 쉬어 가기
    int k = bar % 8;
    bool full = section < 3;

    // 드럼
    int[] kickSteps = full ? (k % 2 == 1 ? new[] { 0, 6, 8, 11 } : new[] { 0, 6, 8 })
        : k < 4 ? Array.Empty<int>() : k < 6 ? new[] { 0, 8 } : new[] { 0, 4, 8, 12 };
    foreach (int s in kickSteps)
    {
        kicks.Add(T(bar, s));
        Play(T(bar, s), kick, 0.65f, 0f, 0f);
    }
    int[] clapSteps = full || k == 6 ? new[] { 4, 12 } : k == 7 ? new[] { 4 } : Array.Empty<int>();
    foreach (int s in clapSteps) Play(T(bar, s), clap, Vary(0.34f), 0.05f, 0.3f);
    for (int s = 0; s < 16; s += 2)
    {
        bool offbeat = s % 4 == 2;
        if (!full && (k < 2 || (k < 6 && !offbeat))) continue;
        double jitter = (rng.NextDouble() - 0.5) * 0.004;
        if (s == 14 && k % 2 == 1 && (full || k >= 6)) Play(T(bar, s) + jitter, openHat, 0.16f, 0.35f, 0.08f);
        else Play(T(bar, s) + jitter, hats[rng.Next(hats.Length)], Vary(offbeat ? 0.2f : 0.13f) * (full || k >= 6 ? 1f : 0.7f), 0.35f, 0.05f);
    }
    if (section == 1 || section == 2)
        for (int s = 0; s < 16; s++)
            Play(T(bar, s) + (rng.NextDouble() - 0.5) * 0.004, shakers[rng.Next(shakers.Length)], Vary(s % 4 == 2 ? 0.07f : s % 2 == 1 ? 0.032f : 0.05f), -0.45f, 0.05f);
    if (bar == 0 || bar == 8 || bar == 16) Play(T(bar, 0), crash, 0.15f, -0.25f, 0.2f);
    if (bar == Bars - 1)
        for (int s = 8; s < 16; s++) Play(T(bar, s), snare, 0.05f + 0.18f * (s - 8) / 7f, 0.1f, 0.25f);
    if (bar == Bars - 2) Play(T(bar, 0), Synth.Riser(32 * Step), 0.09f, 0f, 0.3f);

    // 베이스
    int root = chords[k].bass;
    if (full || k >= 6)
    {
        int[] steps = { 0, 3, 6, 8, 11, 14 };
        int[] up = { 0, 0, 12, 0, 0, 12 };
        double[] lengths = { 2.5, 2.5, 1.5, 2.5, 2.5, 1.5 };
        for (int i = 0; i < steps.Length; i++)
            Play(T(bar, steps[i]), Synth.Bass(ChordAt(bar, steps[i]).bass + up[i], lengths[i] * Step), Vary(0.24f), 0f, 0f);
    }
    else if (k < 4) Play(T(bar, 0), Synth.Bass(root, 15.5 * Step), 0.2f, 0f, 0f);
    else
        for (int s = 0; s < 16; s += 8) Play(T(bar, s), Synth.Bass(root, 7.5 * Step), 0.22f, 0f, 0f);

    // 패드 (마지막 마디는 반 마디씩 sus4 → 7)
    float padGain = section == 3 ? 0.095f : section == 0 ? 0.085f : 0.07f;
    foreach (double half in k == 7 ? new[] { 0.0, 8.0 } : new[] { 0.0 })
    {
        double length = (k == 7 ? 8 : 16) * Step;
        foreach (int note in Synth.Voice(ChordAt(bar, half).tones, 55))
        {
            var (l, r) = Synth.Pad(note, length, rng.Next());
            int at = (int)Math.Round(T(bar, half) * Rate);
            ducked.AddStereo(at, l, r, padGain);
            verbSend.AddStereo(at, l, r, padGain * 0.55f);
        }
    }

    // 아르페지오: 그루브·주선율 구간은 8분음표, 쉬어 가는 구간은 16분음표
    int[] wave = { 0, 1, 2, 3, 4, 3, 2, 1 };
    int every = section == 3 ? 1 : 2;
    float arpGain = section switch { 0 => 0.1f, 1 => 0.065f, 2 => 0.075f, _ => 0.085f };
    for (int s = 0, n = 0; s < 16; s += every, n++)
    {
        int[] v = Synth.Voice(ChordAt(bar, s).tones, 64);
        int[] five = { v[0], v[1], v[2], v[3], v[0] + 12 };
        Play(T(bar, s), Synth.Pluck(five[wave[n % 8]], every * Step), Vary(arpGain), n % 2 == 0 ? -0.5f : 0.5f, 0.35f, 0.1f, duck: true);
    }

    // 주선율 (쉬어 가는 구간 뒤 네 마디는 일렉 피아노만 작게 A의 뒷부분을 미리 들려준다)
    (int at, int len, int note)[]? phrase = section == 1 ? melodyA[k] : section == 2 ? melodyB[k] : section == 3 && k >= 4 ? melodyA[k] : null;
    if (phrase != null)
        foreach (var (at, len, note) in phrase)
        {
            float gain = section == 3 ? 0.12f : 0.17f;
            if (at == 0) gain *= 1.08f;
            Play(T(bar, at * 2), Synth.Lead(note, len * 2 * Step * 0.92, section == 3 ? 0f : 1f), Vary(gain), 0.05f, 0.25f, 0.22f);
        }
}

// 끝을 넘어 울리는 소리를 처음에 더해 고리로 만든다
foreach (Bus b in new[] { dry, ducked, verbSend, echoSend }) b.Wrap(loopLen);

// 킥이 칠 때마다 패드·아르페지오를 잠깐 눌러 그루브를 만든다
kicks.Sort();
for (int i = 0, ki = -1; i < loopLen; i++)
{
    double t = (double)i / Rate;
    while (ki + 1 < kicks.Count && kicks[ki + 1] <= t) ki++;
    if (ki < 0) continue;
    float g = 1f - 0.4f * (float)Math.Exp(-(t - kicks[ki]) / 0.09);
    dry.L[i] += ducked.L[i] * g;
    dry.R[i] += ducked.R[i] * g;
}

// 리버브·딜레이는 곡을 두 번 돌려 두 번째 결과를 쓴다 (처음에도 앞 바퀴의 잔향이 이어져 있게)
var reverb = new Freeverb(room: 0.62f, damp: 0.45f);
var delay = new PingPong((int)Math.Round(Beat * 0.75 * Rate), feedback: 0.32f, damp: 0.35f);
var mixL = new float[loopLen];
var mixR = new float[loopLen];
double dryEnergy = 0, wetEnergy = 0;
for (int pass = 0; pass < 2; pass++)
    for (int i = 0; i < loopLen; i++)
    {
        var (rl, rr) = reverb.Process(verbSend.L[i], verbSend.R[i]);
        var (el, er) = delay.Process(echoSend.L[i], echoSend.R[i]);
        if (pass == 0) continue;
        mixL[i] = dry.L[i] + rl + el;
        mixR[i] = dry.R[i] + rr + er;
        dryEnergy += dry.L[i] * dry.L[i] + dry.R[i] * dry.R[i];
        wetEnergy += (rl + el) * (rl + el) + (rr + er) * (rr + er);
    }

// 마스터: 최고점을 맞추고 부드럽게 눌러 -1dB로
float peak = 0f;
for (int i = 0; i < loopLen; i++) peak = Math.Max(peak, Math.Max(Math.Abs(mixL[i]), Math.Abs(mixR[i])));
const float Drive = 1.25f;
float norm = (float)Math.Tanh(Drive);
for (int i = 0; i < loopLen; i++)
{
    mixL[i] = (float)Math.Tanh(Drive * mixL[i] / peak) / norm * 0.89f;
    mixR[i] = (float)Math.Tanh(Drive * mixR[i] / peak) / norm * 0.89f;
}

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
Synth.WriteWav(output, mixL, mixR);

// 확인용 수치: 구간별 크기, 잔향 비율, 이음매
Console.WriteLine($"{output}: {loopLen / (double)Rate:0.00}s, 원래 최고점 {peak:0.00}, 잔향/원음 {Math.Sqrt(wetEnergy / dryEnergy):0.00}");
for (int section = 0; section < 4; section++)
{
    int a = (int)(T(section * 8, 0) * Rate), b = Math.Min(loopLen, (int)(T(section * 8 + 8, 0) * Rate));
    double sum = 0; float p = 0;
    for (int i = a; i < b; i++) { sum += mixL[i] * mixL[i] + mixR[i] * mixR[i]; p = Math.Max(p, Math.Abs(mixL[i])); }
    Console.WriteLine($"  구간 {section + 1}: RMS {10 * Math.Log10(sum / (2 * (b - a))):0.0} dBFS, 최고 {p:0.00}");
}
double typical = 0;
for (int i = 1; i < loopLen; i++) typical += Math.Abs(mixL[i] - mixL[i - 1]);
typical /= loopLen - 1;
Console.WriteLine($"  이음매 차이 {Math.Abs(mixL[0] - mixL[loopLen - 1]):0.0000} (평소 표본 간 평균 {typical:0.0000})");

class Bus
{
    public readonly float[] L, R;
    public Bus(int length) { L = new float[length]; R = new float[length]; }

    // 등전력 팬 (-1 왼쪽 … 1 오른쪽)
    public void Add(int at, float[] mono, float gain, float pan)
    {
        double angle = (pan + 1) * Math.PI / 4;
        float gl = (float)Math.Cos(angle) * gain, gr = (float)Math.Sin(angle) * gain;
        for (int i = 0; i < mono.Length && at + i < L.Length; i++)
        {
            L[at + i] += mono[i] * gl;
            R[at + i] += mono[i] * gr;
        }
    }

    public void AddStereo(int at, float[] l, float[] r, float gain)
    {
        for (int i = 0; i < l.Length && at + i < L.Length; i++)
        {
            L[at + i] += l[i] * gain;
            R[at + i] += r[i] * gain;
        }
    }

    public void Wrap(int loop)
    {
        for (int i = loop; i < L.Length; i++)
        {
            L[i - loop] += L[i];
            R[i - loop] += R[i];
        }
    }
}

static class Synth
{
    public const int Rate = 44100;
    const double Tau = Math.PI * 2;

    static double Hz(double midi) => 440.0 * Math.Pow(2, (midi - 69) / 12.0);

    // 구성음을 [low, low+12) 음역에 놓은 화음 (낮은 음부터)
    public static int[] Voice(int[] tones, int low)
    {
        int[] notes = tones.Select(t => low + ((t - low) % 12 + 12) % 12).ToArray();
        Array.Sort(notes);
        return notes;
    }

    // 킥: 150Hz에서 45Hz로 떨어지는 사인 + 짧은 클릭
    public static float[] Kick()
    {
        var s = new float[(int)(Rate * 0.45)];
        double phase = 0;
        for (int i = 0; i < s.Length; i++)
        {
            double t = (double)i / Rate;
            phase += Tau * (45 + 105 * Math.Exp(-t * 30)) / Rate;
            double body = Math.Sin(phase) * Math.Exp(-t * 6.5);
            double click = Math.Sin(Tau * 1800 * t) * Math.Exp(-t * 900) * 0.4;
            s[i] = (float)Math.Tanh(1.4 * (body + click));
        }
        FadeOut(s, 0.02);
        return s;
    }

    // 클랩: 잘게 세 번 터진 뒤 짧은 꼬리
    public static float[] Clap(int seed)
    {
        var rng = new Random(seed);
        var bp = Biquad.BandPass(1400, 1.1);
        var s = new float[(int)(Rate * 0.35)];
        for (int i = 0; i < s.Length; i++)
        {
            double t = (double)i / Rate, env = 0;
            foreach (double d in new[] { 0, 0.010, 0.021 })
                if (t >= d) env = Math.Max(env, Math.Exp(-(t - d) * 170));
            if (t >= 0.021) env = Math.Max(env, 0.55 * Math.Exp(-(t - 0.021) * 16));
            s[i] = (float)(bp.Run(rng.NextDouble() * 2 - 1) * env);
        }
        return Normalize(s);
    }

    public static float[] Snare(int seed)
    {
        var rng = new Random(seed);
        var bp = Biquad.BandPass(2200, 0.6);
        var s = new float[(int)(Rate * 0.25)];
        for (int i = 0; i < s.Length; i++)
        {
            double t = (double)i / Rate;
            s[i] = (float)(bp.Run(rng.NextDouble() * 2 - 1) * Math.Exp(-t * 22) + 0.5 * Math.Sin(Tau * 190 * t) * Math.Exp(-t * 28));
        }
        FadeOut(s, 0.01);
        return Normalize(s);
    }

    // 하이햇: 높은 잡음, 닫힘은 짧게 열림은 길게
    public static float[] Hat(bool open, int seed)
    {
        var rng = new Random(seed);
        var hp = Biquad.HighPass(6000, 0.7);
        var s = new float[(int)(Rate * (open ? 0.45 : 0.08))];
        for (int i = 0; i < s.Length; i++)
        {
            double t = (double)i / Rate;
            s[i] = (float)(hp.Run(rng.NextDouble() * 2 - 1) * Math.Exp(-t * (open ? 9 : 60)) * Math.Min(1, t / 0.0005));
        }
        FadeOut(s, 0.01);
        return Normalize(s);
    }

    public static float[] Shaker(int seed)
    {
        var rng = new Random(seed);
        var hp = Biquad.HighPass(5500, 0.7);
        var s = new float[(int)(Rate * 0.12)];
        for (int i = 0; i < s.Length; i++)
        {
            double t = (double)i / Rate;
            double env = t < 0.008 ? t / 0.008 : Math.Exp(-(t - 0.008) * 45);
            s[i] = (float)(hp.Run(rng.NextDouble() * 2 - 1) * env);
        }
        FadeOut(s, 0.01);
        return Normalize(s);
    }

    // 크래시: 높은 잡음 + 어긋난 금속성 배음, 길게 사라진다
    public static float[] Crash(int seed)
    {
        var rng = new Random(seed);
        var hp = Biquad.HighPass(3500, 0.6);
        double[] partials = { 540, 803, 1172, 1431, 1797, 2289 };
        var s = new float[(int)(Rate * 2.5)];
        for (int i = 0; i < s.Length; i++)
        {
            double t = (double)i / Rate, metal = 0;
            foreach (double f in partials) metal += Math.Sign(Math.Sin(Tau * f * t));
            double x = rng.NextDouble() * 2 - 1 + metal * 0.12;
            s[i] = (float)(hp.Run(x) * Math.Exp(-t * 1.4) * Math.Min(1, t / 0.002));
        }
        FadeOut(s, 0.05);
        return Normalize(s);
    }

    // 라이저: 걸러 낸 잡음이 점점 높고 커지다가 다음 마디 첫 박에서 끊긴다
    public static float[] Riser(double seconds)
    {
        var rng = new Random(77);
        var bp = Biquad.BandPass(400, 1.5);
        var s = new float[(int)(Rate * seconds)];
        for (int i = 0; i < s.Length; i++)
        {
            double u = (double)i / s.Length;
            if (i % 64 == 0) bp.SetBandPass(400 * Math.Pow(7000.0 / 400, u), 1.5);
            s[i] = (float)(bp.Run(rng.NextDouble() * 2 - 1) * u * u);
        }
        FadeOut(s, 0.005);
        return Normalize(s);
    }

    // 베이스: 사인에 2·3배음을 조금 섞고 살짝 찌그러뜨린다
    public static float[] Bass(int midi, double length)
    {
        double f = Hz(midi), release = 0.05;
        var s = new float[(int)(Rate * (length + release))];
        double phase = 0;
        for (int i = 0; i < s.Length; i++)
        {
            double t = (double)i / Rate;
            double env = t < 0.004 ? t / 0.004 : 0.75 + 0.25 * Math.Exp(-(t - 0.004) / 0.12);
            if (t > length) env *= Math.Max(0, 1 - (t - length) / release);
            phase += Tau * f / Rate;
            double x = Math.Sin(phase) + 0.35 * Math.Sin(2 * phase) + 0.12 * Math.Sin(3 * phase);
            s[i] = (float)(Math.Tanh(1.3 * x) / 1.3 * env);
        }
        return s;
    }

    // 패드: 부드러운 톱니파 두 겹을 조금 어긋나게 좌우로 벌린다
    public static (float[] l, float[] r) Pad(int midi, double length, int seed)
    {
        double f = Hz(midi), attack = 0.35, release = 0.8;
        var rng = new Random(seed);
        int n = (int)(Rate * (length + release));
        var voices = new[] { new double[n], new double[n] };
        for (int v = 0; v < 2; v++)
        {
            double detune = v == 0 ? 0.9975 : 1.0025;
            for (int h = 1; h <= 10 && f * h * detune < 6000; h++)
            {
                double amp = Math.Exp(-(h - 1) * 0.22) / h, phase = rng.NextDouble() * Tau, w = Tau * f * detune * h / Rate;
                for (int i = 0; i < n; i++) voices[v][i] += amp * Math.Sin(phase + w * i);
            }
        }
        var l = new float[n];
        var r = new float[n];
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / Rate;
            double env = Math.Min(1, t / attack);
            env = env * env * (3 - 2 * env);
            if (t > length) env *= Math.Exp(-(t - length) / 0.25) * (1 - (t - length) / release);
            l[i] = (float)((0.85 * voices[0][i] + 0.15 * voices[1][i]) * env);
            r[i] = (float)((0.15 * voices[0][i] + 0.85 * voices[1][i]) * env);
        }
        return (l, r);
    }

    // 플럭: 배음이 높을수록 빨리 사라지는 튕긴 소리
    public static float[] Pluck(int midi, double length)
    {
        double f = Hz(midi);
        var s = new float[(int)(Rate * (length + 0.4))];
        for (int i = 0; i < s.Length; i++)
        {
            double t = (double)i / Rate, x = 0;
            for (int h = 1; h <= 10 && f * h < 12000; h++) x += Math.Sin(Tau * f * h * t) / h * Math.Exp(-t * (3 + h * 2.2));
            double env = Math.Min(1, t / 0.002);
            if (t > length) env *= Math.Exp(-(t - length) * 25);
            s[i] = (float)(x * env * 0.6);
        }
        FadeOut(s, 0.02);
        return s;
    }

    // 주선율: FM 일렉 피아노 + (softLayer만큼) 부드러운 사각파 리드, 길게 끌면 비브라토
    public static float[] Lead(int midi, double length, float softLayer)
    {
        double f = Hz(midi), release = 0.35;
        var s = new float[(int)(Rate * (length + release))];
        double phase = 0;
        for (int i = 0; i < s.Length; i++)
        {
            double t = (double)i / Rate;
            double vibrato = 1 + 0.0035 * Math.Sin(Tau * 5.2 * t) * Math.Clamp((t - 0.18) / 0.3, 0, 1);
            phase += Tau * f * vibrato / Rate;
            double index = 0.35 + 1.6 * Math.Exp(-t * 7);
            double piano = Math.Sin(phase + index * Math.Sin(phase)) * Math.Exp(-t * 1.1);
            double tine = 0.12 * Math.Sin(14 * phase) * Math.Exp(-t * 45);
            double square = 0;
            for (int h = 1; h <= 9; h += 2) square += Math.Sin(h * phase) / h * Math.Exp(-(h - 1) * 0.12);
            double env = Math.Min(1, t / 0.002);
            if (t > length) env *= Math.Exp(-(t - length) / 0.09);
            s[i] = (float)((piano * 0.8 + tine + softLayer * 0.45 * square * Math.Min(1, t / 0.03)) * env);
        }
        FadeOut(s, 0.02);
        return s;
    }

    static void FadeOut(float[] s, double seconds)
    {
        int n = Math.Min(s.Length, (int)(Rate * seconds));
        for (int i = 0; i < n; i++) s[s.Length - 1 - i] *= (float)i / n;
    }

    static float[] Normalize(float[] s)
    {
        float p = s.Max(v => Math.Abs(v));
        if (p > 0) for (int i = 0; i < s.Length; i++) s[i] /= p;
        return s;
    }

    public static void WriteWav(string path, float[] l, float[] r)
    {
        using var w = new BinaryWriter(File.Create(path));
        int bytes = l.Length * 4;
        w.Write("RIFF".ToCharArray()); w.Write(36 + bytes); w.Write("WAVE".ToCharArray());
        w.Write("fmt ".ToCharArray()); w.Write(16); w.Write((short)1); w.Write((short)2); w.Write(Rate); w.Write(Rate * 4); w.Write((short)4); w.Write((short)16);
        w.Write("data".ToCharArray()); w.Write(bytes);
        for (int i = 0; i < l.Length; i++)
        {
            w.Write((short)(Math.Clamp(l[i], -1f, 1f) * 32767));
            w.Write((short)(Math.Clamp(r[i], -1f, 1f) * 32767));
        }
    }
}

// RBJ 2차 필터
class Biquad
{
    double b0, b1, b2, a1, a2, x1, x2, y1, y2;

    public static Biquad HighPass(double f, double q)
    {
        var b = new Biquad();
        double w = 2 * Math.PI * f / Synth.Rate, alpha = Math.Sin(w) / (2 * q), cos = Math.Cos(w), a0 = 1 + alpha;
        b.b0 = (1 + cos) / 2 / a0; b.b1 = -(1 + cos) / a0; b.b2 = (1 + cos) / 2 / a0; b.a1 = -2 * cos / a0; b.a2 = (1 - alpha) / a0;
        return b;
    }

    public static Biquad BandPass(double f, double q)
    {
        var b = new Biquad();
        b.SetBandPass(f, q);
        return b;
    }

    public void SetBandPass(double f, double q)
    {
        double w = 2 * Math.PI * f / Synth.Rate, alpha = Math.Sin(w) / (2 * q), cos = Math.Cos(w), a0 = 1 + alpha;
        b0 = alpha / a0; b1 = 0; b2 = -alpha / a0; a1 = -2 * cos / a0; a2 = (1 - alpha) / a0;
    }

    public double Run(double x)
    {
        double y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
        x2 = x1; x1 = x; y2 = y1; y1 = y;
        return y;
    }
}

// Freeverb (Jezar): 병렬 콤 8개 + 직렬 올패스 4개, 오른쪽은 길이를 조금 어긋나게
class Freeverb
{
    static readonly int[] CombTuning = { 1116, 1188, 1277, 1356, 1422, 1491, 1557, 1617 };
    static readonly int[] AllpassTuning = { 556, 441, 341, 225 };
    const int Spread = 23;
    readonly Comb[] combL, combR;
    readonly Allpass[] allL, allR;

    public Freeverb(float room, float damp)
    {
        float feedback = room * 0.28f + 0.7f, d = damp * 0.4f;
        combL = CombTuning.Select(n => new Comb(n, feedback, d)).ToArray();
        combR = CombTuning.Select(n => new Comb(n + Spread, feedback, d)).ToArray();
        allL = AllpassTuning.Select(n => new Allpass(n)).ToArray();
        allR = AllpassTuning.Select(n => new Allpass(n + Spread)).ToArray();
    }

    public (float, float) Process(float l, float r)
    {
        float input = (l + r) * 0.015f, outL = 0f, outR = 0f;
        foreach (Comb c in combL) outL += c.Process(input);
        foreach (Comb c in combR) outR += c.Process(input);
        foreach (Allpass a in allL) outL = a.Process(outL);
        foreach (Allpass a in allR) outR = a.Process(outR);
        return (outL, outR);
    }

    class Comb
    {
        readonly float[] buffer;
        readonly float feedback, damp;
        float store;
        int index;
        public Comb(int length, float feedback, float damp) { buffer = new float[length]; this.feedback = feedback; this.damp = damp; }
        public float Process(float input)
        {
            float output = buffer[index];
            store = output * (1 - damp) + store * damp;
            buffer[index] = input + store * feedback;
            if (++index == buffer.Length) index = 0;
            return output;
        }
    }

    class Allpass
    {
        readonly float[] buffer;
        int index;
        public Allpass(int length) => buffer = new float[length];
        public float Process(float input)
        {
            float delayed = buffer[index];
            buffer[index] = input + delayed * 0.5f;
            if (++index == buffer.Length) index = 0;
            return delayed - input;
        }
    }
}

// 핑퐁 딜레이: 메아리가 왼쪽·오른쪽을 번갈아 가며 점점 어두워진다
class PingPong
{
    readonly float[] left, right;
    readonly float feedback, damp;
    float lowL, lowR;
    int index;

    public PingPong(int length, float feedback, float damp)
    {
        left = new float[length];
        right = new float[length];
        this.feedback = feedback;
        this.damp = damp;
    }

    public (float, float) Process(float l, float r)
    {
        float outL = left[index], outR = right[index];
        lowL += (1 - damp) * (outR - lowL);
        lowR += (1 - damp) * (outL - lowR);
        left[index] = (l + r) * 0.5f + lowL * feedback;
        right[index] = lowR * feedback;
        if (++index == left.Length) index = 0;
        return (outL, outR);
    }
}
