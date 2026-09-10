using UnityEngine;

namespace GroundStation.UI
{
    /// <summary>A continuous press confirms once, and must be released to rearm.</summary>
    public sealed class HoldToConfirm
    {
        private bool _pressed, _confirmed;
        private float _startedAt;
        public float Progress { get; private set; }

        public bool Tick(bool pressed, float now, float duration)
        {
            if (!pressed) { Reset(); return false; }
            if (!_pressed) { _pressed = true; _startedAt = now; }
            Progress = Mathf.Clamp01((now - _startedAt) / Mathf.Max(0.1f, duration));
            if (_confirmed || Progress < 1f) return false;
            _confirmed = true;
            return true;
        }

        public void Reset() { _pressed = _confirmed = false; Progress = 0f; }
    }
}
