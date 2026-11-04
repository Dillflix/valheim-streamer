using UnityEngine;

namespace ValheimBroadcaster.Plugin;

// Valheim's built-in render pipeline invokes this after existing camera effects.
// Keep this callback on the Camera object, not on the BepInEx plugin object.
internal sealed class FrameHoldRenderer : MonoBehaviour
{
    internal FrameHold? Owner;
    private void OnPreRender() => Owner?.BeforeRender(this);
    private void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        if (Owner != null) Owner.Render(source, destination);
        else Graphics.Blit(source, destination);
    }
    private void OnDisable() => Owner?.LostRenderer(this);
    private void OnDestroy() => Owner?.LostRenderer(this);
}
