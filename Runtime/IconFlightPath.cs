using UnityEngine;

namespace Ji2.IconFlight
{
    /// <summary>Pure flight geometry, shared by the service and its tests.</summary>
    public static class IconFlightPath
    {
        /// <summary>
        /// Control point of a quadratic arc from <paramref name="from"/> to <paramref name="to"/>.
        /// The arc bows sideways, away from the target's horizontal side, so a straight-up
        /// flight still curves and neighbouring flights do not overlap the same line.
        /// </summary>
        public static Vector2 Control(Vector2 from, Vector2 to, float arcHeight)
        {
            var delta = to - from;
            var mid = (from + to) * .5f;
            if (delta.sqrMagnitude < 1e-6f) return mid + Vector2.up * arcHeight;
            var side = new Vector2(-delta.y, delta.x).normalized;
            // Bow outward: when the target lies to the left, the arc swings right first, and vice versa.
            if (side.x * delta.x > 0) side = -side;
            return mid + side * arcHeight;
        }

        public static Vector2 Evaluate(Vector2 from, Vector2 control, Vector2 to, float t)
        {
            t = Mathf.Clamp01(t);
            var u = 1 - t;
            return u * u * from + 2 * u * t * control + t * t * to;
        }

        /// <summary>Slow start, quick middle, soft arrival.</summary>
        public static float Ease(float t)
        {
            t = Mathf.Clamp01(t);
            return t < .5f ? 4 * t * t * t : 1 - Mathf.Pow(-2 * t + 2, 3) * .5f;
        }

        /// <summary>A pop that overshoots to <paramref name="peak"/> and settles back to 1.</summary>
        public static float Pop(float t, float peak)
        {
            t = Mathf.Clamp01(t);
            return 1 + (peak - 1) * Mathf.Sin(t * Mathf.PI);
        }
    }
}
