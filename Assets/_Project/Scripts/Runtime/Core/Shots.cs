using System;
using UnityEngine;

namespace FirstPersonTennis
{
    // Volley는 입력 구질이 아니라, 바운드 전에 칠 때 자동 스윙이 정하는 타격 종류다.
    public enum ShotType { Topspin, Slice, Lob, Smash, Volley }

    public enum ServeType { Flat, Slice, Kick }

    public enum StrokeSide { Forehand, Backhand }

    public struct ShotIntent
    {
        public ShotType Type;
        public float Aim;        // -1 왼쪽 ~ +1 오른쪽 (치는 사람 기준)
        public float Depth;      // 0 짧게 ~ 1 길게 (슬라이스·로브)
        public float SpeedScale; // 0이면 1로 본다

        public static ShotIntent Default => new ShotIntent { Type = ShotType.Topspin, Depth = 1f };
    }

    [Serializable]
    public struct ShotProfile
    {
        [Tooltip("기본 속도 (m/s)")] public float speed;
        [Tooltip("가장 짧게 칠 때 속도 (m/s)")] public float shortSpeed;
        [Tooltip("스핀 (rad/s). + 탑스핀, - 백스핀")] public float spin;
        [Tooltip("네트에서 가장 가까운 목표 거리 (m)")] public float minDepth;
        [Tooltip("네트에서 가장 먼 목표 거리 (m)")] public float maxDepth;
        [Tooltip("0보다 크면 발사각을 고정하고 속도를 계산한다 (로브)")] public float launchAngle;
        [Tooltip("옆 스핀 (rad/s). + 치는 사람 기준 왼쪽으로 휜다 (서브)")] public float sideSpin;
    }

    public struct ShotSolution
    {
        public Vector3 Velocity;
        public Vector3 Spin;
        public Vector3 Landing;
        public float NetClearance;
        public bool Success;
    }

    // 목표 지점에 첫 바운드가 떨어지도록 발사 속도를 역산한다.
    public static class ShotSolver
    {
        public const float NetMargin = 0.08f;
        public const float DistanceTolerance = 0.3f;
        const int MaxFlightSteps = 2400;

        // 진행 방향에 수직인 축의 스핀. + 탑스핀, - 백스핀
        public static Vector3 SpinFor(Vector3 horizontalDir, float spinRate) =>
            Vector3.Cross(Vector3.up, horizontalDir) * spinRate;

        // 속도를 정해 두고 낮은 궤적의 발사각을 찾는다. 네트에 걸리면 속도를 줄여 궤적을 높인다.
        // sideSpin(세로축 스핀, rad/s)이 있으면 공이 옆으로 휘므로, 떨어진 위치만큼 조준을 옮겨 다시 푼다.
        public static ShotSolution SolveDrive(Vector3 from, Vector3 target, float speed, float spinRate,
            in SurfaceProfile surface, float minAngle = -12f, float sideSpin = 0f)
        {
            if (sideSpin == 0f) return SolveDriveStraight(from, target, speed, spinRate, 0f, surface, minAngle);

            Vector3 aim = target;
            ShotSolution solution = default;
            for (int i = 0; i < 5; i++)
            {
                solution = SolveDriveStraight(from, aim, speed, spinRate, sideSpin, surface, minAngle);
                Vector3 miss = solution.Landing - target;
                miss.y = 0f;
                if (miss.magnitude < 0.1f) break;
                aim -= miss;
            }
            Vector3 finalMiss = solution.Landing - target;
            finalMiss.y = 0f;
            solution.Success = solution.NetClearance >= NetMargin && finalMiss.magnitude <= DistanceTolerance;
            return solution;
        }

