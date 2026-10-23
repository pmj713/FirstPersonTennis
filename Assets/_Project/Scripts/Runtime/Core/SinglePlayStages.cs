using UnityEngine;

namespace FirstPersonTennis
{
    // 싱글플레이 단계 도전의 한 단계: AI 난이도 · 코트 · 경기 길이 · 상대 선수
    public readonly struct Stage
    {
        public readonly int Index;
        public readonly AiDifficulty Difficulty;
        public readonly CourtType Court;
        public readonly MatchFormat Format;
        public readonly int OpponentCharacter; // TennisConfig.characters의 번호

        public Stage(int index, AiDifficulty difficulty, CourtType court, MatchFormat format, int opponentCharacter)
        {
            Index = index;
            Difficulty = difficulty;
            Court = court;
            Format = format;
            OpponentCharacter = opponentCharacter;
        }

        public int Number => Index + 1;
        public MatchRules Rules => new MatchRules { format = Format };
    }

    // 난이도 3 × 코트 3 = 9단계. 쉬움 하드 → 쉬움 클레이 → 쉬움 잔디 → 보통 하드 → … → 어려움 잔디 순서로 연다.
    // 경기 길이는 난이도마다 길어진다 (쉬움 타이브레이크, 보통 숏세트, 어려움 1세트).
    // 상대는 코트에 어울리는 선수: 클레이는 수비형, 잔디는 공격형, 하드는 쉬움에서 균형형·그 위로 컨트롤형
    public static class SinglePlayStages
    {
        public const int Count = 9;
        public const int CourtsPerDifficulty = 3;

        const int Balanced = 0, Aggressive = 1, Control = 2, Defensive = 3; // CharacterPreset.DefaultRoster 순서

        public static Stage Get(int index)
        {
            index = Mathf.Clamp(index, 0, Count - 1);
            var difficulty = (AiDifficulty)(index / CourtsPerDifficulty);
            var court = (CourtType)(index % CourtsPerDifficulty);
            return new Stage(index, difficulty, court, FormatFor(difficulty), OpponentFor(difficulty, court));
        }

        public static MatchFormat FormatFor(AiDifficulty difficulty) => difficulty switch
        {
            AiDifficulty.Easy => MatchFormat.TiebreakOnly,
            AiDifficulty.Normal => MatchFormat.ShortSet,
            _ => MatchFormat.OneSet,
        };

        static int OpponentFor(AiDifficulty difficulty, CourtType court) => court switch
        {
            CourtType.Clay => Defensive,
            CourtType.Grass => Aggressive,
            _ => difficulty == AiDifficulty.Easy ? Balanced : Control,
        };
    }

    // 단계 도전 기록: 깬 단계를 이 PC에 저장한다. 1단계는 처음부터 열려 있고, 단계를 이기면 다음 단계가 열린다
    public static class StageProgress
    {
        const string ClearedKey = "SinglePlay.Cleared";     // 깬 단계 비트 (1 << 단계 번호)
        const string CharacterKey = "SinglePlay.Character"; // 마지막으로 고른 내 선수

        public static int ClearedMask
        {
            get => PlayerPrefs.GetInt(ClearedKey, 0);
            private set
            {
                PlayerPrefs.SetInt(ClearedKey, value);
                PlayerPrefs.Save();
            }
        }

        public static bool IsCleared(int index) => index >= 0 && index < SinglePlayStages.Count && (ClearedMask & (1 << index)) != 0;
        public static bool IsUnlocked(int index) => index == 0 || index > 0 && index < SinglePlayStages.Count && IsCleared(index - 1);

        public static int ClearedCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < SinglePlayStages.Count; i++) if (IsCleared(i)) n++;
                return n;
            }
        }

        // 모두 깨면 자유 경기의 경기 모드가 열린다
        public static bool AllCleared => ClearedCount >= SinglePlayStages.Count;

        // 다음에 도전할 단계: 열렸지만 아직 못 깬 첫 단계 (모두 깼으면 마지막 단계)
        public static int NextStage
        {
            get
            {
                for (int i = 0; i < SinglePlayStages.Count; i++)
                    if (IsUnlocked(i) && !IsCleared(i)) return i;
                return SinglePlayStages.Count - 1;
            }
        }

        // 이번에 처음 깼으면 true (다음 단계가 새로 열림)
        public static bool MarkCleared(int index)
        {
            if (index < 0 || index >= SinglePlayStages.Count || IsCleared(index)) return false;
            ClearedMask |= 1 << index;
            return true;
        }

        public static int LastCharacter
        {
            get => PlayerPrefs.GetInt(CharacterKey, 0);
            set
            {
                PlayerPrefs.SetInt(CharacterKey, value);
                PlayerPrefs.Save();
            }
        }

        // 테스트용: 기록을 통째로 읽고 되돌린다
        public static int Snapshot() => ClearedMask;
        public static void Restore(int mask) => ClearedMask = mask;
    }
}
