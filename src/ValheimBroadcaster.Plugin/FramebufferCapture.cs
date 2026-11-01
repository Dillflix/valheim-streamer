using UnityEngine;

namespace ValheimBroadcaster.Plugin;

internal static class FramebufferCapture
{
    // Normalize the screen texture through Unity's single-texture blit path.
    // A raw backbuffer copy can have a different vertical convention from a
    // regular texture sampled by GUI. Do not apply a global/platform-only flip:
    // the live camera and encoded output may already have the correct orientation.
    internal static void Copy(RenderTexture screenshot, RenderTexture destination)
    {
        RenderTexture previousTarget = RenderTexture.active;
        bool previousSrgb = GL.sRGBWrite;
        try
        {
            ScreenCapture.CaptureScreenshotIntoRenderTexture(screenshot);
            GL.sRGBWrite = destination.sRGB;
            Graphics.Blit(screenshot, destination);
        }
        finally
        {
            // The hold and stream coroutines share the graphics context. Neither
            // may leave its output bound for the next capture or game render.
            RenderTexture.active = previousTarget;
            GL.sRGBWrite = previousSrgb;
        }
    }
}
