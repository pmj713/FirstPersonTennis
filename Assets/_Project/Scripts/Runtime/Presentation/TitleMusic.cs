using UnityEngine;

namespace FirstPersonTennis
{
    // 메인 화면 배경음악: 처음 몇 초 동안 서서히 커지고, 끝없이 반복된다 (곡 자체가 끝과 처음이 이어지게 만들어져 있다.
    // 원본: ArtSource/Music/TitleTheme.cs). 크기는 설정 화면의 배경음악 크기를 바로 따른다
    [RequireComponent(typeof(AudioSource))]
    public class TitleMusic : MonoBehaviour
    {
        [SerializeField, Range(0f, 1f)] float volume = 0.5f; // 설정이 100%일 때
        [SerializeField] float fadeIn = 2.5f;

        AudioSource source;
        float elapsed;

        public float Volume => source != null ? source.volume : 0f;
        public float TargetVolume => volume * UserSettings.MusicVolume;

        void Awake()
        {
            source = GetComponent<AudioSource>();
            source.loop = true;
            source.spatialBlend = 0f;
            source.volume = 0f;
        }

        void Start()
        {
            if (source.clip != null && !source.isPlaying) source.Play();
        }

        void Update()
        {
            if (elapsed < fadeIn) elapsed += Time.unscaledDeltaTime;
            source.volume = TargetVolume * Mathf.SmoothStep(0f, 1f, elapsed / fadeIn);
        }
    }
}