        static ShotSolution SolveDriveStraight(Vector3 from, Vector3 target, float speed, float spinRate,
            float sideSpin, in SurfaceProfile surface, float minAngle)
        {
            Horizontal(from, target, out Vector3 dir, out float distance);
            Vector3 spin = SpinFor(dir, spinRate) + Vector3.up * sideSpin;
            ShotSolution best = Evaluate(from, dir, distance, speed, 8f, spin, surface);
            float bestError = Score(best, from, dir, distance);

            for (int attempt = 0; attempt < 16; attempt++)
            {
                if (!TryFindLowAngle(from, dir, distance, speed, spin, surface, minAngle, out float angle))
                {
                    speed *= 1.15f; // 거리가 모자람
                    continue;
                }
                ShotSolution solution = Evaluate(from, dir, distance, speed, angle, spin, surface);
                if (solution.Success) return solution;

                float error = Score(solution, from, dir, distance);
                if (error < bestError)
                {
                    best = solution;
                    bestError = error;
                }
                speed *= 0.93f; // 네트에 걸리거나 길게 넘어감 → 느리고 높게
            }
            return best;
        }

        // 발사각을 고정하고 속도를 찾는다 (로브).
        public static ShotSolution SolveFixedAngle(Vector3 from, Vector3 target, float angle, float spinRate,
            in SurfaceProfile surface)
        {
            Horizontal(from, target, out Vector3 dir, out float distance);
            Vector3 spin = SpinFor(dir, spinRate);
            float lo = 3f, hi = 45f;
            for (int i = 0; i < 24; i++)
            {
                float mid = 0.5f * (lo + hi);
                if (Range(from, dir, mid, angle, spin, surface) >= distance) hi = mid;
                else lo = mid;
            }
            return Evaluate(from, dir, distance, hi, angle, spin, surface);
        }

        static bool TryFindLowAngle(Vector3 from, Vector3 dir, float distance, float speed, Vector3 spin,
            in SurfaceProfile surface, float minAngle, out float angle)
        {
            const float MaxAngle = 60f, StepDeg = 2f;
            float prevAngle = minAngle;
            float prevRange = Range(from, dir, speed, prevAngle, spin, surface);
            if (prevRange >= distance)
            {
                angle = minAngle; // 가장 낮게 쳐도 길다 → 속도를 줄여야 함
                return true;
            }

            for (float a = minAngle + StepDeg; a <= MaxAngle; a += StepDeg)
            {
                float range = Range(from, dir, speed, a, spin, surface);
                if (range >= distance)
                {
                    float lo = prevAngle, hi = a;
                    for (int i = 0; i < 18; i++)
                    {
                        float mid = 0.5f * (lo + hi);
                        if (Range(from, dir, speed, mid, spin, surface) >= distance) hi = mid;
                        else lo = mid;
                    }
                    angle = hi;
                    return true;
                }
                if (range > 0f && prevRange > 0f && range < prevRange) break; // 최대 사거리를 지났다 (네트에 걸린 각도는 제외)
                prevAngle = a;
                prevRange = range;
            }
            angle = 0f;
            return false;
        }

        static ShotSolution Evaluate(Vector3 from, Vector3 dir, float distance, float speed, float angle,
            Vector3 spin, in SurfaceProfile surface)
        {
            var solution = new ShotSolution { Velocity = Launch(dir, speed, angle), Spin = spin };
            bool landed = Fly(from, solution.Velocity, spin, surface, out solution.Landing, out solution.NetClearance);
            float range = Vector3.Dot(solution.Landing - from, dir);
            solution.Success = landed && solution.NetClearance >= NetMargin &&
                               Mathf.Abs(range - distance) <= DistanceTolerance;
            return solution;
        }

        static float Score(in ShotSolution s, Vector3 from, Vector3 dir, float distance)
        {
            if (s.NetClearance < 0f) return float.MaxValue;
            return Mathf.Abs(Vector3.Dot(s.Landing - from, dir) - distance);
        }

        // 첫 바운드까지의 수평 거리. 네트에 걸리면 -1.
        static float Range(Vector3 from, Vector3 dir, float speed, float angle, Vector3 spin, in SurfaceProfile surface)
        {
            bool landed = Fly(from, Launch(dir, speed, angle), spin, surface, out Vector3 landing, out _);
            return landed ? Vector3.Dot(landing - from, dir) : -1f;
        }

