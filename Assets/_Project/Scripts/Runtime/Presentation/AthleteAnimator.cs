using UnityEngine;

namespace FirstPersonTennis
{
    // 선수 모델 애니메이션. 이동은 준비 자세·걷기·달리기 블렌드, 스윙은 Blender에서 손으로 짠 클립(tennis_animations.py).
    // 스윙 클립은 시간을 직접 정한다: 준비 동작(테이크백)은 AutoSwing이 주는 준비 정도만큼, 스윙은 클립의 임팩트 프레임이
    // 공 도착 시각에 오게 빨리 돌리고, 임팩트 뒤는 원래 속도로 마무리한다.
    // 서 있으면 온몸 스윙 층, 움직이는 중이면 상체만 스윙하고 다리는 이동 클립 (두 층 가중치를 이동 속도로 나눈다).
    public class AthleteAnimator : MonoBehaviour
    {
        public const string SpeedParameter = "Speed";
        public const string MoveRateParameter = "MoveRate";
        public const string SwingBodyLayer = "SwingBody";
        public const string SwingUpperLayer = "SwingUpper";
        const float Fps = 30f; // Blender 클립 프레임

        [SerializeField] Animator animator;
        [Tooltip("이동 방향으로 돌리는 모델 (선수 루트는 늘 네트를 본다)")]
        [SerializeField] Transform model;
        [Tooltip("이 속도(m/s)보다 빠르면 상체만 스윙하고 다리는 이동 클립")]
        [SerializeField] float upperBodyOnlySpeed = 1.5f;
        [Tooltip("달리기 클립을 원래 속도로 재생하는 이동 속도 (m/s). 더 빠르면 그만큼 빨리 돌린다")]
        [SerializeField] float runClipSpeed = 6f;
        [Tooltip("옆·앞으로 이 속도보다 빠르게 달리면 몸을 이동 방향으로 돌린다 (m/s)")]
        [SerializeField] float turnToRunSpeed = 3f;
        [Tooltip("서브를 칠 때 라켓을 떨어뜨렸다 임팩트까지 걸리는 시간 (초)")]
        [SerializeField] float serveStrikeTime = 0.08f;
        [Tooltip("손 쥐기 블렌드셰이프(GripR·GripL)가 있는 몸 메시. 리그에 손가락 뼈가 없어 모프로 쥔다")]
        [SerializeField] SkinnedMeshRenderer hands;

        [Header("공 위치 보정 (스윙 클립의 임팩트 자리 → 실제 타점)")]
        [Tooltip("오른손에 붙은 라켓 (손잡이 끝이 원점, up = 샤프트)")]
        [SerializeField] Transform racket;
        [Tooltip("라켓 손잡이 끝에서 공이 맞는 곳까지 (m)")]
        [SerializeField] float sweetSpot = 0.5f;
        [Tooltip("임팩트 몇 초 전부터 팔을 공 쪽으로 당기기 시작하는지")]
        [SerializeField] float reachLead = 0.3f;
        [Tooltip("임팩트 뒤 몇 초에 걸쳐 보정을 푸는지")]
        [SerializeField] float reachRelease = 0.35f;
        [Tooltip("팔이 못 닿는 거리를 몸을 옮겨 메우는 최대 거리 (m)")]
        [SerializeField] float maxBodyShift = 0.35f;
        [Tooltip("보정하는 최대 거리 (m). 이보다 멀면 그만큼만 다가간다")]
        [SerializeField] float maxReach = 1.2f;
        [Tooltip("낮은 공에 몸을 낮추는 최대 높이 (m). 발은 그 자리에 두고 무릎을 굽힌다")]
        [SerializeField] float maxCrouch = 0.3f;
        [Tooltip("낮은 공에서 모자란 높이 중 무릎을 굽혀 메우는 몫 (나머지는 팔)")]
        [SerializeField] float crouchShare = 0.7f;
        [Tooltip("무릎으로도 모자란 높이 중 라켓 헤드를 떨어뜨려 메우는 몫 (나머지는 팔)")]
        [SerializeField] float headDropShare = 1f;
        [Tooltip("라켓 헤드를 떨어뜨리는 최대 각도 (도)")]
        [SerializeField] float maxHeadDrop = 55f;

