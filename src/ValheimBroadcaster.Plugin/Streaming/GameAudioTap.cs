using UnityEngine;

namespace ValheimBroadcaster.Plugin.Streaming;

// Installed only on the existing game's AudioListener. Do not clear or modify data:
// the observer must still hear normal local playback. No microphone is opened.
public sealed class GameAudioTap : MonoBehaviour
{
    internal volatile EncoderSession? Session;
    private void OnAudioFilterRead(float[] data, int channels) => Session?.SubmitAudio(data, channels);
}
