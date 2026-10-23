using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FirstPersonTennis
{
    // 코트 주위 경기장: 선수가 움직이는 범위 바깥에 펜스(광고판 자리), 네 면 관중석(좌석 12줄, 통로), 모서리 조명탑,
    // 양 끝 관중석 위 전광판, 네트 옆 심판 의자와 반대편 선수 벤치, 펜스 밖 바닥. CourtView처럼 코트 크기 배율을 따라 코드로 만든다.
    // 공·선수와 충돌은 없다 (보는 용도). 상자들을 재질별로 한 메시에 모아 오브젝트 수를 줄인다
    [ExecuteAlways]
    [DefaultExecutionOrder(-90)] // CourtView(-100)가 코트 크기를 적용한 뒤
    public class StadiumView : MonoBehaviour
    {
        [SerializeField] TennisConfig config;
        [SerializeField] Material concrete;
        [SerializeField] Material seat;
        [SerializeField] Material seatAccent;
        [SerializeField] Material barrier;
        [SerializeField] Material barrierAlt;
        [SerializeField] Material trim;
        [SerializeField] Material metal;
        [SerializeField] Material screen;

        // 관중석 치수 (m)
        const int Rows = 12;
        const float RowDepth = 0.85f, RowRise = 0.42f;
        const float BarrierGap = 0.6f;   // 선수 이동 범위 끝 → 펜스
        const float Walkway = 1.4f;      // 펜스 → 관중석 첫 줄
        const float BarrierHeight = 1.1f;
        const float SeatPitch = 0.55f, SeatWidth = 0.48f;
        const float AisleWidth = 1.3f;

        public static float FenceHalfWidth => Court.PlayableHalfWidth + BarrierGap;
        public static float FenceHalfLength => Court.PlayableDepth + BarrierGap;
        public static float StandDepth => Rows * RowDepth;
        public static float StandHeight => Rows * RowRise;
        // 관중석 바깥 끝 (뒷벽 포함)
        public static float OuterHalfWidth => FenceHalfWidth + Walkway + StandDepth + 0.4f;
        public static float OuterHalfLength => FenceHalfLength + Walkway + StandDepth + 0.4f;

        bool dirty;
        readonly List<Mesh> meshes = new List<Mesh>();
        Dictionary<(Material, bool), Batch> batches;

        sealed class Batch
        {
            public readonly List<Vector3> Vertices = new List<Vector3>();
            public readonly List<Vector3> Normals = new List<Vector3>();
            public readonly List<int> Triangles = new List<int>();
        }

        void OnEnable()
        {
            TennisConfig.Changed += OnConfigChanged;
            Rebuild();
        }

        void OnDisable()
        {
            TennisConfig.Changed -= OnConfigChanged;
            if (!Application.isPlaying) Clear();
        }

        void OnConfigChanged(TennisConfig changed)
        {
            if (changed != config) return;
            dirty = true;
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
            batches = new Dictionary<(Material, bool), Batch>();

            BuildConcourse();
            BuildBarrier();
            float fx = FenceHalfWidth, fz = FenceHalfLength;
            // 관중석: 옆 두 면은 코트 길이 방향, 끝 두 면은 너비 방향. 모서리는 조명탑 자리로 비운다
            BuildStand(new Vector3(fx + Walkway, 0f, 0f), Vector3.right, fz * 2f);
            BuildStand(new Vector3(-fx - Walkway, 0f, 0f), Vector3.left, fz * 2f);
            BuildStand(new Vector3(0f, 0f, fz + Walkway), Vector3.forward, fx * 2f);
            BuildStand(new Vector3(0f, 0f, -fz - Walkway), Vector3.back, fx * 2f);
            BuildLightTowers();
            BuildScreens();
            BuildUmpireChair();
            BuildBenches();

            foreach (KeyValuePair<(Material, bool), Batch> kv in batches) Emit(kv.Key.Item1, kv.Key.Item2, kv.Value);
            batches = null;

            if (!Application.isPlaying)
            {
                // 에디터용: 씬에 저장하지 않고 열 때마다 다시 만든다 (CourtView와 같음)
                foreach (Transform t in GetComponentsInChildren<Transform>(true))
                    if (t != transform) t.gameObject.hideFlags = HideFlags.DontSave | HideFlags.NotEditable;
                foreach (Mesh m in meshes) m.hideFlags = HideFlags.DontSave;
            }
        }

        // ── 부분별 ────────────────────────────────────────────

        // 펜스 밖 바닥: 코트 주변 바닥색(코트 종류마다 다름)을 펜스 안쪽에만 남기고 바깥은 콘크리트로 덮는다
        void BuildConcourse()
        {
            const float Outer = 70f, Y = 0.003f;
            float fx = FenceHalfWidth, fz = FenceHalfLength;
            Box(concrete, false, new Vector3(0f, Y, (fz + Outer) / 2f), new Vector3(Outer * 2f, 0.002f, Outer - fz));
            Box(concrete, false, new Vector3(0f, Y, -(fz + Outer) / 2f), new Vector3(Outer * 2f, 0.002f, Outer - fz));
            Box(concrete, false, new Vector3((fx + Outer) / 2f, Y, 0f), new Vector3(Outer - fx, 0.002f, fz * 2f));
            Box(concrete, false, new Vector3(-(fx + Outer) / 2f, Y, 0f), new Vector3(Outer - fx, 0.002f, fz * 2f));
        }

        // 선수 이동 범위를 두르는 낮은 펜스. 4m마다 두 가지 색 판을 번갈아 (광고판 자리), 위에 흰 띠
        void BuildBarrier()
        {
            float fx = FenceHalfWidth, fz = FenceHalfLength;
            Wall(new Vector3(-fx, 0f, fz), new Vector3(fx, 0f, fz));
            Wall(new Vector3(-fx, 0f, -fz), new Vector3(fx, 0f, -fz));
            Wall(new Vector3(fx, 0f, -fz), new Vector3(fx, 0f, fz));
            Wall(new Vector3(-fx, 0f, -fz), new Vector3(-fx, 0f, fz));

            void Wall(Vector3 a, Vector3 b)
            {
                Vector3 dir = (b - a).normalized;
                float length = Vector3.Distance(a, b);
                int panels = Mathf.Max(1, Mathf.RoundToInt(length / 4f));
                float pl = length / panels;
                Quaternion rot = Quaternion.LookRotation(Vector3.Cross(dir, Vector3.up), Vector3.up);
                for (int i = 0; i < panels; i++)
                {
                    Vector3 c = a + dir * (pl * (i + 0.5f));
                    Box(i % 2 == 0 ? barrier : barrierAlt, true, c + Vector3.up * BarrierHeight / 2f,
                        new Vector3(pl, BarrierHeight, 0.15f), rot);
                }
                Box(trim, false, (a + b) / 2f + Vector3.up * (BarrierHeight + 0.02f), new Vector3(length + 0.15f, 0.04f, 0.17f), rot);
            }
        }

        // 계단식 관중석 한 면. origin = 첫 줄 앞 가운데, outward = 코트에서 멀어지는 방향, length = 면 길이.
        // 좌석은 하나씩 (등받이 + 앉는 판), 면을 셋으로 나누는 통로 두 곳은 비운다. 4줄마다 다른 색 줄
        void BuildStand(Vector3 origin, Vector3 outward, float length)
        {
            Quaternion rot = Quaternion.LookRotation(outward, Vector3.up); // 로컬 x = 면 방향, z = 바깥
            Vector3 Local(float along, float up, float depth) => origin + rot * new Vector3(along, up, depth);

            float aisleA = -length / 6f, aisleB = length / 6f;
            int seatCount = Mathf.FloorToInt(length / SeatPitch);
            float firstSeat = -(seatCount - 1) * SeatPitch / 2f;
            for (int r = 0; r < Rows; r++)
            {
                float depth = r * RowDepth, top = (r + 1) * RowRise;
                // 콘크리트 단 (바닥부터 꽉 채운다)
                Box(concrete, true, Local(0f, top / 2f, depth + RowDepth / 2f), new Vector3(length, top, RowDepth), rot);
                Material seatMat = r % 4 == 3 ? seatAccent : seat;
                for (int s = 0; s < seatCount; s++)
                {
                    float x = firstSeat + s * SeatPitch;
                    if (Mathf.Abs(x - aisleA) < AisleWidth / 2f || Mathf.Abs(x - aisleB) < AisleWidth / 2f) continue;
                    Box(seatMat, true, Local(x, top + 0.2f, depth + 0.42f), new Vector3(SeatWidth, 0.06f, 0.4f), rot);
                    Box(seatMat, true, Local(x, top + 0.42f, depth + 0.64f), new Vector3(SeatWidth, 0.42f, 0.06f), rot);
                }
            }
            // 통로 난간과 뒷벽
            float back = Rows * RowDepth;
            Box(concrete, true, Local(0f, (StandHeight + 1.4f) / 2f, back + 0.2f), new Vector3(length, StandHeight + 1.4f, 0.4f), rot);
            foreach (float aisle in new[] { aisleA, aisleB })
            foreach (float side in new[] { -1f, 1f })
            {
                float x = aisle + side * (AisleWidth / 2f + 0.03f);
                float rise = Mathf.Atan2(StandHeight, back) * Mathf.Rad2Deg;
                Box(metal, true, Local(x, StandHeight / 2f + 0.9f, back / 2f), new Vector3(0.05f, 0.05f, back / Mathf.Cos(rise * Mathf.Deg2Rad)),
                    rot * Quaternion.Euler(-rise, 0f, 0f));
            }
        }

        // 모서리 네 곳 조명탑 (그림자 없음: 긴 그림자가 코트를 가로지르지 않게)
        void BuildLightTowers()
        {
            const float Height = 24f;
            float x = FenceHalfWidth + Walkway + StandDepth * 0.5f, z = FenceHalfLength + Walkway + StandDepth * 0.5f;
            foreach (float sx in new[] { -1f, 1f })
            foreach (float sz in new[] { -1f, 1f })
            {
                var basePos = new Vector3(sx * x, 0f, sz * z);
                Box(metal, false, basePos + Vector3.up * Height / 2f, new Vector3(0.5f, Height, 0.5f));
                // 코트 가운데를 향해 조금 숙인 조명판
                Quaternion face = Quaternion.LookRotation(-new Vector3(basePos.x, 0f, basePos.z).normalized, Vector3.up) * Quaternion.Euler(20f, 0f, 0f);
                Vector3 head = basePos + Vector3.up * (Height + 1.2f);
                Box(metal, false, head, new Vector3(4.4f, 3f, 0.4f), face);
                Box(trim, false, head + face * new Vector3(0f, 0f, 0.22f), new Vector3(4f, 2.6f, 0.05f), face); // 조명면 (흰색)
            }
        }

        // 양 끝 관중석 위 전광판 (코트를 향함)
        void BuildScreens()
        {
            float z = FenceHalfLength + Walkway + StandDepth + 0.8f;
            float bottom = StandHeight + 1.8f;
            const float W = 12f, H = 4.5f;
            foreach (float s in new[] { -1f, 1f })
            {
                Quaternion face = Quaternion.LookRotation(new Vector3(0f, 0f, -s), Vector3.up);
                var c = new Vector3(0f, bottom + H / 2f, s * z);
                Box(metal, true, c, new Vector3(W + 0.6f, H + 0.6f, 0.5f), face);
                Box(screen, false, c + face * new Vector3(0f, 0f, 0.27f), new Vector3(W, H, 0.05f), face);
                foreach (float px in new[] { -W / 3f, W / 3f })
                    Box(metal, true, new Vector3(px, bottom / 2f, s * z), new Vector3(0.4f, bottom, 0.4f));
            }
        }

        // 네트 기둥 옆 심판 의자 (+x 쪽): 다리 넷, 발판, 의자, 등받이, 사다리
        void BuildUmpireChair()
        {
            var b = new Vector3(Court.NetPostX + 1.3f, 0f, 0f);
            const float Seat = 1.9f;
            foreach (float dx in new[] { -0.35f, 0.35f })
            foreach (float dz in new[] { -0.35f, 0.35f })
                Box(metal, true, b + new Vector3(dx, Seat / 2f, dz), new Vector3(0.06f, Seat, 0.06f));
            Box(seat, true, b + new Vector3(0f, Seat, 0f), new Vector3(0.9f, 0.08f, 0.9f));
            Box(seat, true, b + new Vector3(0.15f, Seat + 0.3f, 0f), new Vector3(0.5f, 0.08f, 0.55f));
            Box(seat, true, b + new Vector3(0.42f, Seat + 0.65f, 0f), new Vector3(0.08f, 0.7f, 0.6f));
            Box(trim, true, b + new Vector3(-0.3f, Seat + 0.3f, 0f), new Vector3(0.06f, 0.6f, 0.9f)); // 앞 난간
            for (int i = 1; i <= 5; i++) // 사다리 (네트 반대쪽 뒤로)
                Box(metal, true, b + new Vector3(0.75f, Seat * i / 6f, 0f), new Vector3(0.25f, 0.04f, 0.5f));
        }

        // 반대편(-x) 네트 양옆 선수 벤치
        void BuildBenches()
        {
            float x = -(Court.NetPostX + 1.6f);
            foreach (float z in new[] { -2.4f, 2.4f })
            {
                var c = new Vector3(x, 0f, z);
                Box(seat, true, c + new Vector3(0f, 0.45f, 0f), new Vector3(0.55f, 0.08f, 2.2f));
                Box(seat, true, c + new Vector3(-0.25f, 0.8f, 0f), new Vector3(0.06f, 0.6f, 2.2f));
                foreach (float dz in new[] { -1f, 1f })
                    Box(metal, true, c + new Vector3(0f, 0.22f, dz), new Vector3(0.5f, 0.44f, 0.06f));
                Box(barrierAlt, true, c + new Vector3(0.1f, 0.2f, 1.45f), new Vector3(0.4f, 0.4f, 0.5f)); // 아이스박스
            }
        }

        // ── 메시 ──────────────────────────────────────────────

        void Box(Material material, bool shadows, Vector3 center, Vector3 size) => Box(material, shadows, center, size, Quaternion.identity);

        void Box(Material material, bool shadows, Vector3 center, Vector3 size, Quaternion rotation)
        {
            if (material == null) return;
            if (!batches.TryGetValue((material, shadows), out Batch batch))
                batches[(material, shadows)] = batch = new Batch();
            Vector3 h = size / 2f;
            // 면마다 4개 점 (평평한 음영)
            AddFace(batch, center, rotation, Vector3.right, Vector3.up, Vector3.forward, h);
            AddFace(batch, center, rotation, Vector3.left, Vector3.up, Vector3.back, h);
            AddFace(batch, center, rotation, Vector3.up, Vector3.forward, Vector3.right, h);
            AddFace(batch, center, rotation, Vector3.down, Vector3.back, Vector3.right, h);
            AddFace(batch, center, rotation, Vector3.forward, Vector3.up, Vector3.left, h);
            AddFace(batch, center, rotation, Vector3.back, Vector3.up, Vector3.right, h);
        }

        // normal 방향 면. u·v는 면 위의 두 축으로 u × v = normal (위 여섯 면 모두). 이 순서로 감으면
        // 바깥(normal 쪽)에서 볼 때 시계 방향이라 Unity의 앞면이 된다
        static void AddFace(Batch b, Vector3 center, Quaternion rot, Vector3 normal, Vector3 u, Vector3 v, Vector3 h)
        {
            Vector3 n = Vector3.Scale(normal, h), du = Vector3.Scale(u, h), dv = Vector3.Scale(v, h);
            int start = b.Vertices.Count;
            b.Vertices.Add(center + rot * (n - du - dv));
            b.Vertices.Add(center + rot * (n + du - dv));
            b.Vertices.Add(center + rot * (n + du + dv));
            b.Vertices.Add(center + rot * (n - du + dv));
            Vector3 worldNormal = rot * normal;
            for (int i = 0; i < 4; i++) b.Normals.Add(worldNormal);
            b.Triangles.Add(start); b.Triangles.Add(start + 1); b.Triangles.Add(start + 2);
            b.Triangles.Add(start); b.Triangles.Add(start + 2); b.Triangles.Add(start + 3);
        }

        void Emit(Material material, bool shadows, Batch batch)
        {
            var mesh = new Mesh { name = $"Stadium_{material.name}", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(batch.Vertices);
            mesh.SetNormals(batch.Normals);
            mesh.SetTriangles(batch.Triangles, 0);
            mesh.RecalculateBounds();
            meshes.Add(mesh);
            var go = new GameObject(mesh.name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
        }

        void Clear()
        {
            for (int i = transform.childCount - 1; i >= 0; i--) Remove(transform.GetChild(i).gameObject);
            foreach (Mesh m in meshes) Remove(m);
            meshes.Clear();
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