        static bool Fly(Vector3 from, Vector3 velocity, Vector3 spin, in SurfaceProfile surface,
            out Vector3 landing, out float netClearance)
        {
            var s = new BallState(from, velocity, spin);
            netClearance = float.PositiveInfinity;
            for (int i = 0; i < MaxFlightSteps; i++)
            {
                StepEvents ev = BallPhysics.Integrate(ref s, surface);
                if (ev.HitNet)
                {
                    landing = s.Position;
                    netClearance = -1f;
                    return false;
                }
                if (ev.CrossedNet)
                    netClearance = ev.NetPoint.y - BallPhysics.Radius - Court.NetHeightAt(ev.NetPoint.x);
                if (ev.Bounced)
                {
                    landing = ev.BouncePoint;
                    return true;
                }
            }
            landing = s.Position;
            return false;
        }

        static Vector3 Launch(Vector3 dir, float speed, float angleDeg)
        {
            float r = angleDeg * Mathf.Deg2Rad;
            return dir * (Mathf.Cos(r) * speed) + Vector3.up * (Mathf.Sin(r) * speed);
        }

        static void Horizontal(Vector3 from, Vector3 target, out Vector3 dir, out float distance)
        {
            Vector3 d = target - from;
            d.y = 0f;
            distance = d.magnitude;
            dir = distance > 1e-4f ? d / distance : Vector3.forward;
        }
    }

    // 선입력(구질·방향·길이)을 상대 코트의 목표 지점으로 바꾸고 발사 속도를 구한다.
    public static class ShotPlanner
    {
        // power: 힘 스탯의 속도 배율, spinScale: 기술 스탯의 스핀 배율, aimMarginScale: 기술 스탯의 사이드라인 여유 배율 (1 = 기본)
        public static ShotSolution Plan(TennisConfig config, ShotIntent intent, Vector3 contact, int hitterSide,
            float power = 1f, float spinScale = 1f, float aimMarginScale = 1f)
        {
            float facing = Court.Facing(hitterSide);
            float aim = Mathf.Clamp(intent.Aim, -1f, 1f);
            float depth01 = Mathf.Clamp01(intent.Depth);
            float speedScale = intent.SpeedScale > 0f ? intent.SpeedScale : 1f;
            float minAngle = -12f;
            ShotProfile p;
            float depth, speed;

            switch (intent.Type)
            {
                case ShotType.Slice:
                    p = config.slice;
                    depth = Mathf.Lerp(p.minDepth, p.maxDepth, depth01);
                    // 짧게 칠수록 느려지되 드롭샷 쪽에서 크게 느려진다: 중간 길이 슬라이스도 낮고 빠르게 깔린다
                    speed = Mathf.Lerp(p.shortSpeed, p.speed, Mathf.Sqrt(depth01));
                    break;
                case ShotType.Lob:
                    p = config.lob;
                    depth = Mathf.Lerp(p.minDepth, p.maxDepth, depth01);
                    speed = p.speed;
                    break;
                case ShotType.Smash:
                    p = config.smash;
                    depth = Mathf.Lerp(p.maxDepth, p.minDepth, Mathf.Abs(aim));
                    speed = p.speed;
                    if (contact.y >= config.smashMinHeight)
                    {
                        minAngle = -35f;
                    }
                    else
                    {
                        // 낮은 공에 스매시 입력 → 강한 플랫 스트로크
                        speed *= 0.85f;
                        p.spin = 60f;
                    }
                    break;
                default:
                    p = config.topspin;
                    depth = Mathf.Lerp(p.maxDepth, p.minDepth, Mathf.Abs(aim)); // 각을 줄수록 짧게
                    speed = p.speed;
                    break;
            }

            // 전체 템포: 속도는 배율만큼, 스핀은 배율의 제곱만큼 줄인다
            float pace = config.shotSpeedScale > 0f ? config.shotSpeedScale : 1f;
            float spin = p.spin * pace * pace * spinScale;

            depth *= Court.LengthScale; // 구질별 깊이는 ITF 길이 기준
            float maxAimX = Court.PlayHalfWidth - config.aimMargin * aimMarginScale;
            var target = new Vector3(aim * maxAimX * facing, 0f, depth * facing);
            if (p.launchAngle > 0f)
                return ShotSolver.SolveFixedAngle(contact, target, p.launchAngle, spin, Court.Surface);
            return ShotSolver.SolveDrive(contact, target, speed * speedScale * pace * power, spin, Court.Surface, minAngle);
        }

