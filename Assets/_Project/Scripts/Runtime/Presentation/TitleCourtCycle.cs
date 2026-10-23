using UnityEngine;

namespace FirstPersonTennis
{
    // 메인 화면 배경: 일정 시간(기본 10초)마다 코트를 하드 → 클레이 → 잔디 → 하드로 바꾼다.
    // 바꾸기 직전 화면(메뉴 없이)을 찍어 두고 그 위에 겹쳐 그린 뒤 천천히 투명하게 해서 디졸브로 넘어간다
    public class TitleCourtCycle : MonoBehaviour
    {
        [SerializeField] TennisConfig config;
        [SerializeField] Camera cam;
        [SerializeField] float interval = 10f;
        [SerializeField] float fadeSeconds = 1.6f;

        public int Switches { get; private set; }
        public float Fade => ghostAlpha; // 1 = 이전 코트, 0 = 새 코트

        float timer;
        RenderTexture ghost;
        float ghostAlpha;

        void Start() => config.SelectCourt(CourtType.Hard);

        void Update()
        {
            timer += Time.unscaledDeltaTime;
            if (timer >= interval)
            {
                timer -= interval;
                Capture();
                config.SelectCourt((CourtType)(((int)Court.Type + 1) % 3));
                Switches++;
            }
            if (ghostAlpha > 0f) ghostAlpha = Mathf.Max(0f, ghostAlpha - Time.unscaledDeltaTime / fadeSeconds);
        }

        // 지금 카메라 화면을 한 장 더 그려 둔다 (화면에 보인 마지막 프레임과 같은 자리)
        void Capture()
        {
            if (cam == null) return;
            int w = Mathf.Max(1, Screen.width), h = Mathf.Max(1, Screen.height);
            if (ghost == null || ghost.width != w || ghost.height != h)
            {
                Release();
                ghost = new RenderTexture(w, h, 24) { name = "TitleCourtGhost" };
            }
            RenderTexture previous = cam.targetTexture;
            cam.targetTexture = ghost;
            cam.Render();
            cam.targetTexture = previous;
            ghostAlpha = 1f;
        }

        void OnGUI()
        {
            if (ghost == null || ghostAlpha <= 0f || Event.current.type != EventType.Repaint) return;
            GUI.depth = 10; // 메뉴(0)보다 뒤에 그린다
            Color color = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, Mathf.SmoothStep(0f, 1f, ghostAlpha));
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), ghost, ScaleMode.StretchToFill, false);
            GUI.color = color;
        }

        void OnDestroy() => Release();

        void Release()
        {
            if (ghost == null) return;
            ghost.Release();
            Destroy(ghost);
            ghost = null;
        }
    }
}
