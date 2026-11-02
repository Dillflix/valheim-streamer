using System;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

/// <summary>
/// Uses native ClearShot to rank the actual evaluated shot cameras. The manager
/// never advances child solvers or writes the rendered camera. Presentation
/// pacing and validated transition adoption remain the director's job.
/// </summary>
internal sealed class NativeClearShotRanker : IShotCandidateRanker, IDisposable
{
    private readonly GameObject owner;
    private readonly EvaluatedClearShot manager;
    private readonly CinemachineCamera[] cameras;
    private readonly float[] previousQuality;
    private readonly PrioritySettings[] previousPriority;
    private bool bound, disposed;
    internal Transform CameraParent => owner.transform;
    internal string Diagnostics { get; private set; } = "ClearShot: awaiting evaluated candidates";

    internal NativeClearShotRanker(Transform parent, int count)
    {
        if (!parent) throw new ArgumentNullException(nameof(parent));
        if (count < 1 || count > ProceduralShotPolicy.MaximumCandidates) throw new ArgumentOutOfRangeException(nameof(count));
        cameras = new CinemachineCamera[count];
        previousQuality = new float[count];
        previousPriority = new PrioritySettings[count];
        owner = new GameObject("Broadcast native ClearShot") { hideFlags = HideFlags.HideAndDontSave };
        owner.transform.SetParent(parent, false);
        manager = owner.AddComponent<EvaluatedClearShot>();
        manager.enabled = false;
        manager.StandbyUpdate = CinemachineVirtualCameraBase.StandbyUpdateMode.Never;
        manager.RandomizeChoice = false;
        // Native wall-clock dwell would count held/slate footage and conflict
        // with usable-presentation clocks. Only its candidate ranking is used.
        manager.ActivateAfter = manager.MinDuration = 0;
        manager.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0);
    }

    internal void Bind(IReadOnlyList<CinemachineCamera> value)
    {
        if (bound || disposed) throw new InvalidOperationException("ClearShot cameras may only be bound once.");
        if (value == null || value.Count != cameras.Length) throw new ArgumentException("Supply every stable shot camera.", nameof(value));
        for (int i = 0; i < value.Count; i++)
        {
            if (!value[i] || value[i].transform.parent != CameraParent)
                throw new ArgumentException("ClearShot candidates must be actual direct child cameras.", nameof(value));
            cameras[i] = value[i];
        }
        // Warm the native hierarchy cache before cameras receive their first
        // solve. Rebuilding it later would invalidate their damping history.
        var children = manager.ChildCameras;
        children.Clear();
        for (int i = 0; i < cameras.Length; i++) children.Add(cameras[i]);
        bound = true;
    }

    public int Best(IReadOnlyList<float> scores, IReadOnlyList<bool> eligible,
        IReadOnlyList<bool>? readable, int incumbent)
    {
        if (!bound || disposed) throw new InvalidOperationException("ClearShot has no bound camera set.");
        if (scores.Count != cameras.Length || eligible.Count != cameras.Length || readable != null && readable.Count != cameras.Length)
            throw new ArgumentException("ClearShot evidence must match its stable camera set.");
        var children = manager.ChildCameras;
        children.Clear();
        int prepared = 0, admitted = 0, result = -1;
        try
        {
            for (int i = 0; i < cameras.Length; i++)
            {
                previousPriority[i] = cameras[i].Priority;
                previousQuality[i] = cameras[i].SwapEvaluatedShotQuality(
                    ShotCandidateRanking.NativeQuality(scores[i], readable != null && readable[i]));
                prepared = i + 1;
                // Native ClearShot keeps an incumbent on a quality tie. Its
                // manager is used as a stateless ranker, so pass that preference
                // explicitly through its native priority tie-breaker.
                cameras[i].Priority.Value = i == incumbent ? 1 : 0;
                if (eligible[i]) { children.Add(cameras[i]); admitted++; }
            }
            // Native ClearShot compares actual cached camera State.ShotQuality.
            // Filtering its child list excludes physical failures without ever
            // turning foliage into a scene-loading failure or toggling objects.
            var best = manager.ChooseEvaluatedCamera();
            if (best != null)
                for (int i = 0; i < cameras.Length; i++)
                    if (best == cameras[i]) { result = i; break; }
            Diagnostics = result < 0 ? "ClearShot: no physically admitted camera" :
                $"ClearShot: ranked {admitted}/{cameras.Length}; winner {result + 1}; presentation pacing retained";
            return result;
        }
        finally
        {
            for (int i = 0; i < prepared; i++)
            {
                cameras[i].SwapEvaluatedShotQuality(previousQuality[i]);
                cameras[i].Priority = previousPriority[i];
            }
            children.Clear();
            for (int i = 0; i < cameras.Length; i++) children.Add(cameras[i]);
        }
    }

    internal void Reset() => Diagnostics = "ClearShot: awaiting fresh evaluated candidates";

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (owner) UnityEngine.Object.Destroy(owner);
    }
}

/// <summary>Expose the upstream protected ranking operation without invoking
/// its manager update/blend loop or bypassing the rig's presentation checks.</summary>
internal sealed class EvaluatedClearShot : CinemachineClearShot
{
    internal CinemachineVirtualCameraBase ChooseEvaluatedCamera() => base.ChooseCurrentCamera(Vector3.up, -1);
}