        // 클립별 프레임: 테이크백 끝, 임팩트, 끝 (tennis_animations.py의 CLIPS·CONTACT)
        readonly struct Clip
        {
            public readonly int Hash;
            public readonly float TakeBack, Contact, End;
            readonly float[] leftGrip; // (프레임, 왼손이 라켓을 쥐는 정도) 쌍. tennis_animations.py의 left가 throat·grip2인 키

            public Clip(string name, float takeBack, float contact, float end, params float[] leftGrip)
            {
                Hash = Animator.StringToHash(name);
                TakeBack = takeBack / Fps;
                Contact = contact / Fps;
                End = end / Fps;
                this.leftGrip = leftGrip;
            }

            public float LeftGripAt(float seconds)
            {
                float frame = seconds * Fps;
                if (leftGrip == null || leftGrip.Length < 2) return 0f;
                if (frame <= leftGrip[0]) return leftGrip[1];
                for (int i = 2; i < leftGrip.Length; i += 2)
                    if (frame <= leftGrip[i])
                        return Mathf.Lerp(leftGrip[i - 1], leftGrip[i + 1], Mathf.InverseLerp(leftGrip[i - 2], leftGrip[i], frame));
                return leftGrip[leftGrip.Length - 1];
            }
        }

        static readonly Clip Forehand = new Clip("Forehand", 16, 24, 48, 0, 1, 8, 0, 36, 0, 48, 1);
        static readonly Clip SliceForehand = new Clip("SliceForehand", 16, 24, 48, 0, 1, 8, 0, 38, 0, 48, 1);
        static readonly Clip SliceBackhand = new Clip("SliceBackhand", 16, 24, 48, 0, 1, 16, 1, 24, 0, 38, 0, 48, 1);
        static readonly Clip LobForehand = new Clip("LobForehand", 16, 24, 48, 0, 1, 8, 0, 36, 0, 48, 1);
        static readonly Clip LobBackhand = new Clip("LobBackhand", 16, 24, 48, 0, 1, 48, 1);
        static readonly Clip ServeSlice = new Clip("ServeSlice", 32, 40, 72, 0, 1, 12, 0, 48, 0, 58, 1);
        static readonly Clip ServeKick = new Clip("ServeKick", 32, 40, 72, 0, 1, 12, 0, 48, 0, 58, 1);
        static readonly Clip Backhand = new Clip("Backhand", 16, 24, 48, 0, 1, 48, 1); // 양손 백핸드
        static readonly Clip VolleyForehand = new Clip("VolleyForehand", 7, 14, 30, 0, 1, 7, 0, 20, 0, 30, 1);
        static readonly Clip VolleyBackhand = new Clip("VolleyBackhand", 7, 14, 30, 0, 1, 7, 1, 14, 0, 20, 0, 30, 1);
        static readonly Clip Smash = new Clip("Smash", 15, 22, 44, 0, 1, 8, 0, 30, 0, 44, 1);
        static readonly Clip Serve = new Clip("Serve", 32, 40, 72, 0, 1, 12, 0, 48, 0, 58, 1); // 테이크백 = 라켓을 등 뒤로 떨어뜨린 자세
        const float ServeTrophy = 28f / Fps;                        // 토스하는 동안 여기까지 가서 기다린다 (트로피 자세에서 라켓을 막 떨어뜨리기 시작)
        const float BlendOut = 0.2f;

        enum Mode { Idle, Prep, Swing, ServeToss }

        IMover mover;
        int bodyLayer, upperLayer;
        Mode mode;
        Clip clip;
        float time, weight, preContactRate;
        float yaw, yawVelocity;
        Quaternion racketRest;
        Vector3 racketRestPosition;
        Quaternion modelRest; // 모델이 루트 앞을 보는 기본 회전
        int gripLeft = -1;
        float leftGrip = 1f;
        Vector3 contact;
        bool hasContact;
        float contactTimer; // 실제 임팩트까지 남은 시간 (지나면 음수)
        // 클립 임팩트 자세의 라켓 타구점 (골반 기준: 이동 중에 치면 골반은 이동 클립을 따르고 상체만 스윙 클립이라)
        readonly System.Collections.Generic.Dictionary<int, Vector3> sweetAtContact = new();
        Transform hips, rightArm, rightFore, rightHand, leftArm, leftFore, leftHand;
        Transform leftThigh, leftShin, leftFoot, rightThigh, rightShin, rightFoot;

