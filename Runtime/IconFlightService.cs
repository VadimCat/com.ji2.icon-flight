using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Ji2.IconFlight
{
    /// <summary>Timing and size of one flight, in seconds and canvas units.</summary>
    [Serializable]
    public struct IconFlightStyle
    {
        [Tooltip("Icon size when it appears, in canvas units.")] public float StartSize;
        [Tooltip("Overshoot scale of the appearing pop.")] public float PopScale;
        public float PopSeconds;
        public float FlightSeconds;
        [Tooltip("Sideways bow of the arc, in canvas units.")] public float ArcHeight;
        [Tooltip("Arrival size as a fraction of the target's smaller side. 0 keeps the start size.")] public float LandSizeOfTarget;
        [Tooltip("Scale punch applied to the target on landing. 0 leaves the target to the caller.")] public float TargetPunch;
        public float TargetPunchSeconds;

        public static IconFlightStyle Default => new()
        {
            StartSize = 96, PopScale = 1.25f, PopSeconds = .1f, FlightSeconds = .5f, ArcHeight = 140,
            LandSizeOfTarget = .7f, TargetPunch = .14f, TargetPunchSeconds = .24f
        };
    }

    /// <summary>
    /// Flies pooled sprite miniatures from a screen point to a live HUD target along an arc and
    /// calls back when each lands. The caller owns timing (<see cref="Tick"/>), which keeps the
    /// service usable with paused or unscaled clocks. Presentation only: the caller's domain state
    /// should already be updated when a flight starts.
    /// </summary>
    public sealed class IconFlightService : IDisposable
    {
        private sealed class Flight
        {
            public RectTransform Rect;
            public Image Image;
            public RectTransform Target;
            public Vector2 From;
            public float Delay, Age;
            public IconFlightStyle Style;
            public Action Landed;
        }

        private sealed class Punch
        {
            public Vector3 BaseScale;
            public float Age, Seconds, Strength;
        }

        private readonly RectTransform layer;
        private readonly List<Flight> active = new();
        private readonly Stack<Flight> pool = new();
        private readonly Dictionary<RectTransform, Punch> punches = new();
        private readonly List<RectTransform> finishedPunches = new();
        private bool disposed;

        public IconFlightStyle Style = IconFlightStyle.Default;

        /// <summary>When true (reduced motion), launches land at once without an animation.</summary>
        public bool Instant { get; set; }

        public int ActiveCount => active.Count;

        /// <param name="layer">A full-screen RectTransform drawn above the HUD. Flights are its children.</param>
        public IconFlightService(RectTransform layer)
        {
            this.layer = layer != null ? layer : throw new ArgumentNullException(nameof(layer));
        }

        /// <summary>Starts a flight from a screen-space point (pixels).</summary>
        public void LaunchFromScreen(Sprite icon, Vector2 screenPoint, RectTransform target, Action landed = null, float delay = 0, IconFlightStyle? style = null)
        {
            if (disposed || target == null) return;
            var canvas = layer.GetComponentInParent<Canvas>();
            var camera = canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(layer, screenPoint, camera, out var local);
            Launch(icon, local, target, landed, delay, style);
        }

        /// <summary>Starts a flight from a world position seen by <paramref name="worldCamera"/>.</summary>
        public void LaunchFromWorld(Sprite icon, Vector3 worldPoint, Camera worldCamera, RectTransform target, Action landed = null, float delay = 0, IconFlightStyle? style = null)
        {
            if (worldCamera == null) { Launch(icon, Vector2.zero, target, landed, delay, style); return; }
            LaunchFromScreen(icon, worldCamera.WorldToScreenPoint(worldPoint), target, landed, delay, style);
        }

        /// <summary>Starts a flight from a point in the layer's local space.</summary>
        public void Launch(Sprite icon, Vector2 layerPoint, RectTransform target, Action landed = null, float delay = 0, IconFlightStyle? style = null)
        {
            if (disposed || target == null) return;
            var s = style ?? Style;
            if (Instant)
            {
                StartPunch(target, s);
                landed?.Invoke();
                return;
            }
            var flight = pool.Count > 0 ? pool.Pop() : Create();
            flight.Image.sprite = icon;
            flight.Target = target;
            flight.From = layerPoint;
            flight.Delay = Mathf.Max(0, delay);
            flight.Age = 0;
            flight.Style = s;
            flight.Landed = landed;
            flight.Rect.SetAsLastSibling();
            flight.Rect.localPosition = layerPoint;
            flight.Rect.sizeDelta = Vector2.one * s.StartSize;
            flight.Rect.localScale = Vector3.zero;
            flight.Rect.gameObject.SetActive(true);
            active.Add(flight);
        }

        public void Tick(float deltaTime)
        {
            if (disposed) return;
            for (var i = active.Count - 1; i >= 0; i--)
            {
                var f = active[i];
                if (f.Target == null) { Recycle(i, false); continue; }
                if (f.Delay > 0) { f.Delay -= deltaTime; if (f.Delay > 0) continue; }
                f.Age += deltaTime;
                var to = TargetPoint(f.Target);
                var pop = f.Style.PopSeconds;
                if (f.Age < pop)
                {
                    var p = f.Age / Mathf.Max(.0001f, pop);
                    f.Rect.localPosition = f.From;
                    f.Rect.localScale = Vector3.one * Mathf.Lerp(.4f, f.Style.PopScale, 1 - (1 - p) * (1 - p));
                    continue;
                }
                var t = (f.Age - pop) / Mathf.Max(.0001f, f.Style.FlightSeconds);
                if (t >= 1) { StartPunch(f.Target, f.Style); Recycle(i, true); continue; }
                var e = IconFlightPath.Ease(t);
                f.Rect.localPosition = IconFlightPath.Evaluate(f.From, IconFlightPath.Control(f.From, to, f.Style.ArcHeight), to, e);
                var land = LandSize(f);
                var size = Mathf.Lerp(f.Style.StartSize * f.Style.PopScale, land, e);
                f.Rect.sizeDelta = Vector2.one * size;
                f.Rect.localScale = Vector3.one;
            }
            TickPunches(deltaTime);
        }

        /// <summary>Lands every flight now and reports each landing (for example before a results screen).</summary>
        public void CompleteAll()
        {
            for (var i = active.Count - 1; i >= 0; i--) Recycle(i, active[i].Target != null);
            RestorePunches();
        }

        /// <summary>Drops every flight without callbacks (for example when the HUD is rebuilt).</summary>
        public void CancelAll()
        {
            for (var i = active.Count - 1; i >= 0; i--) Recycle(i, false);
            RestorePunches();
        }

        public void Dispose()
        {
            if (disposed) return;
            CancelAll();
            foreach (var f in pool) if (f.Rect != null) UnityEngine.Object.Destroy(f.Rect.gameObject);
            pool.Clear();
            disposed = true;
        }

        private Flight Create()
        {
            var go = new GameObject("Icon flight", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(layer, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            var image = go.GetComponent<Image>();
            image.raycastTarget = false;
            image.preserveAspect = true;
            return new Flight { Rect = rect, Image = image };
        }

        private Vector2 TargetPoint(RectTransform target) => layer.InverseTransformPoint(target.TransformPoint(target.rect.center));

        private float LandSize(Flight f)
        {
            if (f.Style.LandSizeOfTarget <= 0) return f.Style.StartSize;
            var r = f.Target.rect;
            var lossy = f.Target.lossyScale.x / Mathf.Max(.0001f, layer.lossyScale.x);
            return Mathf.Min(r.width, r.height) * lossy * f.Style.LandSizeOfTarget;
        }

        private void Recycle(int index, bool landed)
        {
            var f = active[index];
            active.RemoveAt(index);
            f.Rect.gameObject.SetActive(false);
            var callback = f.Landed;
            f.Landed = null;
            f.Target = null;
            f.Image.sprite = null;
            pool.Push(f);
            if (landed) callback?.Invoke();
        }

        private void StartPunch(RectTransform target, IconFlightStyle style)
        {
            if (style.TargetPunch <= 0 || style.TargetPunchSeconds <= 0 || target == null) return;
            if (!punches.TryGetValue(target, out var punch))
            {
                punch = new Punch { BaseScale = target.localScale };
                punches.Add(target, punch);
            }
            punch.Age = 0;
            punch.Seconds = style.TargetPunchSeconds;
            punch.Strength = style.TargetPunch;
        }

        private void TickPunches(float deltaTime)
        {
            finishedPunches.Clear();
            foreach (var pair in punches)
            {
                var punch = pair.Value;
                if (pair.Key == null) { finishedPunches.Add(pair.Key); continue; }
                punch.Age += deltaTime;
                var t = punch.Age / punch.Seconds;
                if (t >= 1) { pair.Key.localScale = punch.BaseScale; finishedPunches.Add(pair.Key); continue; }
                pair.Key.localScale = punch.BaseScale * (1 + punch.Strength * Mathf.Sin(t * Mathf.PI));
            }
            foreach (var key in finishedPunches) punches.Remove(key);
        }

        private void RestorePunches()
        {
            foreach (var pair in punches) if (pair.Key != null) pair.Key.localScale = pair.Value.BaseScale;
            punches.Clear();
        }
    }
}
