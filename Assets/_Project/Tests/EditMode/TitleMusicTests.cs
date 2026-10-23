using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FirstPersonTennis.Tests
{
    // 메인 화면 배경음악: 긴 스테레오 곡이 압축된 채로 메모리에 있고, 메인 화면 씬에서 반복 재생되도록 연결되어 있는지
    public class TitleMusicTests
    {
        const string ThemePath = "Assets/_Project/Audio/Music/TitleTheme.wav";

        [Test]
        public void ThemeIsALongStereoTrackKeptCompressed()
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(ThemePath);
            Assert.IsNotNull(clip, "메인 화면 음악이 없음 (ArtSource/Music/TitleTheme.cs로 만든다)");
            Assert.AreEqual(2, clip.channels);
            Assert.That(clip.length, Is.InRange(30f, 150f));
            var importer = (AudioImporter)AssetImporter.GetAtPath(ThemePath);
            Assert.AreEqual(AudioClipLoadType.CompressedInMemory, importer.defaultSampleSettings.loadType, "씬을 다시 만들면 맞춰진다");
        }

        [Test]
        public void TitleScenePlaysTheThemeOnLoop()
        {
            Scene scene = EditorSceneManager.OpenScene("Assets/_Project/Scenes/Title.unity", OpenSceneMode.Additive);
            try
            {
                TitleMusic music = null;
                foreach (GameObject root in scene.GetRootGameObjects())
                    if (music == null) music = root.GetComponentInChildren<TitleMusic>(true);
                Assert.IsNotNull(music, "메인 화면 씬에 TitleMusic이 없음 (씬을 다시 만들어야 함)");
                var source = music.GetComponent<AudioSource>();
                Assert.AreEqual(AssetDatabase.LoadAssetAtPath<AudioClip>(ThemePath), source.clip);
                Assert.IsTrue(source.loop);
                Assert.AreEqual(0f, source.spatialBlend, "배경음악은 2D");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
                ItfDimensionsSetUp.Apply(); // 씬의 코트가 설정 파일의 코트·공 크기를 전역에 적용했다
            }
        }
    }
}
