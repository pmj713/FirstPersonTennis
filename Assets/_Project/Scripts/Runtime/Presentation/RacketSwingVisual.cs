using UnityEngine;

namespace FirstPersonTennis
{
    // 1단계용 절차적 라켓 스윙. 어깨 피벗을 회전시키고, 임팩트 순간이 공 도착 시각과 맞도록 재생한다.
    // 백핸드는 피벗을 좌우 반전해서 같은 포즈를 쓴다. 정식 애니메이션은 이후 단계에서 교체.
    public class RacketSwingVisual : MonoBehaviour
    {
        [SerializeField] Transform pivot;
        [SerializeField] float shoulderOffset = 0.22f;

        readonly struct Pose
        {
            public readonly float Yaw, Lift;
            public Pose(float yaw, float lift)
            {
                Yaw = yaw;
                Lift = lift;
            }
        }

        static readonly Pose Ready = new Pose(-75f, -25f);

        StrokeSide stroke;
        ShotType type;
        float prep;
        bool swinging;
        float swingTime, impactTime, followTime;
        Quaternion swingStart;

        public void SetPreparation(StrokeSide strokeSide, ShotType shotType, float amount)
        {
            if (swinging) return;
            stroke = strokeSide;
            type = shotType;
            prep = Mathf.Clamp01(amount);
        }

        public void PlaySwing(StrokeSide strokeSide, ShotType shotType, float timeToImpact, float followThrough)
        {
            stroke = strokeSide;
            type = shotType;
            ApplyMirror();
            swingStart = pivot.localRotation;
            swinging = true;
            swingTime = 0f;
            impactTime = Mathf.Max(0.02f, timeToImpact);
            followTime = Mathf.Max(0.05f, followThrough);
        }

        void Update()
        {
            if (pivot == null) return;
            float dt = Time.deltaTime;

            if (swinging)
            {
                swingTime += dt;
                if (swingTime < impactTime)
                {
                    float t = swingTime / impactTime;
                    pivot.localRotation = Quaternion.Slerp(swingStart, Rot(Impact()), t * t); // 가속하며 임팩트
                }
                else if (swingTime < impactTime + followTime)
                {
                    float t = (swingTime - impactTime) / followTime;
                    pivot.localRotation = Quaternion.Slerp(Rot(Impact()), Rot(Follow()), 1f - (1f - t) * (1f - t));
                }
                else
                {
                    swinging = false;
                    prep = 0f;
                }
                return;
            }

            ApplyMirror();
            Quaternion desired = Quaternion.Slerp(Rot(Ready), Rot(TakeBack()), prep);
            pivot.localRotation = Quaternion.Slerp(pivot.localRotation, desired, 1f - Mathf.Exp(-12f * dt));
        }

        // 포핸드 기준 포즈. yaw +면 라켓이 뒤로, lift +면 라켓 끝이 위로.
        Pose TakeBack() => type switch
        {
            ShotType.Slice => new Pose(80f, 40f),
            ShotType.Lob => new Pose(80f, -35f),
            ShotType.Smash => new Pose(20f, 85f),
            _ => new Pose(85f, -15f),
        };

        Pose Impact() => type switch
        {
            ShotType.Slice => new Pose(-10f, 0f),
            ShotType.Lob => new Pose(-10f, -10f),
            ShotType.Smash => new Pose(-20f, 60f),
            _ => new Pose(-10f, -5f),
        };

        Pose Follow() => type switch
        {
            ShotType.Slice => new Pose(-120f, -25f),
            ShotType.Lob => new Pose(-130f, 75f),
            ShotType.Smash => new Pose(-110f, -35f),
            _ => new Pose(-150f, 50f),
        };

        float Mirror => stroke == StrokeSide.Forehand ? 1f : -1f;

        Quaternion Rot(Pose p) => Quaternion.Euler(0f, p.Yaw * Mirror, p.Lift * Mirror);

        void ApplyMirror()
        {
            Vector3 position = pivot.localPosition;
            position.x = shoulderOffset * Mirror;
            pivot.localPosition = position;
            pivot.localScale = new Vector3(Mirror, 1f, 1f);
        }
    }
}
