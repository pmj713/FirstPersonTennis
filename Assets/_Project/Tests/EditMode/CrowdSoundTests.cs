using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FirstPersonTennis.Tests
{
    // 관중 소리: 포인트에 따른 박수 크기, 녹음(Audio/Crowd), 경기 씬 연결
    public class CrowdSoundTests
    {
        const string Dir = "Assets/_Project/Audio/Crowd";

        static int Level(PointReason reason, int shots) => CrowdAudio.ApplauseLevel(CrowdAudio.ApplauseSize(reason, shots));

        [Test]
        public void DoubleFaultGetsNoApplause()
        {
            Assert.AreEqual(0, Level(PointReason.DoubleFault, 1));
            Assert.AreEqual(0, Level(PointReason.DoubleFault, 2));
        }

        [Test]
        public void ShortErrorIsPoliteWinnersAndAcesAreLouder()
        {
            Assert.AreEqual(1, Level(PointReason.Out, 3), "짧은 랠리 범실은 작게");
            Assert.AreEqual(1, Level(PointReason.Net, 2));
            Assert.AreEqual(2, Level(PointReason.DoubleBounce, 3), "위너는 보통");
            Assert.AreEqual(2, Level(PointReason.Ace, 1), "에이스는 보통");
        }

        [Test]
        public void LongRalliesGetBigApplause()
        {
            Assert.Greater(CrowdAudio.ApplauseSize(PointReason.Out, 12), CrowdAudio.ApplauseSize(PointReason.Out, 4));
            Assert.AreEqual(3, Level(PointReason.Out, 15), "길게 주고받은 끝의 범실도 크게");
            Assert.AreEqual(3, Level(PointReason.DoubleBounce, 10), "긴 랠리의 위너는 크게");
        }

        [TestCase("Ambience", 2, 8f, 30f)]
        [TestCase("Chatter", 1, 8f, 30f)]
        [TestCase("ApplauseSmall", 3, 2f, 5f)]
        [TestCase("ApplauseMedium", 3, 3f, 8f)]
        [TestCase("ApplauseBig", 3, 5f, 10f)]
        [TestCase("Cheer", 1, 4f, 12f)]
        public void EachCrowdKindHasStereoRecordings(string folder, int atLeast, float minLength, float maxLength)
        {
            string[] guids = AssetDatabase.FindAssets("t:AudioClip", new[] { $"{Dir}/{folder}" });
            Assert.GreaterOrEqual(guids.Length, atLeast, $"{folder} 녹음이 모자람");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                Assert.AreEqual(2, clip.channels, $"{clip.name}: 경기장을 둘러싼 소리라 스테레오");
                Assert.That(clip.length, Is.InRange(minLength, maxLength), clip.name);
                var importer = (AudioImporter)AssetImporter.GetAtPath(path);
                Assert.AreEqual(AudioClipLoadType.CompressedInMemory, importer.defaultSampleSettings.loadType, $"{clip.name}: 씬을 다시 만들면 맞춰진다");
            }
        }

        [Test]
        public void MatchSceneHasTheCrowd()
        {
            Scene scene = EditorSceneManager.OpenScene("Assets/_Project/Scenes/Match.unity", OpenSceneMode.Additive);
            try
            {
                CrowdAudio crowd = null;
                foreach (GameObject root in scene.GetRootGameObjects())
                    if (crowd == null) crowd = root.GetComponentInChildren<CrowdAudio>(true);
                Assert.IsNotNull(crowd, "경기 씬에 CrowdAudio가 없음 (씬을 다시 만들어야 함)");
                var so = new SerializedObject(crowd);
                Assert.IsNotNull(so.FindProperty("referee").objectReferenceValue);
                foreach (string field in new[] { "ambience", "chatter", "applauseSmall", "applauseMedium", "applauseBig", "cheers" })
                    Assert.Greater(so.FindProperty(field).arraySize, 0, $"{field}가 비어 있음 (씬을 다시 만들어야 함)");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
                ItfDimensionsSetUp.Apply(); // 씬의 공이 설정 파일의 코트·공 크기를 전역에 적용했다
            }
        }
    }
}