        // 발리: 클릭 = 기본 발리, ↓ = 드롭 발리 (길이 무관 짧게), ↑ = 깊은 발리 (로브 아님). 스매시는 Plan으로.
        public static ShotSolution PlanVolley(TennisConfig config, ShotIntent intent, Vector3 contact, int hitterSide,
            float power = 1f, float spinScale = 1f, float aimMarginScale = 1f)
        {
            if (intent.Type == ShotType.Smash) return Plan(config, intent, contact, hitterSide, power, spinScale, aimMarginScale);

            float facing = Court.Facing(hitterSide);
            float aim = Mathf.Clamp(intent.Aim, -1f, 1f);
            ShotProfile p = config.volley;
            float depth, speed;
            switch (intent.Type)
            {
                case ShotType.Slice:
                    depth = p.minDepth;
                    speed = p.shortSpeed;
                    break;
                case ShotType.Lob:
                    depth = p.maxDepth;
                    speed = p.speed;
                    break;
                default:
                    depth = Mathf.Lerp(p.minDepth, p.maxDepth, 0.7f);
                    speed = p.speed;
                    break;
            }

            float pace = config.shotSpeedScale > 0f ? config.shotSpeedScale : 1f;
            depth *= Court.LengthScale;
            float maxAimX = Court.PlayHalfWidth - config.aimMargin * aimMarginScale;
            var target = new Vector3(aim * maxAimX * facing, 0f, depth * facing);
            return ShotSolver.SolveDrive(contact, target, speed * pace * power, p.spin * pace * pace * spinScale, Court.Surface, -25f);
        }

        // 서브: 대각선 서비스 박스의 서버 기준 왼쪽(-1)~가운데(0)~오른쪽(+1), 서비스라인 조금 안쪽을 노린다.
        public static ShotSolution PlanServe(TennisConfig config, ServeType type, float aim, Vector3 contact,
            int serverSide, bool deuce, float power = 1f, float spinScale = 1f)
        {
            int receiver = -serverSide;
            Court.ServiceBox(receiver, deuce, out float xMin, out float xMax);
            float facing = Court.Facing(serverSide);
            float inset = config.serveAimInset;
            float right = facing > 0f ? xMax - inset : xMin + inset; // 서버 기준 오른쪽 끝
            float left = facing > 0f ? xMin + inset : xMax - inset;
            float center = (xMin + xMax) * 0.5f;
            aim = Mathf.Clamp(aim, -1f, 1f);
            float x = aim >= 0f ? Mathf.Lerp(center, right, aim) : Mathf.Lerp(center, left, -aim);
            var target = new Vector3(x, 0f, (Court.ServiceLine - config.serveDepthInset) * receiver);

            ShotProfile p = type switch
            {
                ServeType.Slice => config.sliceServe,
                ServeType.Kick => config.kickServe,
                _ => config.flatServe,
            };
            float pace = config.shotSpeedScale > 0f ? config.shotSpeedScale : 1f;
            float speed = p.speed * pace * power;
            float sideSpin = -p.sideSpin * pace * pace * spinScale * facing; // + = 서버 기준 왼쪽으로 휨
            return ShotSolver.SolveDrive(contact, target, speed, p.spin * pace * pace * spinScale, Court.Surface, -20f, sideSpin);
        }
    }
}
