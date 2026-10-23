// 심판 콜 다듬기: 음성 합성 결과(make_umpire_tts.ps1)를 경기장 확성기 소리처럼 바꾼다
// 실행 (.NET 10, 이 폴더에서): dotnet run UmpireFx.cs -- <TTS 폴더> ../../../Assets/_Project/Audio/Umpire
// 앞뒤 무음을 자르고 → 확성기 대역(300Hz~4.5kHz)만 남겨 살짝 눌러 주고 → 경기장 잔향과 0.14초 메아리 → 말소리 크기를 맞춘다
using System;
using System.IO;
using System.Linq;

string input = args[0], output = args[1];
Directory.CreateDirectory(output);
foreach (string path in Directory.GetFiles(input, "*.wav").OrderBy(p => p))
{
    var (x, rate) = Read(path);
    // 앞뒤 무음 (최고점의 -40dB 아래) 자르기
    float peak = x.Max(v => Math.Abs(v));
    int a = Array.FindIndex(x, v => Math.Abs(v) > peak * 0.01f), b = Array.FindLastIndex(x, v => Math.Abs(v) > peak * 0.01f);
    a = Math.Max(0, a - rate / 50);
    b = Math.Min(x.Length - 1, b + rate / 20);
    int tail = (int)(rate * 0.7);
    var y = new double[b - a + 1 + tail];
    for (int i = a; i <= b; i++) y[i - a] = x[i];

    // 확성기: 대역 제한 + 부드러운 포화
    y = Biquad(Biquad(y, rate, 300, true), rate, 300, true);
    y = Biquad(Biquad(y, rate, 4500, false), rate, 4500, false);
    double p = y.Max(v => Math.Abs(v));
    for (int i = 0; i < y.Length; i++) y[i] = Math.Tanh(1.8 * y[i] / p) / Math.Tanh(1.8);

    // 경기장: 잔향(빗살 필터 넷 + 올패스 둘) + 먼 확성기 메아리
    var wet = Reverb(y, rate);
    int echo = (int)(rate * 0.14);
    var z = new double[y.Length];
    for (int i = 0; i < y.Length; i++) z[i] = y[i] + 0.32 * wet[i] + (i >= echo ? 0.18 * y[i - echo] : 0);

    // 말소리 부분의 크기를 -18dBFS로 (최고점 0.9 아래)
    int speech = b - a + 1;
    double rms = Math.Sqrt(z.Take(speech).Average(v => v * v));
    double gain = Math.Min(Math.Pow(10, -18 / 20.0) / rms, 0.9 / z.Max(v => Math.Abs(v)));
    int fade = (int)(rate * 0.25);
    for (int i = 0; i < fade; i++) z[z.Length - 1 - i] *= (double)i / fade;
    Write(Path.Combine(output, Path.GetFileName(path)), z, gain, rate);
}
Console.WriteLine($"{Directory.GetFiles(output, "*.wav").Length}개 → {output}");

static double[] Reverb(double[] x, int rate)
{
    double scale = rate / 44100.0;
    int[] combs = { 1116, 1277, 1422, 1557 }, alls = { 556, 341 };
    var output = new double[x.Length];
    foreach (int length in combs)
    {
        var buf = new double[(int)(length * scale)]; int idx = 0; double store = 0;
        for (int i = 0; i < x.Length; i++)
        {
            double o = buf[idx];
            store = o * 0.7 + store * 0.3;
            buf[idx] = x[i] * 0.03 + store * 0.84;
            idx = (idx + 1) % buf.Length;
            output[i] += o;
        }
    }
    foreach (int length in alls)
    {
        var buf = new double[(int)(length * scale)]; int idx = 0;
        for (int i = 0; i < x.Length; i++)
        {
            double d = buf[idx];
            buf[idx] = output[i] + d * 0.5;
            idx = (idx + 1) % buf.Length;
            output[i] = d - output[i];
        }
    }
    return output;
}

static double[] Biquad(double[] x, int rate, double f, bool high)
{
    double w = 2 * Math.PI * f / rate, alpha = Math.Sin(w) / (2 * 0.7071), cos = Math.Cos(w), a0 = 1 + alpha;
    double b0 = (high ? (1 + cos) / 2 : (1 - cos) / 2) / a0, b1 = (high ? -(1 + cos) : (1 - cos)) / a0, b2 = b0, a1 = -2 * cos / a0, a2 = (1 - alpha) / a0;
    var y = new double[x.Length]; double x1 = 0, x2 = 0, y1 = 0, y2 = 0;
    for (int i = 0; i < x.Length; i++) { double v = b0 * x[i] + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2; x2 = x1; x1 = x[i]; y2 = y1; y1 = v; y[i] = v; }
    return y;
}

static (float[] x, int rate) Read(string path)
{
    byte[] b = File.ReadAllBytes(path);
    int rate = BitConverter.ToInt32(b, 24), pos = 12, start = 0, len = 0;
    while (pos + 8 <= b.Length)
    {
        string id = System.Text.Encoding.ASCII.GetString(b, pos, 4); int size = BitConverter.ToInt32(b, pos + 4);
        if (id == "data") { start = pos + 8; len = Math.Min(size, b.Length - start); break; }
        pos += 8 + size + (size & 1);
    }
    var x = new float[len / 2];
    for (int i = 0; i < x.Length; i++) x[i] = BitConverter.ToInt16(b, start + i * 2) / 32768f;
    return (x, rate);
}

static void Write(string path, double[] x, double gain, int rate)
{
    using var w = new BinaryWriter(File.Create(path));
    int bytes = x.Length * 2;
    w.Write("RIFF".ToCharArray()); w.Write(36 + bytes); w.Write("WAVE".ToCharArray());
    w.Write("fmt ".ToCharArray()); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
    w.Write("data".ToCharArray()); w.Write(bytes);
    foreach (double v in x) w.Write((short)(Math.Clamp(v * gain, -1, 1) * 32767));
}
