using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FirstPersonTennis
{
    // 자국이 남는 코트(클레이)에서 공이 떨어진 자리를 표시한다. 진행 방향으로 길쭉한 타원이고,
    // 다음 포인트를 위해 공을 손에 들면 지운다. 인·아웃을 눈으로 확인하는 용도.
    public class BallMarks : MonoBehaviour
    {
        [SerializeField] TennisBall ball;
        [SerializeField] Material material;
        [SerializeField] int maxMarks = 12;

        const int Segments = 24;

        readonly List<Transform> marks = new List<Transform>();
        Mesh disc;
        int next;

        void OnEnable()
        {
            ball.Bounced += OnBounced;
            ball.Held += Clear;
            Court.SurfaceChanged += Clear;
        }

        void OnDisable()
        {
            ball.Bounced -= OnBounced;
            ball.Held -= Clear;
            Court.SurfaceChanged -= Clear;
        }

        void OnDestroy()
        {
            if (disc != null) Destroy(disc);
        }

        void OnBounced(Vector3 point)
        {
            if (!Court.Surface.leavesMarks) return;
            Vector3 direction = ball.State.Velocity;
            direction.y = 0f;
            float diameter = BallPhysics.Radius * 2f;

            Transform mark = Take();
            mark.SetPositionAndRotation(new Vector3(point.x, 0.012f, point.z),
                Quaternion.LookRotation(direction.sqrMagnitude > 1e-4f ? direction : Vector3.forward));
            mark.localScale = new Vector3(diameter * 1.1f, 1f, diameter * 2.4f);
            mark.gameObject.SetActive(true);
        }

        void Clear()
        {
            foreach (Transform mark in marks)
                if (mark.gameObject.activeSelf) mark.gameObject.SetActive(false);
        }

        // 오래된 자국부터 다시 쓴다
        Transform Take()
        {
            if (marks.Count < maxMarks)
            {
                var go = new GameObject("BallMark");
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = disc != null ? disc : disc = CreateDisc();
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                marks.Add(go.transform);
                return go.transform;
            }
            Transform mark = marks[next];
            next = (next + 1) % marks.Count;
            return mark;
        }

        // 지름 1, 바닥(XZ)에 눕힌 원판
        static Mesh CreateDisc()
        {
            var vertices = new Vector3[Segments + 1];
            var triangles = new int[Segments * 3];
            for (int i = 0; i < Segments; i++)
            {
                float a = i * Mathf.PI * 2f / Segments;
                vertices[i + 1] = new Vector3(Mathf.Cos(a) * 0.5f, 0f, Mathf.Sin(a) * 0.5f);
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = (i + 1) % Segments + 1;
                triangles[i * 3 + 2] = i + 1;
            }
            var mesh = new Mesh { name = "BallMarkDisc", vertices = vertices, triangles = triangles };
            mesh.RecalculateNormals();
            return mesh;
        }
    }
}
