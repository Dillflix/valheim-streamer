namespace ValheimBroadcaster.Plugin;

/// <summary>ZNet exists while a client is connecting, before PeerInfo creates
/// its World. GetWorldUID dereferences that absent World; GetWorld safely
/// exposes the lifecycle boundary. Zero retains the existing unbound state.</summary>
internal static class WorldIdentity
{
    internal static long Current => Read(ZNet.instance);
    internal static long Read(ZNet? net) => net ? net.GetWorld()?.m_uid ?? 0 : 0;
}
