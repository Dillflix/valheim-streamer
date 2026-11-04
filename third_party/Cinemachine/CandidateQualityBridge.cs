// Mod-owned bridge. The pinned source preparation marks CinemachineCamera
// partial; the upstream pose solver and State getter otherwise stay unchanged.
namespace Unity.Cinemachine;

public sealed partial class CinemachineCamera
{
    /// <summary>
    /// Temporarily supplies externally evaluated shot quality to a native
    /// ClearShot ranking pass, without updating the camera again. The caller
    /// must restore the returned value in finally before any other camera work.
    /// </summary>
    public float SwapEvaluatedShotQuality(float value)
    {
        float previous = m_State.ShotQuality;
        m_State.ShotQuality = value;
        return previous;
    }
}
