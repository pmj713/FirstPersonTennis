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
        const int NetColumns = 80, NetRows = 8;
        const float RippleDuration = NetRipple.Duration;
        Vector3[] netRest, netVertices;
        TennisBall observedBall;
        struct Impact { public Vector3 Point; public float Speed, Started; }
        readonly Impact[] impacts = new Impact[4];
        int nextImpact;
        bool netMoving;
        Renderer groundRenderer, surfaceRenderer;
        MaterialPropertyBlock block;

        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        void OnEnable()
        {
            TennisConfig.Changed += OnConfigChanged;
            Court.SurfaceChanged += ApplySurfaceColors;
            Rebuild();
            if (Application.isPlaying) BindBall();
        }

        void Start() { if (Application.isPlaying) BindBall(); }

        void BindBall()
        {
            if (observedBall != null) return;
            foreach (var ball in FindObjectsByType<TennisBall>())
            {
                if (ball.gameObject.scene != gameObject.scene) continue;
                observedBall = ball;
                observedBall.NetImpact += OnNetImpact;
                break;
            }
        }

        void OnNetImpact(Vector3 point, Vector3 velocity)
        {
            impacts[nextImpact] = new Impact { Point = transform.InverseTransformPoint(point),
                Speed = transform.InverseTransformVector(velocity).z, Started = Time.time };
            nextImpact = (nextImpact + 1) % impacts.Length;
            netMoving = true;
        }

        void OnDisable()
        {
            TennisConfig.Changed -= OnConfigChanged;
            Court.SurfaceChanged -= ApplySurfaceColors;
            if (observedBall != null) observedBall.NetImpact -= OnNetImpact;
            observedBall = null;
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
            if (dirty) { dirty = false; Rebuild(); }
            if (Application.isPlaying && netMoving && netMesh != null) AnimateNet();
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
            netRest = netVertices = null;
            netMoving = false;
            System.Array.Clear(impacts, 0, impacts.Length);
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
            int gridCount = (NetColumns + 1) * (NetRows + 1);
            netRest = new Vector3[gridCount + (NetColumns + 1) * 4];
            var triangles = new int[NetColumns * NetRows * 6];
            var tapeTriangles = new int[NetColumns * 12];
            for (int column = 0; column <= NetColumns; column++)
            {
                float px = Mathf.Lerp(-x, x, column / (float)NetColumns);
                float height = Court.NetHeightAt(px);
                for (int row = 0; row <= NetRows; row++)
                    netRest[column * (NetRows + 1) + row] = new Vector3(px, height * row / NetRows, 0f);
                for (int face = 0; face < 2; face++)
                {
                    int t = gridCount + column * 4 + face * 2;
                    netRest[t] = new Vector3(px, height - 0.05f, face == 0 ? -0.006f : 0.006f);
                    netRest[t + 1] = new Vector3(px, height, netRest[t].z);
                }
                if (column == NetColumns) continue;
                for (int row = 0; row < NetRows; row++)
                {
                    int v = column * (NetRows + 1) + row, next = v + NetRows + 1;
                    int t = (column * NetRows + row) * 6;
                    triangles[t] = v; triangles[t + 1] = v + 1; triangles[t + 2] = next;
                    triangles[t + 3] = next; triangles[t + 4] = v + 1; triangles[t + 5] = next + 1;
                }
                for (int face = 0; face < 2; face++)
                {
                    int v = gridCount + column * 4 + face * 2, t = column * 12 + face * 6;
                    tapeTriangles[t] = v; tapeTriangles[t + 1] = face == 0 ? v + 1 : v + 4;
                    tapeTriangles[t + 2] = face == 0 ? v + 4 : v + 1;
                    tapeTriangles[t + 3] = v + 4; tapeTriangles[t + 4] = face == 0 ? v + 1 : v + 5;
                    tapeTriangles[t + 5] = face == 0 ? v + 5 : v + 1;
                }
            }
            netVertices = (Vector3[])netRest.Clone();
            netMesh = new Mesh { name = "Net", subMeshCount = 2 };
            netMesh.MarkDynamic();
            netMesh.vertices = netVertices;
            netMesh.SetTriangles(triangles, 0);
            netMesh.SetTriangles(tapeTriangles, 1);
            netMesh.RecalculateNormals();
            netGo.AddComponent<MeshFilter>().sharedMesh = netMesh;
            var netRenderer = netGo.AddComponent<MeshRenderer>();
            netRenderer.sharedMaterials = new[] { net, line };
            netRenderer.shadowCastingMode = ShadowCastingMode.Off;

            foreach (float s in new[] { -1f, 1f })
            {
                Transform pole = Primitive(PrimitiveType.Cylinder, "NetPost", netGo.transform, post);
                pole.localPosition = new Vector3(s * x, Court.NetPostHeight / 2f, 0f);
                pole.localScale = new Vector3(0.08f, Court.NetPostHeight / 2f, 0.08f);
            }
        }

        void AnimateNet()
        {
            bool active = false;
            for (int i = 0; i < impacts.Length; i++)
                active |= impacts[i].Speed != 0f && Time.time - impacts[i].Started < RippleDuration;
            for (int v = 0; v < netVertices.Length; v++)
            {
                Vector3 rest = netRest[v];
                float offset = 0f;
                for (int i = 0; i < impacts.Length; i++)
                    offset += NetRipple.Deflection(rest, impacts[i].Point, impacts[i].Speed,
                        Time.time - impacts[i].Started, Court.NetPostX);
                netVertices[v] = rest + Vector3.forward * Mathf.Clamp(offset, -0.4f, 0.4f);
            }
            netMesh.vertices = netVertices;
            netMesh.RecalculateNormals();
            netMesh.RecalculateBounds();
            netMoving = active;
        }

        void OnDestroy() { if (netMesh != null) Remove(netMesh); }

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

    public static class NetRipple
    {
        public const float Duration = 2.5f;
        // Localized damped wave; posts and the bottom edge remain attached.
        public static float Deflection(Vector3 vertex, Vector3 impact, float speed, float age, float halfWidth)
        {
            if (age <= 0f || age >= Duration || halfWidth <= 0f) return 0f;
            float edge = Mathf.Clamp01(1f - Mathf.Abs(vertex.x) / halfWidth);
            float anchored = Mathf.Sin(edge * Mathf.PI * 0.5f) * Mathf.Clamp01(vertex.y / 0.25f);
            float dx = vertex.x - impact.x, dy = vertex.y - impact.y;
            float distance = Mathf.Sqrt(dx * dx + dy * dy * 2f);
            float arrival = age - distance / 8f;
            if (arrival <= 0f) return 0f;
            float strength = Mathf.Clamp(speed * 0.012f, -0.32f, 0.32f);
            float fade = 1f - age / Duration;
            return strength * anchored * Mathf.Exp(-distance * 0.65f - arrival * 3f) *
                   Mathf.Sin(arrival * 18f) * fade * fade;
        }

    }

}