        public bool IsSwinging => mode == Mode.Swing;

        void Awake()
        {
            mover = GetComponent<IMover>();
            if (racket != null) { racketRest = racket.localRotation; racketRestPosition = racket.localPosition; }
            if (model != null) modelRest = model.localRotation;
            bodyLayer = animator.GetLayerIndex(SwingBodyLayer);
            upperLayer = animator.GetLayerIndex(SwingUpperLayer);
            if (hands != null)
            {
                int right = hands.sharedMesh.GetBlendShapeIndex("GripR");
                if (right >= 0) hands.SetBlendShapeWeight(right, 100f); // 오른손은 늘 라켓을 쥔다
                gripLeft = hands.sharedMesh.GetBlendShapeIndex("GripL");
            }
            Transform Bone(string bone)
            {
                foreach (Transform t in model.GetComponentsInChildren<Transform>())
                    if (t.name == bone) return t;
                return null;
            }
            hips = Bone("Hips");
            rightArm = Bone("RightArm");
            rightFore = Bone("RightForeArm");
            rightHand = Bone("RightHand");
            leftArm = Bone("LeftArm");
            leftFore = Bone("LeftForeArm");
            leftHand = Bone("LeftHand");
            leftThigh = Bone("LeftUpLeg");
            leftShin = Bone("LeftLeg");
            leftFoot = Bone("LeftFoot");
            rightThigh = Bone("RightUpLeg");
            rightShin = Bone("RightLeg");
            rightFoot = Bone("RightFoot");
        }

        // 클립마다 임팩트 프레임의 라켓 타구점을 한 번 재 둔다. 실제 타점과의 차이만큼 팔·몸을 옮긴다
        void Start()
        {
            if (racket == null || rightHand == null || hips == null) return;
            foreach (AnimationClip asset in animator.runtimeAnimatorController.animationClips)
            {
                foreach (Clip c in new[] { Forehand, Backhand, SliceForehand, SliceBackhand, LobForehand, LobBackhand, VolleyForehand, VolleyBackhand, Smash })
                {
                    if (Animator.StringToHash(asset.name) != c.Hash || sweetAtContact.ContainsKey(c.Hash)) continue;
                    asset.SampleAnimation(model.gameObject, c.Contact); // 다음 프레임에 애니메이터가 덮어쓴다
                    ApplyGroundstrokeGrip(c, c.Contact, 1f);
                    sweetAtContact[c.Hash] = hips.InverseTransformPoint(SweetSpot());
                }
            }
            ApplyGroundstrokeGrip(default, 0f, 0f);
        }

        // Preserve the grasp point when changing the groundstroke grip.
        // Backhand lays the handle diagonally across the palm; other shots keep their grip.
        void ApplyGroundstrokeGrip(Clip active, float seconds, float blend)
        {
            if (racket == null) return;
            bool forehand = active.Hash == Forehand.Hash, backhand = active.Hash == Backhand.Hash;
            float frame = seconds * Fps;
            float t = forehand || backhand ? Mathf.Clamp01(Mathf.Min(frame / 8f, (48f - frame) / 8f)) : 0f;
            t = t * t * (3f - 2f * t) * blend;
            Quaternion diagonal = Quaternion.AngleAxis(backhand ? -45f * t : 0f, Vector3.forward);
            racket.localRotation = racketRest * diagonal * Quaternion.AngleAxis((backhand ? -20f : 90f) * t, Vector3.up);
            Vector3 grasp = racketRestPosition + racketRest * (Vector3.up * 0.09f);
            racket.localPosition = grasp - racket.localRotation * (Vector3.up * 0.09f);
        }

        Vector3 SweetSpot() => racket.position + racket.up * sweetSpot;

