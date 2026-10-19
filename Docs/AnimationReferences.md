# First Person Tennis 애니메이션 교정

2026-10-16. 프로젝트: `C:/Users/mbc/Documents/Unity_P/FirstPersonTennis`.

## 반영한 내용

- 포핸드·양손 백핸드: 임팩트 후 라켓이 반대쪽 어깨 옆으로 감기도록 샤프트 방향과 마무리 손 위치 조정.
- 포핸드·백핸드 슬라이스: 일반 탑스핀과 분리. 높은 준비 자세에서 앞으로 내려치고 타격 쪽에 라켓을 뻗는 전용 클립 추가. 백핸드는 타격 전 왼손을 놓고 뒤로 벌림.
- 포핸드·백핸드 로브: 낮은 준비 위치에서 위로 들어 올리는 전용 클립 추가. 마무리는 어깨 쪽.
- 포핸드·백핸드 발리: 임팩트 후 지나치게 내려치는 경로를 줄이고 라켓 헤드를 세운 짧은 전방 마무리.
- 서브·스매시: 높은 타점에서 곧바로 낮은 마무리로 보간되던 구간에 전방 확장·회전 자세 추가.
- 슬라이스·킥 서브: 공을 가로질러 지나가는 경로와 위·옆으로 뻗는 경로를 별도 클립으로 구분. 구질 선택을 애니메이터까지 전달.
- 임팩트를 넘는 한 프레임 전체에 빠른 스윙 배속이 적용되던 계산 수정. 남은 프레임 시간은 원래 속도로 팔로스루 진행.
- FBX, Blender 파일, 클립 임포트 설정 및 SwingBody/SwingUpper 상태 연결 저장. 기존 씬은 재생성하지 않음.

## 참고 영상과 설명

아래 영상 페이지의 시범 설명·전사와 포핸드 영상 프레임을 참고해 수작업 핵심 자세를 조정했다. 영상의 3D 모션을 추출하거나 모든 영상을 프레임별 분석한 작업은 아니다.

| 기술 | 참고 | 적용 기준 |
|---|---|---|
| 포핸드·양손 백핸드·발리·서브 | [MIT OCW Tennis 시범 영상](https://ocw.mit.edu/courses/pe-710-tennis-spring-2007/pages/video/) | 반대쪽 어깨 마무리, 짧은 발리, 높은 서브 타점과 반대쪽 골반 마무리 |
| 포핸드 슬라이스 | [Sikana: Forehand Slice](https://www.sikana.tv/en/sport/tennis/forehand-slice) | 높은 준비 자세에서 전방·하방 경로 |
| 백핸드 슬라이스 | [Sikana: Backhand Slice](https://www.sikana.tv/en/sport/tennis/backhand-slice) | 한 손 타격, 반대팔 균형, 옆으로 선 자세에서 전방 마무리 |
| 포핸드 로브 | [Sikana: Forehand Offensive Lob](https://www.sikana.tv/en/sport/tennis/forehand-offensive-lob) | 공 아래에서 상승하는 경로 |
| 백핸드 로브 | [Sikana: Backhand Topspin Lob](https://www.sikana.tv/en/sport/tennis/backhand-lob) | 일반 드라이브와 구분되는 상승 경로 |
| 스매시 | [Howcast: Overhead Smash](https://howcast.com/videos/512329-how-to-hit-an-overhead-smash-tennis-lessons/) | 옆으로 준비, 높은 타점, 반대쪽 마무리 |
| 서브 구질 | [Top Tennis Training: Slice Serve vs Kick Serve](https://www.youtube.com/watch?v=WR-QgYYvu7U) | 가로 방향과 위·옆 방향 타격 경로 구분 |

## 검증 결과와 한계

- Blender에서 타격 12종을 베이크하고 FBX 내보내기 성공.
- 모든 타격 클립의 모든 정수 프레임에서 뼈 행렬이 유한한 값인지 검사 통과.
- 포핸드·백핸드 프레임 24~40에서 오른손 손목이 머리 뼈보다 낮은지 검사 통과. 프레임 미리보기 육안 확인.
- 프로젝트의 기존 컴파일 응답 파일을 사용해 Unity 번들 C# 컴파일러로 런타임·에디터 어셈블리 컴파일 성공. 기존 TennisBall.State 직렬화 경고 1건.
- FBX 신규 클립 6개와 두 레이어의 신규 상태 12개 연결, 중복 ID 없음 확인. Unity 재임포트 결과는 미확인.
- Unity 배치 검증은 Unity.Licensing.Client의 시스템 정보 접근 중 예외로 실패. PlayMode, 공 접촉 정확도, 이동 중 IK와 레이어 혼합은 검증하지 못함.
- 서브 토스는 기존 공유 동작을 유지한다. 게임이 마우스를 놓을 때 구질을 확정하므로 구질별 차이는 타격 구간부터 적용된다. 기존 서브 발사 시점과 시각적 타격 간 지연은 이번 수정에 포함하지 않았다.
- 손가락 뼈가 없는 기존 리그와 손목 각도 제한을 유지하므로 실제 선수 모션을 그대로 재현한 결과는 아니다.

## Unity에서 확인

Unity Hub에서 프로젝트를 열고 에셋 재임포트 후 Match 씬을 재생한다. 연결 갱신이 필요하면 `FirstPersonTennis > Refresh Tennis Animations` 메뉴를 사용한다. 이 메뉴는 경기 씬을 다시 만들지 않고 애니메이션 에셋만 갱신한다.

