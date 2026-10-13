using UnityEngine;

namespace FirstPersonTennis
{
    // 바닥에 눕힌 고리(또는 원판) 메시. 공 그림자, 낙하 지점 마커, 타격 범위 링에 쓴다.
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter))]
    public class ProceduralRing : MonoBehaviour
    {
        [Tooltip("0이면 원판")]
        [SerializeField, Range(0f, 0.99f)] float innerRadius = 0.4f;
        [SerializeField] float outerRadius = 0.5f;
        [SerializeField, Range(8, 128)] int segments = 48;

        Mesh mesh;

        void OnEnable() => Rebuild();

        void OnDestroy()
        {
            if (mesh == null) return;
            if (Application.isPlaying) Destroy(mesh);
            else DestroyImmediate(mesh);
        }

        void Rebuild()
        {
            if (mesh == null)
            {
                mesh = new Mesh { name = "ProceduralRing", hideFlags = HideFlags.DontSave };
            }
            mesh.Clear();

            int n = segments;
            bool disc = innerRadius <= 0.0001f;
            var vertices = new Vector3[disc ? n + 1 : n * 2];
            var triangles = new int[n * (disc ? 3 : 6)];

            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                if (disc)
                {
                    vertices[i + 1] = dir * outerRadius;
                }
                else
                {
                    vertices[i * 2] = dir * innerRadius;
                    vertices[i * 2 + 1] = dir * outerRadius;
                }
            }

            for (int i = 0; i < n; i++)
            {
                int next = (i + 1) % n;
                if (disc)
                {
                    triangles[i * 3] = 0;
                    triangles[i * 3 + 1] = 1 + next;
                    triangles[i * 3 + 2] = 1 + i;
                }
                else
                {
                    int inner = i * 2, outer = i * 2 + 1, nextInner = next * 2, nextOuter = next * 2 + 1;
                    int t = i * 6;
                    triangles[t] = inner;
                    triangles[t + 1] = nextOuter;
                    triangles[t + 2] = outer;
                    triangles[t + 3] = inner;
                    triangles[t + 4] = nextInner;
                    triangles[t + 5] = nextOuter;
                }
            }

            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            GetComponent<MeshFilter>().sharedMesh = mesh;
        }
    }
}