        // 공이 오는 동안: amount 0~1만큼 테이크백. 0이면 준비 자세로 돌아간다
        public void SetPreparation(StrokeSide stroke, ShotType type, float amount)
        {
            if (mode == Mode.Swing) return;
            if (amount <= 0f)
            {
                if (mode != Mode.Idle) mode = Mode.Idle; // 테이크백을 풀거나 토스를 거둔다
                return;
            }
            Clip wanted = Pick(stroke, type);
            // 테이크백이 많이 진행된 뒤에는 클립을 바꾸지 않는다 (포핸드↔백핸드 예측이 흔들려도 자세가 튀지 않게)
            if (mode != Mode.Prep || (wanted.Hash != clip.Hash && time < clip.TakeBack * 0.3f))
            {
                if (mode != Mode.Prep) time = 0f;
                clip = wanted;
            }
            mode = Mode.Prep;
            float target = Mathf.Clamp01(amount) * clip.TakeBack;
            time = Mathf.MoveTowards(time, target, Time.deltaTime * 1.5f);
        }

        // 스윙 시작: timeToImpact 뒤에 클립의 임팩트 프레임이 오게 돌리고, 그 순간 라켓이 contactPoint(공 중심)에 오게 팔을 보정한다
        public void PlaySwing(StrokeSide stroke, ShotType type, float timeToImpact, Vector3 contactPoint)
        {
            Clip wanted = Pick(stroke, type);
            if (mode != Mode.Prep || wanted.Hash != clip.Hash) time = wanted.TakeBack * 0.5f;
            Begin(wanted, Mathf.Max(0.02f, timeToImpact));
            contact = contactPoint;
            hasContact = true;
        }

        // 토스하는 동안 매 프레임: 서브 클립을 트로피 자세까지 재생하고 기다린다
        public void PrepareServe()
        {
            if (mode == Mode.Swing) return;
            if (mode != Mode.ServeToss)
            {
                mode = Mode.ServeToss;
                clip = Serve;
                time = 0f;
            }
            time = Mathf.Min(time + Time.deltaTime, ServeTrophy);
        }

        // 서브 타격: 라켓을 떨어뜨렸다 바로 임팩트
        public void PlayServe(ServeType type = ServeType.Flat)
        {
            if (mode != Mode.ServeToss) time = ServeTrophy;
            Clip wanted = type switch
            {
                ServeType.Slice => ServeSlice,
                ServeType.Kick => ServeKick,
                _ => Serve,
            };
            Begin(wanted, serveStrikeTime);
        }

        // 포인트가 리셋될 때
        public void Stop()
        {
            mode = Mode.Idle;
            weight = 0f;
        }

        void Begin(Clip c, float timeToImpact)
        {
            hasContact = false; // 서브는 보정하지 않는다 (PlaySwing이 다시 켠다)
            contactTimer = timeToImpact;
            clip = c;
            mode = Mode.Swing;
            preContactRate = Mathf.Max(0f, c.Contact - time) / timeToImpact;
        }

        static Clip Pick(StrokeSide stroke, ShotType type) => type switch
        {
            ShotType.Slice => stroke == StrokeSide.Forehand ? SliceForehand : SliceBackhand,
            ShotType.Lob => stroke == StrokeSide.Forehand ? LobForehand : LobBackhand,
            ShotType.Smash => Smash,
            ShotType.Volley => stroke == StrokeSide.Forehand ? VolleyForehand : VolleyBackhand,
            _ => stroke == StrokeSide.Forehand ? Forehand : Backhand,
        };

        void Update()
        {
            float dt = Time.deltaTime;
            Vector3 velocity = mover != null ? mover.Velocity : Vector3.zero;
            velocity.y = 0f;
            float speed = velocity.magnitude;
            animator.SetFloat(SpeedParameter, speed, 0.08f, dt);
            animator.SetFloat(MoveRateParameter, Mathf.Max(1f, speed / runClipSpeed));

            float targetWeight = 0f;
            switch (mode)
            {
                case Mode.Prep:
                case Mode.ServeToss:
                    targetWeight = 1f;
                    break;
                case Mode.Swing:
                    float beforeImpact = Mathf.Min(dt, Mathf.Max(0f, contactTimer));
                    time += beforeImpact * preContactRate + (dt - beforeImpact);
                    contactTimer -= dt;
                    if (time >= clip.End)
                    {
                        time = clip.End;
                        mode = Mode.Idle;
                    }
                    targetWeight = time > clip.End - BlendOut ? (clip.End - time) / BlendOut : 1f;
                    break;
            }
            // 들어갈 때는 빠르게, 끝날 때는 클립 끝에서 이미 줄여 놓았다
            weight = mode == Mode.Idle ? Mathf.MoveTowards(weight, 0f, dt / BlendOut) : Mathf.MoveTowards(weight, targetWeight, dt * 8f);

            float upper = Mathf.Clamp01(speed / upperBodyOnlySpeed);
            if (weight > 0f && clip.Hash != 0)
            {
                float normalized = Mathf.Clamp01(time / clip.End);
                animator.Play(clip.Hash, bodyLayer, normalized);
                animator.Play(clip.Hash, upperLayer, normalized);
            }
            animator.SetLayerWeight(bodyLayer, weight * (1f - upper));
            animator.SetLayerWeight(upperLayer, weight * upper);

            TurnModel(velocity, speed, dt);
            UpdateLeftGrip(speed, dt);
        }

