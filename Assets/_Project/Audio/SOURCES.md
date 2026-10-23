# 소리 출처

효과음·관중 소리는 모두 Freesound의 **CC0 (퍼블릭 도메인)** 녹음이다 (배경음악은 직접 만든 것, 맨 아래). 상업적 이용·수정·배포에 허락이나 출처 표기가 필요 없지만, 고마움의 뜻으로 적어 둔다.
Freesound 공개 미리듣기(128kbps MP3)를 받아, 타구음·바운드는 한 타씩 잘라 90Hz 아래 잡음(바람·차 소리)을 걷어 내고 크기를 맞춘 48kHz 모노 WAV로 만들었다 (2026-10-22).

## 라켓 타구음 (Hits)

| 폴더 | 원본 | 녹음자 |
| --- | --- | --- |
| Hits/Drive | [Tennis Forehand 2](https://freesound.org/people/jamesdrake89/sounds/662263/) (6타), [Tennis Ball being hit](https://freesound.org/people/jacklilley/sounds/338122/) (3타) | jamesdrake89, jacklilley |
| Hits/Slice | [Tennis Slices 1](https://freesound.org/people/jamesdrake89/sounds/662270/), [2](https://freesound.org/people/jamesdrake89/sounds/662269/), [4](https://freesound.org/people/jamesdrake89/sounds/662268/) | jamesdrake89 |
| Hits/Serve | [Tennis Serves 1](https://freesound.org/people/jamesdrake89/sounds/662265/), [Serve 2](https://freesound.org/people/jamesdrake89/sounds/662264/), [Serves 3](https://freesound.org/people/jamesdrake89/sounds/662266/) | jamesdrake89 |
| Hits/Volley | Drive 녹음을 70ms로 짧게 자른 것 (발리 녹음은 없음) | — |

쓰는 곳: `AudioFeedback` — 드라이브·로브는 Drive, 슬라이스·드롭샷은 Slice, 서브·스매시는 Serve, 발리는 Volley. 폴더가 비면 코드로 만든 소리(`RacketSound`)를 쓴다.

## 바운드 (Bounces)

| 폴더 | 원본 | 녹음자 |
| --- | --- | --- |
| Bounces | [Tennis Bounce Ball on Grass](https://freesound.org/people/jamesdrake89/sounds/662258/) (22번) | jamesdrake89 |

잔디 녹음 하나뿐이라 코트마다 재생 높이·크기를 바꾼다: 하드는 높고 또렷하게, 클레이는 낮고 부드럽게, 잔디는 그대로.

씬 빌더(`MatchSceneBuilder`)가 이 폴더들의 소리를 경기 씬에 연결하므로, 파일을 더 넣으면 씬을 다시 만들면 된다.

## 관중 (Crowd)

| 폴더 | 원본 | 녹음자 |
| --- | --- | --- |
| Crowd/ApplauseSmall·Medium·Big | [tennis match](https://freesound.org/people/huubjeroen/sounds/114052/) (ATP 로테르담 실내 대회 중계용 녹음, 5개), [applause](https://freesound.org/people/thedapperdan/sounds/26807/) (공연장 큰 박수, 4개), [Applause_encore](https://freesound.org/people/Zihris/sounds/324369/) (콘서트 앙코르, 2개), [small_applause](https://freesound.org/people/Sclolex/sounds/261617/) (결혼식 하객, 2개), [Golf Crowd Applauds](https://freesound.org/people/craigsmith/sounds/479967/) (골프 관중, 1개) | huubjeroen, thedapperdan, Zihris, Sclolex, craigsmith |
| Crowd/Cheer | [Large crowd applause](https://freesound.org/people/Bansemer/sounds/160493/) (2개) | Bansemer |
| Crowd/Ambience | 이 프로젝트에서 합성 (낮은 바람 + 먼 웅성임). 원본 코드 `ArtSource/Audio/Crowd/StadiumAir.cs` | — |
| Crowd/Chatter | [Crowd Talking Quietly Stadium](https://freesound.org/people/FunWithSound/sounds/381373/) (고리) | FunWithSound |

박수는 랠리가 끝난 뒤 손뼉이 시작되는 곳부터 자르고, 한 사람 목소리(심판 점수 콜·선수 외침·기합)가 조금이라도 의심되는 클립은 뺐다. 목소리는 음높이가 또렷한 구간(80~600Hz 주기)을 찾아 확인했다 (2026-10-23). 처음에는 신시내티 오픈 야외 녹음([treytatum3](https://freesound.org/people/treytatum3/sounds/819358/))도 썼지만 여자 선수 기합·심판 콜이 섞여 모두 뺐고, 경기 녹음에서 자른 경기장 공기 루프도 심판의 점수 콜이 섞인 것으로 보여 합성음으로 바꿨다. 48kHz 스테레오, 60Hz 아래를 걷어 내고 평균 크기를 맞췄다.
쓰는 곳: `CrowdAudio` — 포인트가 끝나면 박수(범실 작게, 위너·에이스 보통, 긴 랠리 크게, 더블 폴트 없음), 세트·경기를 따면 환호, 포인트 사이 웅성임.

## 선수 기합 (Grunts)

| 파일 | 원본 | 녹음자 |
| --- | --- | --- |
| Grunts/grunt_511023_*.wav (24개) | [Male Fight Grunts](https://freesound.org/people/ale-batec/sounds/511023/) | ale-batec |
| Grunts/grunt_661242_00.wav | [Swing grunt](https://freesound.org/people/DeqstersLab/sounds/661242/) | DeqstersLab |
| Grunts/grunt_464485_00.wav | [Male Attack Grunt](https://freesound.org/people/elynch0901/sounds/464485/) | elynch0901 |
| Grunts/grunt_536751_*.wav (2개) | [GruntM](https://freesound.org/people/egomassive/sounds/536751/) | egomassive |

한 번씩 잘라(갑자기 커지는 곳부터 최고점보다 30dB 내려갈 때까지) 48kHz 모노로, 90Hz 아래를 걷고 최고점을 맞췄다 (2026-10-23).
쓰는 곳: `AudioFeedback` — 서브·스매시는 거의 늘, 세게 친 드라이브는 가끔, 슬라이스·로브·발리는 없음. 친 자리에서 3D로, 내 기합은 작게.

## 배경음악 (Music)

| 파일 | 출처 |
| --- | --- |
| Music/TitleTheme.wav | 이 프로젝트에서 직접 작곡·합성 (녹음·외부 음원 없음). 원본 코드 `ArtSource/Music/TitleTheme.cs` |

메인 화면 음악: D장조 104BPM 32마디(약 74초), 끝과 처음이 이어져 끊김 없이 반복된다. 고치려면 원본 코드를 고쳐 그 폴더에서 `dotnet run TitleTheme.cs`로 다시 만들고 메인 화면 씬을 다시 만든다.

## 심판 콜 (Umpire)

| 파일 | 출처 |
| --- | --- |
| Umpire/*.wav (105개) | 이 프로젝트에서 만든 것. Windows 내장 음성 합성(Microsoft Zira, 영어)으로 읽고 경기장 확성기처럼 다듬었다. 원본 `ArtSource/Audio/Umpire` |

파일 이름이 콜 이름이다: `point_서버_리시버`(0 love, 1 fifteen, 2 thirty, 3 forty), `deuce`, `advantage_server`/`advantage_receiver`, `game`, `game_tiebreak`, `game_set`, `game_set_match`, `fault`, `out`, `let_first`/`let_second`, 타이브레이크 `tb_서버_리시버`(0~7점), 그 위는 `num_숫자` 둘 또는 `num_숫자` + `word_all`.
다시 만들려면 그 폴더에서 `powershell -NoProfile -File make_umpire_tts.ps1 <임시 폴더>` → `dotnet run UmpireFx.cs -- <임시 폴더> ../../../Assets/_Project/Audio/Umpire`.
