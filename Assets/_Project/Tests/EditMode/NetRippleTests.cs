using NUnit.Framework;
using UnityEngine;

namespace FirstPersonTennis.Tests
{
    public class NetRippleTests
    {
        static readonly Vector3 Hit = new Vector3(0f, 0.6f, 0f);
        static float Wave(Vector3 vertex, float age, float speed = 20f) =>
            NetRipple.Deflection(vertex, Hit, speed, age, 6.4f);

        [Test]
        public void ImpactPushesInIncomingDirectionAndScalesWithSpeed()
        {
            Assert.Greater(Wave(Hit, 0.05f), 0f);
            Assert.AreEqual(-Wave(Hit, 0.05f), Wave(Hit, 0.05f, -20f), 1e-6f);
            Assert.Less(Wave(Hit, 0.05f, 10f), Wave(Hit, 0.05f));
        }

        [Test]
        public void PostsAndBottomStayPinned()
        {
            for (float age = 0f; age < 3f; age += 0.025f)
            {
                Assert.AreEqual(0f, Wave(new Vector3(-6.4f, 0.7f, 0f), age), 1e-6f);
                Assert.AreEqual(0f, Wave(new Vector3(6.4f, 0.7f, 0f), age), 1e-6f);
                Assert.AreEqual(0f, Wave(Vector3.zero, age), 1e-6f);
            }
        }

        [Test]
        public void WavePropagatesAndSettlesExactly()
        {
            Vector3 away = Hit + Vector3.right;
            Assert.AreEqual(0f, Wave(away, 0.05f));
            Assert.Greater(Wave(away, 0.18f), 0f);
            Assert.Less(Wave(Hit, 0.25f), 0f); // Rebounds after the first push.
            Assert.Less(Mathf.Abs(Wave(Hit, 2.4f)), 0.001f);
            Assert.AreEqual(0f, Wave(Hit, 2.5f));
            Assert.AreEqual(0f, Wave(Hit, 20f));
        }

        [Test]
        public void ExtremeSpeedsAreBoundedAndInvalidWidthDoesNotMove()
        {
            for (float age = 0f; age < 3f; age += 0.01f)
                Assert.LessOrEqual(Mathf.Abs(Wave(Hit, age, 10000f)), 0.32f);
            Assert.AreEqual(0f, NetRipple.Deflection(Hit, Hit, 20f, 0.1f, 0f));
            Assert.AreEqual(0f, Wave(Hit, -0.1f));
        }
    }
}
