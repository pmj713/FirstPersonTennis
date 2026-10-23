namespace FirstPersonTennis
{
    // 복식 서브·리시브 순서. 팀(side)마다 선수 0·1.
    // 서브: 서브권이 다른 팀에서 넘어올 때마다 그 팀은 지난번과 다른 선수가 넣는다 (A1 → B1 → A2 → B2, 타이브레이크도 이어서).
    // 리시브: 선수 0이 듀스 코트, 선수 1이 애드 코트를 경기 내내 맡는다.
    public sealed class DoublesOrder
    {
        readonly int[] turns = new int[2]; // 팀마다 서브권을 받은 횟수
        int lastSide;

        public void Reset()
        {
            turns[0] = turns[1] = 0;
            lastSide = 0;
        }

        // 포인트(서브)마다 부른다. 같은 서브 게임의 다음 포인트·두 번째 서브·렛이면 같은 선수
        public int ServerFor(int side)
        {
            if (side != lastSide)
            {
                turns[I(side)]++;
                lastSide = side;
            }
            return (turns[I(side)] - 1) % 2;
        }

        public static int ReceiverFor(bool deuce) => deuce ? 0 : 1;

        static int I(int side) => side < 0 ? 0 : 1;
    }
}
