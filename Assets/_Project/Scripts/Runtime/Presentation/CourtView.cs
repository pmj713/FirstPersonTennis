using UnityEngine;
using UnityEngine.Rendering;

namespace FirstPersonTennis
{
    // 코트 바닥·라인·네트를 설정의 코트 크기에 맞춰 만든다. 에디터와 플레이 중 모두,
    // TennisConfig(또는 Court 오브젝트 Inspector)에서 크기를 바꾸면 바로 다시 그린다.
    // 에디터에서 만든 코트는 씬에 저장하지 않고 열 때마다 다시 만든다.
    [ExecuteAlways]
    [DefaultExecutionOrder(-100)]
    public class CourtView : MonoBehaviour
    {
        [SerializeField] TennisConfig config;
        [SerializeField] Material surround;
        [SerializeField] Material courtSurface;
        [SerializeField] Material line;
        [SerializeField] Material net;
        [SerializeField] Material post;

        public TennisConfig Config => config;

        bool dirty;
        Mesh netMesh;
        Renderer groundRenderer, surfaceRenderer;
        MaterialPropertyBlock block;

        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        void OnEnable()
        {
            TennisConfig.Changed += OnConfigChanged;
            Court.SurfaceChanged += ApplySurfaceColors;
            Rebuild();
        }

        void OnDisable()
        {
            TennisConfig.Changed -= OnConfigChanged;
            Court.SurfaceChanged -= ApplySurfaceColors;
            if (!Application.isPlaying) Clear();
        }

        // 지금 코트 종류의 색을 바닥에 입힌다 (재질 에셋은 그대로 둔다)
        void ApplySurfaceColors()
        {
            SurfaceProfile surface = Court.Surface;
            block ??= new MaterialPropertyBlock();
            Tint(surfaceRenderer, surface.courtColor);
            Tint(groundRenderer, surface.surroundColor);
        }

        void Tint(Renderer target, Color color)
        {
            if (target == null) return;
            if (color.a <= 0f)
            {
                target.SetPropertyBlock(null); // 색이 비어 있으면 재질 색 그대로
                return;
            }
            block.Clear();
            block.SetColor(BaseColor, color);
            target.SetPropertyBlock(block);
        }

        void OnConfigChanged(TennisConfig changed)
        {
            if (changed != config) return;
            dirty = true; // OnValidate 안에서는 오브젝트를 만들 수 없어서 다음 Update에서 다시 그린다
#if UNITY_EDITOR
            if (!Application.isPlaying) UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
#endif
        }

        void Update()
        {
            if (!dirty) return;
            dirty = false;
            Rebuild();
        }

        public void Rebuild()
        {
            if (config == null) return;
            config.ApplyGlobals();
            Clear();

            Transform ground = Primitive(PrimitiveType.Plane, "Ground", transform, surround);
            ground.localScale = new Vector3((Court.PlayableHalfWidth + 21f) / 5f, 1f, (Court.HalfLength + 28f) / 5f);
            groundRenderer = ground.GetComponent<Renderer>();

            Transform surface = Flat("CourtSurface", transform, courtSurface, new Vector3(0f, 0.005f, 0f),
                new Vector2(Court.DoublesHalfWidth * 2f, Court.HalfLength * 2f));
            surfaceRenderer = surface.GetComponent<Renderer>();
            BuildLines();
            BuildNet();
            ApplySurfaceColors();

            if (!Application.isPlaying)
            {
                // 에디터용: 씬에 저장하지 않고, 손으로 고쳐도 다시 그리면 사라지므로 편집도 막는다
                foreach (Transform t in GetComponentsInChildren<Transform>(true))
                    if (t != transform) t.gameObject.hideFlags = HideFlags.DontSave | HideFlags.NotEditable;
                netMesh.hideFlags = HideFlags.DontSave;
            }
        }

        void Clear()
        {
            for (int i = transform.childCount - 1; i >= 0; i--) Remove(transform.GetChild(i).gameObject);
            if (netMesh != null) Remove(netMesh);
            netMesh = null;
        }