        // 왼손 쥐기: 준비 자세는 라켓 목을 받치고(걷기·달리기에서는 놓음), 스윙 중에는 클립의 양손 구간을 따른다
        void UpdateLeftGrip(float speed, float dt)
        {
            if (gripLeft < 0) return;
            float idle = 1f - Mathf.Clamp01(speed / upperBodyOnlySpeed);
            float swing = clip.Hash != 0 ? clip.LeftGripAt(time) : idle;
            leftGrip = Mathf.MoveTowards(leftGrip, Mathf.Lerp(idle, swing, weight), dt * 6f);
            hands.SetBlendShapeWeight(gripLeft, leftGrip * 100f);
        }

        // ── 공 위치 보정 ─────────────────────────────────────
        // 스윙 클립의 임팩트 자리는 몸 기준으로 하나인데 실제 타점은 옆으로 1.5m, 높이 0~2.5m까지 다르다.
        // 임팩트 앞뒤로 팔을 공 쪽으로 당기고(손 방향 = 라켓 각도는 그대로), 팔이 모자라면 몸을 조금 옮기고 낮은 공에는 무릎을 굽힌다.
        // 애니메이터가 뼈를 움직인 뒤(LateUpdate)에 덮어쓴다.
        void LateUpdate()
        {
            ApplyGroundstrokeGrip(clip, time, weight);
            float w = ReachWeight();
            Vector3 shift = Vector3.zero;
            if (model != null) model.localPosition = Vector3.zero; // 지난 프레임에 옮긴 몸을 되돌리고 잰다
            if (w > 0f && sweetAtContact.TryGetValue(clip.Hash, out Vector3 local))
            {
                Vector3 miss = Vector3.ClampMagnitude(contact - hips.TransformPoint(local), maxReach);
                shift = Vector3.ClampMagnitude(Vector3.ProjectOnPlane(miss, Vector3.up) * 0.5f, maxBodyShift) * w;
                shift += Vector3.down * Mathf.Clamp(-miss.y * crouchShare, 0f, maxCrouch) * w; // 낮은 공: 몸을 낮춘다

                // 몸을 옮기거나 낮춰도 발은 그 자리: 옮기기 전 발 위치로 다리를 다시 푼다 (무릎이 굽는다)
                Vector3 leftFootAt = leftFoot.position, rightFootAt = rightFoot.position;
                model.localPosition = Quaternion.Inverse(transform.rotation) * shift;
                SolveArm(leftThigh, leftShin, leftFoot, leftFootAt, transform.forward);
                SolveArm(rightThigh, rightShin, rightFoot, rightFootAt, transform.forward);

                // 무릎으로도 모자란 낮은 공: 손목을 축으로 라켓 헤드를 떨어뜨린다 (팔은 그만큼 덜 내려도 된다)
                Vector3 before = rightHand.position;
                Quaternion leftBefore = leftHand.rotation;
                Vector3 leftOffset = leftHand.position - before;
                float drop = Mathf.Max(0f, -miss.y * w + shift.y) * headDropShare; // shift.y는 이미 낮춘 높이 (음수)
                Quaternion tilt = HeadDrop(drop);
                Vector3 sweetBefore = SweetSpot();
                rightHand.rotation = tilt * rightHand.rotation;
                Vector3 tilted = SweetSpot() - sweetBefore;

                SolveArm(rightArm, rightFore, rightHand, before + miss * w - shift - tilted, transform.TransformDirection(new Vector3(0.4f, -1f, -0.5f)));
                if (leftGrip > 0.5f) // 양손 그립: 왼손도 라켓과 같이 옮기고 돌린다
                {
                    leftHand.rotation = tilt * leftBefore;
                    SolveArm(leftArm, leftFore, leftHand, rightHand.position + tilt * leftOffset,
                        transform.TransformDirection(new Vector3(-0.4f, -1f, -0.5f)));
                }
            }
        }

