using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FirstPersonTennis.Tests
{
    // 효과음 녹음(Audio/Hits, Audio/Bounces, Audio/Grunts): 종류마다 여러 개가 있고, 짧은 모노 소리이며, 경기 씬에 연결되어 있는지
    public class HitSampleTests
    {
        const string Dir = "Assets/_Project/Audio";

        [TestCase("Hits/Drive")]
        [TestCase("Hits/Slice")]
        [TestCase("Hits/Serve")]
        [TestCase("Hits/Volley")]
        [TestCase("Bounces")]
        public void EachSoundKindHasSeveralShortRecordings(string folder)
        {
            string[] guids = AssetDatabase.FindAssets("t:AudioClip", new[] { $"{Dir}/{folder}" });
            Assert.GreaterOrEqual(guids.Length, 3, $"{folder} 녹음이 모자람");
            foreach (string guid in guids)
            {
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(guid));
                Assert.Less(clip.length, 0.5f, clip.name);
                Assert.AreEqual(1, clip.channels, $"{clip.name}: 3D로 틀 소리라 모노");
            }
        }

        [Test]
        public void GruntsAreShortMonoRecordings()
        {
            string[] guids = AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/_Project/Audio/Grunts" });
            Assert.GreaterOrEqual(guids.Length, 10, "기합 녹음이 모자람");
            foreach (string guid in guids)
            {
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(guid));
                Assert.Less(clip.length, 1f, clip.name);
                Assert.AreEqual(1, clip.channels, $"{clip.name}: 친 자리에서 3D로 틀 소리라 모노");
            }
        }

        // 기합은 힘을 쓰는 샷에만: 서브·스매시는 거의 늘, 드라이브는 빠를 때만, 슬라이스·로브·발리는 없음
        [Test]
        public void GruntsOnlyOnPowerShots()
        {
            Assert.Greater(AudioFeedback.GruntChance(HitSoundKind.Serve, 0.5f), 0.7f);
            Assert.Greater(AudioFeedback.GruntChance(HitSoundKind.Smash, 0.9f), 0.7f);
            Assert.AreEqual(0f, AudioFeedback.GruntChance(HitSoundKind.Drive, 0.3f), "느린 드라이브");
            Assert.Greater(AudioFeedback.GruntChance(HitSoundKind.Drive, 0.9f), AudioFeedback.GruntChance(HitSoundKind.Drive, 0.5f));
            foreach (HitSoundKind kind in new[] { HitSoundKind.Slice, HitSoundKind.Lob, HitSoundKind.Volley })
                Assert.AreEqual(0f, AudioFeedback.GruntChance(kind, 1f), kind.ToString());
        }

        [Test]
        public void MatchSceneUsesTheRecordings()
        {
            Scene scene = EditorSceneManager.OpenScene("Assets/_Project/Scenes/Match.unity", OpenSceneMode.Additive);
            try
            {
                AudioFeedback audio = null;
                foreach (GameObject root in scene.GetRootGameObjects())
                    if (audio == null) audio = root.GetComponentInChildren<AudioFeedback>(true);
                Assert.IsNotNull(audio, "경기 씬에 AudioFeedback이 없음");
                var so = new SerializedObject(audio);
                foreach (string field in new[] { "driveHits", "sliceHits", "serveHits", "volleyHits", "bounceClips", "grunts" })
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
