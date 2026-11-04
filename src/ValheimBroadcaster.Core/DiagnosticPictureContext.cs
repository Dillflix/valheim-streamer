namespace ValheimBroadcaster.Core;

/// <summary>Metadata owned by the rendered source, not by a later GPU callback.
/// Identifiers are numeric shot/scene identities, never player/account names.</summary>
public readonly struct DiagnosticPictureContext
{
    public readonly string Mode, Perspective, Reason, Intent, Encounter, Coverage;
    public readonly long Shot, Revision;
    public readonly long PictureScene;
    public readonly int Candidate;
    public readonly bool Usable;
    public readonly double X, Y, Z, Pitch, Yaw, Roll, Fov;
    public readonly int Hostiles, Participants, Framed, VisibleOpponents;
    public readonly double ActorOcclusion;
    public readonly CameraRenderAudit RenderAudit;

    public DiagnosticPictureContext(string mode, long shot, long revision, bool usable,
        string reason, double x = 0, double y = 0, double z = 0,
        double pitch = 0, double yaw = 0, double roll = 0, double fov = 0, string perspective = "",
        long pictureScene = 0, int candidate = -1, string intent = "", string encounter = "", string coverage = "",
        int hostiles = 0, int participants = 0, int framed = 0, int visibleOpponents = 0, double actorOcclusion = 0,
        CameraRenderAudit renderAudit = default)
    {
        Mode = mode ?? "Unknown"; Shot = shot; Revision = revision; Usable = usable;
        Reason = reason ?? ""; X = x; Y = y; Z = z; Pitch = pitch; Yaw = yaw; Roll = roll;
        Fov = fov; Perspective = perspective ?? "";
        PictureScene = pictureScene; Candidate = candidate;
        Intent = intent ?? "";
        Encounter = encounter ?? ""; Coverage = coverage ?? "";
        Hostiles = hostiles; Participants = participants; Framed = framed; VisibleOpponents = visibleOpponents;
        ActorOcclusion = actorOcclusion;
        RenderAudit = renderAudit;
    }

    public DiagnosticPictureContext WithRenderAudit(CameraRenderAudit audit) => new DiagnosticPictureContext(
        Mode, Shot, Revision, Usable, Reason, X, Y, Z, Pitch, Yaw, Roll, Fov, Perspective, PictureScene, Candidate,
        Intent, Encounter, Coverage, Hostiles, Participants, Framed, VisibleOpponents, ActorOcclusion, audit);

    public CameraTraceRecord Append(CameraTraceRecord row) => RenderAudit.Append(row.Field("mode", Mode)
        .Field("shot", Shot).Field("revision", Revision).Field("usable", Usable)
        .Field("pictureScene", PictureScene).Field("candidate", Candidate)
        .Field("reason", Reason).Field("perspective", Perspective)
        .Field("intent", Intent)
        .Field("encounter", Encounter).Field("coverage", Coverage)
        .Field("hostiles", Hostiles).Field("participants", Participants)
        .Field("framed", Framed).Field("visibleOpponents", VisibleOpponents).Field("actorOcclusion", ActorOcclusion)
        .Field("cameraX", X).Field("cameraY", Y).Field("cameraZ", Z)
        .Field("pitch", Pitch).Field("yaw", Yaw).Field("roll", Roll).Field("fov", Fov));
}