        void BuildLines()
        {
            Transform lines = new GameObject("Lines").transform;
            lines.SetParent(transform, false);
            const float Y = 0.01f, W = 0.05f, Baseline = 0.1f;
            float length = Court.HalfLength * 2f;
            foreach (float s in new[] { -1f, 1f })
            {
                Flat("Baseline", lines, line, new Vector3(0f, Y, s * (Court.HalfLength - Baseline / 2f)),
                    new Vector2(Court.DoublesHalfWidth * 2f, Baseline));
                Flat("DoublesSideline", lines, line, new Vector3(s * (Court.DoublesHalfWidth - W / 2f), Y, 0f),
                    new Vector2(W, length));
                Flat("SinglesSideline", lines, line, new Vector3(s * (Court.SinglesHalfWidth - W / 2f), Y, 0f),
                    new Vector2(W, length));
                Flat("ServiceLine", lines, line, new Vector3(0f, Y, s * (Court.ServiceLine - W / 2f)),
                    new Vector2(Court.SinglesHalfWidth * 2f, W));
                Flat("CenterMark", lines, line, new Vector3(0f, Y, s * (Court.HalfLength - 0.15f)),
                    new Vector2(W, 0.1f));
            }
            Flat("CenterServiceLine", lines, line, new Vector3(0f, Y, 0f), new Vector2(W, Court.ServiceLine * 2f));
        }

        // 네트: 가운데 0.914m, 기둥 1.07m
        void BuildNet()
        {
            var netGo = new GameObject("Net");
            netGo.transform.SetParent(transform, false);
            float x = Court.NetPostX;
            netMesh = new Mesh { name = "Net" };
            netMesh.vertices = new[]
            {
                new Vector3(-x, 0f, 0f), new Vector3(x, 0f, 0f), new Vector3(x, Court.NetPostHeight, 0f),
                new Vector3(0f, Court.NetCenterHeight, 0f), new Vector3(-x, Court.NetPostHeight, 0f),
            };
            netMesh.triangles = new[] { 0, 4, 3, 0, 3, 1, 1, 3, 2 };
            netMesh.RecalculateNormals();
            netGo.AddComponent<MeshFilter>().sharedMesh = netMesh;
            var netRenderer = netGo.AddComponent<MeshRenderer>();
            netRenderer.sharedMaterial = net;
            netRenderer.shadowCastingMode = ShadowCastingMode.Off;

            float slope = Mathf.Atan2(Court.NetPostHeight - Court.NetCenterHeight, x) * Mathf.Rad2Deg;
            float tapeLength = new Vector2(x, Court.NetPostHeight - Court.NetCenterHeight).magnitude;
            float tapeY = (Court.NetPostHeight + Court.NetCenterHeight) / 2f - 0.025f;
            foreach (float s in new[] { -1f, 1f })
            {
                Transform tape = Primitive(PrimitiveType.Cube, "NetTape", netGo.transform, line);
                tape.localPosition = new Vector3(s * x / 2f, tapeY, 0f);
                tape.localRotation = Quaternion.Euler(0f, 0f, s * slope);
                tape.localScale = new Vector3(tapeLength, 0.05f, 0.03f);

                Transform pole = Primitive(PrimitiveType.Cylinder, "NetPost", netGo.transform, post);
                pole.localPosition = new Vector3(s * x, Court.NetPostHeight / 2f, 0f);
                pole.localScale = new Vector3(0.08f, Court.NetPostHeight / 2f, 0.08f);
            }
        }

        static Transform Primitive(PrimitiveType type, string name, Transform parent, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            Remove(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.GetComponent<Renderer>().sharedMaterial = material;
            return go.transform;
        }

        // 바닥에 눕힌 사각형 (size.x = 너비, size.y = z 방향 길이)
        static Transform Flat(string name, Transform parent, Material material, Vector3 center, Vector2 size)
        {
            Transform t = Primitive(PrimitiveType.Quad, name, parent, material);
            t.localPosition = center;
            t.localRotation = Quaternion.Euler(90f, 0f, 0f);
            t.localScale = new Vector3(size.x, size.y, 1f);
            t.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            return t;
        }

        static void Remove(Object target)
        {
            if (Application.isPlaying)
            {
                if (target is GameObject go) go.SetActive(false);
                Destroy(target);
            }
            else
            {
                DestroyImmediate(target);
            }
        }
    }
}
