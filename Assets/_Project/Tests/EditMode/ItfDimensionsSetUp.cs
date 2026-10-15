using NUnit.Framework;

namespace FirstPersonTennis.Tests
{
    // EditMode 테스트는 ITF 규격 코트와 실제 공 크기를 기준으로 한다.
    // 에디터에서 다른 크기로 실험 중이어도 결과가 흔들리지 않도록 테스트 전에 되돌린다.
    [SetUpFixture]
    public class ItfDimensionsSetUp
    {
        [OneTimeSetUp]
        public void UseItfDimensions()
        {
            Court.Configure(1f, 1f);
            Court.SetSurface(CourtType.Hard, SurfaceProfile.Hard);
            BallPhysics.SetRadius(BallPhysics.RealRadius);
        }
    }
}
