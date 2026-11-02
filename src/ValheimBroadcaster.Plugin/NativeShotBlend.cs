using Unity.Cinemachine;
using UnityEngine;

namespace ValheimBroadcaster.Plugin;

/// <summary>
/// Native blend mechanics over the adapter's already evaluated camera states.
/// The director owns presentation time and validates the resulting pose before
/// it reaches the game camera; sampling never advances either virtual camera.
/// </summary>
internal sealed class NativeShotBlend
{
    private readonly SnapshotCamera outgoing = new SnapshotCamera("Displayed source");
    private readonly SnapshotCamera incoming = new SnapshotCamera("Validated destination");
    private readonly CinemachineBlend blend = new CinemachineBlend();
    private float duration = -1;

    internal void Start(CameraState from, float seconds)
    {
        Reset();
        outgoing.Set(from);
        incoming.Set(from);
        Configure(seconds);
    }

    internal CameraState Sample(CameraState fromUpdated, CameraState to, float elapsed, float seconds)
    {
        outgoing.Set(fromUpdated);
        incoming.Set(to);
        Configure(seconds);
        // An invalid clock cannot revive a previous blend or poison its curve.
        blend.TimeInBlend = float.IsNaN(elapsed) || float.IsInfinity(elapsed)
            ? duration : Mathf.Clamp(elapsed, 0, duration);
        return blend.State;
    }

    private void Configure(float seconds)
    {
        float next = float.IsNaN(seconds) || float.IsInfinity(seconds) ? 0 : Mathf.Max(0, seconds);
        if (duration != next)
        {
            var definition = new CinemachineBlendDefinition(next > 0
                ? CinemachineBlendDefinition.Styles.EaseInOut : CinemachineBlendDefinition.Styles.Cut, next);
            duration = blend.Duration = definition.BlendTime;
            blend.BlendCurve = definition.BlendCurve;
        }
        blend.CamA = outgoing;
        blend.CamB = incoming;
    }

    internal void Reset()
    {
        blend.ClearBlend();
        blend.CamB = null;
        outgoing.Clear();
        incoming.Clear();
        duration = -1;
    }

    private sealed class SnapshotCamera : ICinemachineCamera
    {
        internal SnapshotCamera(string name) { Name = name; }
        public string Name { get; }
        public string Description => Name;
        public CameraState State { get; private set; }
        public bool IsValid { get; private set; }
        public ICinemachineMixer ParentCamera => null!;

        internal void Set(CameraState state) { State = state; IsValid = true; }
        internal void Clear() { State = default; IsValid = false; }
        public void UpdateCameraState(Vector3 worldUp, float deltaTime) { }
        public void OnCameraActivated(ICinemachineCamera.ActivationEventParams evt) { }
    }
}
