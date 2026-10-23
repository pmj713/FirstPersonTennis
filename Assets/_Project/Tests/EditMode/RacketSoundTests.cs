using NUnit.Framework;
using UnityEngine;

namespace FirstPersonTennis.Tests
{
    // 라켓 타구음: 구질별로 소리가 만들어지고, 막 친 공의 상태로 알맞은 소리를 고르는지
    public class RacketSoundTests
    {
        static readonly SurfaceProfile Hard = SurfaceProfile.Hard;

        [Test]
        public void EveryKindMakesAShortCleanSound()
        {
            int expected = Mathf.CeilToInt(RacketSound.SampleRate * RacketSound.Duration);
            foreach (HitSoundKind kind in System.Enum.GetValues(typeof(HitSoundKind)))
            {
                float[] a = RacketSound.Synthesize(kind, 0), b = RacketSound.Synthesize(kind, 1);
                Assert.AreEqual(expected, a.Length, kind.ToString());
                float peak = 0f;
                foreach (float s in a)
                {
                    Assert.IsFalse(float.IsNaN(s), kind.ToString());
                    peak = Mathf.Max(peak, Mathf.Abs(s));
                }
                Assert.That(peak, Is.InRange(0.5f, 1f), $"{kind} 소리 크기");
                Assert.AreEqual(0f, a[a.Length - 1], 1e-4f, $"{kind} 끝이 뚝 끊기지 않게 0으로");
                Assert.AreNotEqual(a[200], b[200], $"{kind} 변형마다 다르다");
            }
            Assert.AreEqual(RacketSound.Synthesize(HitSoundKind.Drive, 2)[300], RacketSound.Synthesize(HitSoundKind.Drive, 2)[300], "같은 변형은 같은 소리");
        }

        [Test]
        public void VolleyIsShorterThanADrive()
        {
            Assert.Less(Tail(RacketSound.Synthesize(HitSoundKind.Volley, 0)), Tail(RacketSound.Synthesize(HitSoundKind.Drive, 0)));
        }

        [Test]
        public void SoundFollowsTheShotJustHit()
        {
            var from = new Vector3(0.5f, 1f, -12.3f);
            ShotSolution topspin = ShotSolver.SolveDrive(from, new Vector3(0f, 0f, 9f), 32f, 160f, Hard);
            ShotSolution slice = ShotSolver.SolveDrive(from, new Vector3(0f, 0f, 9f), 28f, -150f, Hard);
            ShotSolution lob = ShotSolver.SolveFixedAngle(from, new Vector3(0f, 0f, 10f), 50f, 120f, Hard);
            Assert.AreEqual(HitSoundKind.Drive, RacketSound.Classify(new BallState(from, topspin.Velocity, topspin.Spin), false, false));
            Assert.AreEqual(HitSoundKind.Slice, RacketSound.Classify(new BallState(from, slice.Velocity, slice.Spin), false, false));
            Assert.AreEqual(HitSoundKind.Lob, RacketSound.Classify(new BallState(from, lob.Velocity, lob.Spin), false, false));
            Assert.AreEqual(HitSoundKind.Volley, RacketSound.Classify(new BallState(from, topspin.Velocity, topspin.Spin), false, true));
            Assert.AreEqual(HitSoundKind.Serve, RacketSound.Classify(new BallState(new Vector3(0f, 2.8f, -12f), new Vector3(0f, -3f, 40f), Vector3.zero), true, false));

            var high = new Vector3(0.3f, 2.7f, -4f);
            ShotSolution smash = ShotSolver.SolveDrive(high, new Vector3(1f, 0f, 8f), 34f, 40f, Hard, -35f);
            Assert.AreEqual(HitSoundKind.Smash, RacketSound.Classify(new BallState(high, smash.Velocity, smash.Spin), false, false));
        }

        // 크기가 최대의 1% 아래로 떨어지는 마지막 샘플
        static int Tail(float[] s)
        {
            float peak = 0f;
            foreach (float v in s) peak = Mathf.Max(peak, Mathf.Abs(v));
            int last = 0;
            for (int i = 0; i < s.Length; i++) if (Mathf.Abs(s[i]) > peak * 0.01f) last = i;
            return last;
        }
    }
}