        // 타구점을 depth만큼 낮추는 라켓 기울기 (손목 기준, 샤프트를 수직면 안에서 아래로). 최대 maxHeadDrop도
        Quaternion HeadDrop(float depth)
        {
            if (depth <= 1e-3f) return Quaternion.identity;
            Vector3 shaft = racket.up;
            Vector3 axis = Vector3.Cross(shaft, Vector3.up);
            if (axis.sqrMagnitude < 1e-6f) return Quaternion.identity; // 라켓이 이미 수직
            float angle = Mathf.Min(Mathf.Asin(Mathf.Clamp01(depth / sweetSpot)) * Mathf.Rad2Deg, maxHeadDrop);
            Quaternion q = Quaternion.AngleAxis(angle, axis.normalized);
            return (q * shaft).y < shaft.y ? q : Quaternion.Inverse(q);
        }

        // 임팩트 reachLead초 전부터 1로 올라가고, 임팩트 뒤 reachRelease초에 걸쳐 0으로
        float ReachWeight()
        {
            if (!hasContact || mode != Mode.Swing || rightHand == null) return 0f;
            float w = contactTimer > 0f ? 1f - contactTimer / reachLead : 1f + contactTimer / reachRelease;
            return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(w)) * weight;
        }

        // 두 뼈 IK (팔·다리): 가운데 관절 각도로 뿌리~끝 거리를 맞추고, 뿌리를 돌려 끝을 목표로. 끝 뼈(손·발)의 월드 방향은 그대로 둔다
        static void SolveArm(Transform upper, Transform lower, Transform end, Vector3 target, Vector3 bendHint)
        {
            Quaternion endRotation = end.rotation;
            Vector3 a = upper.position, b = lower.position, c = end.position;
            float ab = Vector3.Distance(a, b), bc = Vector3.Distance(b, c);
            float reach = Mathf.Clamp(Vector3.Distance(a, target), Mathf.Abs(ab - bc) + 1e-3f, (ab + bc) * 0.999f);
            Vector3 toUpper = (a - b).normalized;
            Vector3 bend = Vector3.ProjectOnPlane(c - b, toUpper);
            if (bend.sqrMagnitude < 1e-8f) bend = Vector3.ProjectOnPlane(bendHint, toUpper); // 팔이 다 펴졌을 때
            bend.Normalize();
            float elbow = Mathf.Acos(Mathf.Clamp((ab * ab + bc * bc - reach * reach) / (2f * ab * bc), -1f, 1f));
            Vector3 forearm = (toUpper * Mathf.Cos(elbow) + bend * Mathf.Sin(elbow)) * bc;
            lower.rotation = Quaternion.FromToRotation(c - b, forearm) * lower.rotation;
            upper.rotation = Quaternion.FromToRotation(end.position - a, target - a) * upper.rotation;
            end.rotation = endRotation;
        }

        // 빠르게 옆·앞으로 달릴 때는 몸을 이동 방향으로, 그 밖에는 네트 쪽 (스윙 클립은 네트를 보고 친다)
        void TurnModel(Vector3 velocity, float speed, float dt)
        {
            if (model == null) return;
            float target = 0f;
            if (mode == Mode.Idle && speed > turnToRunSpeed)
            {
                Vector3 local = transform.InverseTransformDirection(velocity);
                float angle = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
                if (Mathf.Abs(angle) <= 100f) target = angle; // 뒤로 물러설 때는 네트를 본 채로
            }
            yaw = Mathf.SmoothDampAngle(yaw, target, ref yawVelocity, 0.12f, Mathf.Infinity, dt);
            model.localRotation = Quaternion.Euler(0f, yaw, 0f) * modelRest;
        }
    }
}
