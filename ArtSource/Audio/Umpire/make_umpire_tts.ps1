# 심판 콜 음성 합성 (Windows 내장 음성 Microsoft Zira, 영어). 결과는 손보지 않은 22.05kHz 모노 WAV
# 실행 (Windows PowerShell 5.1): powershell -NoProfile -File make_umpire_tts.ps1 <출력 폴더>
# 다음 단계: dotnet run UmpireFx.cs -- <출력 폴더> ../../../Assets/_Project/Audio/Umpire  (확성기 느낌·잔향·크기 맞춤)
param([string]$Out = "tts")
Add-Type -AssemblyName System.Speech
New-Item -ItemType Directory -Force $Out | Out-Null
$synth = New-Object System.Speech.Synthesis.SpeechSynthesizer
$synth.SelectVoice("Microsoft Zira Desktop")
$format = New-Object System.Speech.AudioFormat.SpeechAudioFormatInfo(22050, [System.Speech.AudioFormat.AudioBitsPerSample]::Sixteen, [System.Speech.AudioFormat.AudioChannel]::Mono)

# 이름 = 문장. 점수는 서버 점수를 먼저 부른다 (경기 화면의 점수 콜과 같은 순서)
$words = @{ 0 = "love"; 1 = "fifteen"; 2 = "thirty"; 3 = "forty" }
$calls = [ordered]@{}
for ($s = 0; $s -le 3; $s++) {
    for ($r = 0; $r -le 3; $r++) {
        if ($s -eq 0 -and $r -eq 0) { continue }
        if ($s -eq 3 -and $r -eq 3) { continue }
        $text = if ($s -eq $r) { "$($words[$s]) all" } else { "$($words[$s]), $($words[$r])" }
        $calls["point_${s}_${r}"] = $text
    }
}
$calls["deuce"] = "deuce"
$calls["advantage_server"] = "advantage, server"
$calls["advantage_receiver"] = "advantage, receiver"
$calls["game"] = "game"
$calls["game_tiebreak"] = "game. tie-break"
$calls["game_set"] = "game and set"
$calls["game_set_match"] = "game, set, and match"
$calls["fault"] = "fault"
$calls["out"] = "out!"
$calls["let_first"] = "let. first service"
$calls["let_second"] = "let. second service"

# 타이브레이크 점수 (0~7점 조합, 그 위는 숫자 둘을 이어 부른다)
$numbers = @("zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve", "thirteen", "fourteen", "fifteen")
for ($a = 0; $a -le 7; $a++) {
    for ($b = 0; $b -le 7; $b++) {
        if ($a -eq 0 -and $b -eq 0) { continue }
        $text = if ($a -eq $b) { "$($numbers[$a]) all" } else { "$($numbers[$a]), $($numbers[$b])" }
        $calls["tb_${a}_${b}"] = $text
    }
}
for ($n = 0; $n -lt $numbers.Length; $n++) { $calls["num_$n"] = $numbers[$n] }
$calls["word_all"] = "all"

foreach ($name in $calls.Keys) {
    $ssml = "<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xml:lang='en-US'><prosody rate='+5%' pitch='-2st'>$($calls[$name])</prosody></speak>"
    $synth.SetOutputToWaveFile((Join-Path (Resolve-Path $Out) "$name.wav"), $format)
    $synth.SpeakSsml($ssml)
}
$synth.SetOutputToNull()
"$($calls.Count) calls -> $Out"
