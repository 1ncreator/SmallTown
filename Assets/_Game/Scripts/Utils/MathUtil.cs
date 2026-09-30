using System;

namespace SmallTown.Utils
{
    /// <summary>Minimal 2D vector on the ground plane (X east, Z north). Engine independent.</summary>
    public struct Vec2
    {
        public float X;
        public float Z;

        public Vec2(float x, float z)
        {
            X = x;
            Z = z;
        }

        public static Vec2 operator +(Vec2 a, Vec2 b) => new Vec2(a.X + b.X, a.Z + b.Z);
        public static Vec2 operator -(Vec2 a, Vec2 b) => new Vec2(a.X - b.X, a.Z - b.Z);
        public static Vec2 operator *(Vec2 a, float s) => new Vec2(a.X * s, a.Z * s);
        public float Length => MathF.Sqrt(X * X + Z * Z);
        public float SqrLength => X * X + Z * Z;

        public Vec2 Normalized
        {
            get
            {
                float l = Length;
                return l > 1e-6f ? new Vec2(X / l, Z / l) : new Vec2(0, 0);
            }
        }

        /// <summary>Right-hand perpendicular for right-hand traffic (heading north gives east).</summary>
        public Vec2 Right => new Vec2(Z, -X);

        public static float Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Z * b.Z;
        public static float Cross(Vec2 a, Vec2 b) => a.X * b.Z - a.Z * b.X;
        public static float Distance(Vec2 a, Vec2 b) => (a - b).Length;
        public static Vec2 Lerp(Vec2 a, Vec2 b, float t) => new Vec2(a.X + (b.X - a.X) * t, a.Z + (b.Z - a.Z) * t);
    }

    public static class MathUtil
    {
        public const float Pi = 3.14159265f;

        public static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);
        public static int Clamp(int v, int min, int max) => v < min ? min : (v > max ? max : v);
        public static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
        public static float Lerp(float a, float b, float t) => a + (b - a) * t;

        public static float MoveTowards(float current, float target, float maxDelta)
        {
            if (MathF.Abs(target - current) <= maxDelta) return target;
            return current + MathF.Sign(target - current) * maxDelta;
        }

        public static float SmoothStep(float a, float b, float x)
        {
            float t = Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        /// <summary>Heading in degrees, 0 = north (+Z), clockwise (90 = east).</summary>
        public static float HeadingDeg(float dx, float dz)
        {
            if (dx * dx + dz * dz < 1e-8f) return 0f;
            return MathF.Atan2(dx, dz) * (180f / Pi);
        }

        public static float DeltaAngle(float a, float b)
        {
            float d = (b - a) % 360f;
            if (d > 180f) d -= 360f;
            if (d < -180f) d += 360f;
            return d;
        }

        public static float LerpAngle(float a, float b, float t) => a + DeltaAngle(a, b) * t;

        /// <summary>Quadratic Bezier point.</summary>
        public static Vec2 Bezier(Vec2 p0, Vec2 p1, Vec2 p2, float t)
        {
            float u = 1f - t;
            return new Vec2(
                u * u * p0.X + 2f * u * t * p1.X + t * t * p2.X,
                u * u * p0.Z + 2f * u * t * p1.Z + t * t * p2.Z);
        }

        public static Vec2 BezierTangent(Vec2 p0, Vec2 p1, Vec2 p2, float t)
        {
            float u = 1f - t;
            return new Vec2(
                2f * u * (p1.X - p0.X) + 2f * t * (p2.X - p1.X),
                2f * u * (p1.Z - p0.Z) + 2f * t * (p2.Z - p1.Z));
        }

        public static string FormatHour(float hour)
        {
            hour = ((hour % 24f) + 24f) % 24f;
            int h = (int)hour;
            int m = (int)((hour - h) * 60f);
            if (m >= 60) m = 59;
            return h.ToString("00") + ":" + m.ToString("00");
        }
    }
}
