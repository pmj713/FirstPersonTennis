using NUnit.Framework;

namespace FirstPersonTennis.Tests
{
    // UI 배율: 1280×720 ~ 1920×1080은 그대로(1), 작거나 비율이 다른 화면은 줄이고, 큰 화면은 키운다.
    // 어떤 크기·비율에서도 UI 좌표로 본 화면은 1280×720 이상이라 배치가 들어간다
    public class UiScaleTests
    {
        [TestCase(1920, 1080, 1f)]
        [TestCase(1280, 720, 1f)]
        [TestCase(1600, 900, 1f)]
        [TestCase(1024, 768, 0.8f)]   // 4:3
        [TestCase(960, 540, 0.75f)]   // 작은 창
        [TestCase(2560, 1080, 1f)]    // 21:9 (높이 1080)
        [TestCase(2560, 1440, 1.3333f)]
        [TestCase(3840, 2160, 2f)]
        public void ScaleKeepsTheUsualSizesAndFitsTheRest(int width, int height, float expected)
        {
            Assert.AreEqual(expected, HudSkin.ScaleFor(width, height), 1e-3f);
        }

        [Test]
        public void UiScreenIsAlwaysAtLeast1280By720()
        {
            foreach (int width in new[] { 640, 800, 1024, 1280, 1366, 1440, 1680, 1920, 2560, 3440, 3840, 5120 })
            foreach (int height in new[] { 480, 600, 720, 768, 900, 1050, 1080, 1200, 1440, 1600, 2160 })
            {
                float scale = HudSkin.ScaleFor(width, height);
                Assert.GreaterOrEqual(width / scale, HudSkin.MinWidth - 0.01f, $"{width}x{height}");
                Assert.GreaterOrEqual(height / scale, HudSkin.MinHeight - 0.01f, $"{width}x{height}");
            }
        }
    }
}
