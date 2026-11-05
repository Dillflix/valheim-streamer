using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ValheimBroadcaster.Core;

/// <summary>Bounded, versioned fixture serialization with exact float/double
/// round trips. Decode constructs a complete validated object before returning.</summary>
public static class CameraFixtureCodec
{
    public const int MaximumBytes = 32768;
    private const int Magic = 0x56424346;
    private const int DigestBytes = 32;
    private const int MaximumContinuationReason = 512;
    private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

    public static byte[] Encode(CameraFixture fixture)
    {
        if (fixture == null) throw new ArgumentNullException(nameof(fixture));
        using var payload = new MemoryStream();
        using (var w = new BinaryWriter(payload, Utf8, true))
        {
            w.Write(Magic); w.Write(CameraFixture.SchemaVersion); w.Write(fixture.WorldId);
            Text(w, fixture.Name, 48); Text(w, fixture.SourceVersion, 128); Text(w, fixture.SettingsSummary, 4096);
            w.Write(fixture.CreatedUtc.Ticks); w.Write(fixture.MotionMultiplier); w.Write(fixture.DynamicMovement);
            ScenicShotPlan p = fixture.Plan;
            w.Write(p.Id); w.Write((byte)(p.IsSpatial ? 1 : p.IsPan ? 2 : 0)); w.Write((byte)p.Role);
            w.Write(p.UseNativeSpline); w.Write(p.DurationSeconds); w.Write(p.FieldOfView);
            ScenicShotSample start = p.Sample(0), end = p.Sample(1);
            w.Write(start.ScreenX); w.Write(start.ScreenY);
            w.Write(p.ConstantSpeed); w.Write(p.JourneyEaseIn); w.Write(p.JourneyEaseOut);
            w.Write(fixture.SourceJourneyId); w.Write(fixture.SourcePredecessorId);
            Feature(w, p.Feature!);
            if (p.IsSpatial)
            {
                w.Write((byte)p.WaypointCount);
                for (int i = 0; i < p.WaypointCount; i++) { Point(w, p.CameraWaypoint(i)); Point(w, p.AimWaypoint(i)); }
            }
            else
            {
                Point(w, start.CameraPosition); Point(w, end.CameraPosition);
                Point(w, start.AimPosition); Point(w, end.AimPosition);
                w.Write(p.CameraControl.HasValue); if (p.CameraControl.HasValue) Point(w, p.CameraControl.Value);
            }
            Text(w, p.ContinuationRejection, MaximumContinuationReason);
            w.Write(p.PacingProfile != null);
            if (p.PacingProfile != null)
            {
                ScenicPacingProfile pace = p.PacingProfile;
                w.Write(pace.DurationSeconds); w.Write((ushort)pace.Count);
                for (int i = 0; i < pace.Count; i++) { w.Write(pace.TimeFraction(i)); w.Write(pace.DistanceFraction(i)); }
            }
            // These are validation contracts, independent of whether a saved
            // Journey window still belongs to an active runtime mission.
            w.Write(p.StableWaterClearance); w.Write(p.UsesJourneyView); w.Write(p.JourneyCruiseSpeed);
        }
        if (payload.Length > MaximumBytes - DigestBytes) throw new ArgumentException("Camera fixture is too large.", nameof(fixture));
        byte[] body = payload.ToArray();
        using var sha = SHA256.Create(); byte[] digest = sha.ComputeHash(body);
        var result = new byte[body.Length + DigestBytes];
        Buffer.BlockCopy(body, 0, result, 0, body.Length); Buffer.BlockCopy(digest, 0, result, body.Length, DigestBytes);
        return result;
    }

    public static CameraFixture Decode(byte[] bytes, long expectedWorldId)
    {
        if (bytes == null || bytes.Length < 64 || bytes.Length > MaximumBytes)
            throw new InvalidDataException("Camera fixture has an invalid size.");
        using var sha = SHA256.Create();
        byte[] digest = sha.ComputeHash(bytes, 0, bytes.Length - DigestBytes);
        int difference = 0;
        for (int i = 0; i < DigestBytes; i++) difference |= digest[i] ^ bytes[bytes.Length - DigestBytes + i];
        if (difference != 0) throw new InvalidDataException("Camera fixture checksum failed; the file is incomplete or changed.");
        try
        {
            using var stream = new MemoryStream(bytes, 0, bytes.Length - DigestBytes, false);
            using var r = new BinaryReader(stream, Utf8, true);
            if (r.ReadInt32() != Magic) throw new InvalidDataException("Not a broadcaster camera fixture.");
            int schema = r.ReadInt32();
            if (schema < 1 || schema > CameraFixture.SchemaVersion) throw new InvalidDataException("Unsupported camera fixture schema.");
            long world = r.ReadInt64();
            if (world == 0 || world != expectedWorldId) throw new InvalidDataException("Camera fixture world UID does not match this world.");
            string name = Text(r, 48), version = Text(r, 128), settings = Text(r, 4096);
            DateTime created = new DateTime(r.ReadInt64(), DateTimeKind.Utc);
            float motion = r.ReadSingle(); bool dynamic = Flag(r);
            long id = r.ReadInt64(); byte kind = r.ReadByte(); ScenicShotRole role = (ScenicShotRole)r.ReadByte();
            bool native = Flag(r); double duration = r.ReadDouble(); float fov = r.ReadSingle(), x = r.ReadSingle(), y = r.ReadSingle();
            bool constantSpeed = Flag(r), easeIn = Flag(r), easeOut = Flag(r);
            long journeyId = r.ReadInt64(), predecessor = r.ReadInt64();
            if (kind > 2 || !Enum.IsDefined(typeof(ScenicShotRole), role) || journeyId < 0 || predecessor < 0 || journeyId == 0 && predecessor != 0)
                throw new InvalidDataException("Invalid fixture plan kind or journey identity.");
            ScenicFeature feature = Feature(r);
            ScenicShotPlan plan;
            if (kind == 1)
            {
                int count = r.ReadByte();
                if (count < 2 || count > 16) throw new InvalidDataException("Camera fixture must have 2–16 paired waypoints.");
                var body = new AutonomyPosition[count]; var aim = new AutonomyPosition[count];
                for (int i = 0; i < count; i++) { body[i] = Point(r); aim[i] = Point(r); }
                plan = ScenicShotPlan.Spatial(id, feature, role, body, aim, fov, x, y, native);
            }
            else
            {
                AutonomyPosition bodyStart = Point(r), bodyEnd = Point(r), aimStart = Point(r), aimEnd = Point(r);
                AutonomyPosition? control = Flag(r) ? Point(r) : (AutonomyPosition?)null;
                if (kind == 2 && (bodyStart.Distance(bodyEnd) != 0 || native || control.HasValue))
                    throw new InvalidDataException("A pan fixture must keep one camera position and have no spline control.");
                plan = kind == 2 ? ScenicShotPlan.Pan(id, feature, role, bodyStart, aimStart, aimEnd, fov, x, y) :
                    ScenicShotPlan.Directed(id, feature, role, bodyStart, bodyEnd, aimStart, aimEnd, fov, x, y, control, native);
            }
            plan = plan.WithDuration(duration).WithConstantSpeed(constantSpeed).WithJourneyRamps(easeIn, easeOut);
            // Schema 1 never recorded the evidence beyond its saved endpoint.
            // An absent reason is unknown, not proof of a clear continuation.
            if (schema >= 2)
            {
                string continuationRejection = Text(r, MaximumContinuationReason);
                if (continuationRejection.Length != 0) plan = plan.WithRejectedContinuation(continuationRejection);
            }
            if (schema >= 3 && Flag(r))
            {
                double pacingSeconds = r.ReadDouble(); int count = r.ReadUInt16();
                if (count < 2 || count > ScenicPacingProfile.MaximumPoints || plan.IsPan)
                    throw new InvalidDataException("Invalid fixture pacing table.");
                var times = new double[count]; var distances = new double[count];
                for (int i = 0; i < count; i++) { times[i] = r.ReadDouble(); distances[i] = r.ReadDouble(); }
                plan = plan.WithPacingProfile(ScenicPacingProfile.Restore(pacingSeconds, times, distances));
            }
            // Older fixtures never recorded either contract. Keep their
            // original subject/terrain interpretation; source Journey metadata
            // alone is not evidence to opt them into the angular-view check.
            if (schema >= 4)
            {
                plan = plan.WithStableWaterClearance(Flag(r)).WithJourneyView(Flag(r));
                double cruise = r.ReadDouble();
                if (cruise != 0) plan = plan.WithJourneyCruiseSpeed(cruise);
            }
            if (stream.Position != stream.Length) throw new InvalidDataException("Camera fixture contains unexpected trailing data.");
            return new CameraFixture(world, name, version, settings, created, motion, dynamic, plan, journeyId, predecessor);
        }
        catch (Exception error) when (error is ArgumentException || error is EndOfStreamException || error is DecoderFallbackException || error is OverflowException)
        { throw new InvalidDataException("Camera fixture contains malformed metadata or unsafe camera geometry.", error); }
    }

    private static void Feature(BinaryWriter w, ScenicFeature f)
    {
        Text(w, f.Id, 160); w.Write((byte)f.Kind); Point(w, f.Center); Point(w, f.Direction);
        w.Write(f.Radius); w.Write(f.Height); w.Write(f.Confidence);
        OptionalText(w, f.ParentId, 160); OptionalText(w, f.ScopeLabel, 64); w.Write(f.IsPartial);
        w.Write((byte)f.SubjectKind); OptionalText(w, f.SubjectIdentity, 64);
        w.Write(f.MeasuredBounds.HasValue); if (f.MeasuredBounds.HasValue) Bounds(w, f.MeasuredBounds.Value);
        w.Write((byte)f.MemberPoints.Count); foreach (AutonomyPosition p in f.MemberPoints) Point(w, p);
        w.Write(f.WaterDirection.HasValue); if (f.WaterDirection.HasValue) Point(w, f.WaterDirection.Value);
        w.Write((byte)f.MemberBounds.Count); foreach (PlaceBounds b in f.MemberBounds) Bounds(w, b);
    }
    private static ScenicFeature Feature(BinaryReader r)
    {
        string id = Text(r, 160); ScenicFeatureKind kind = (ScenicFeatureKind)r.ReadByte();
        AutonomyPosition center = Point(r), direction = Point(r);
        float radius = r.ReadSingle(), height = r.ReadSingle(), confidence = r.ReadSingle();
        string? parent = OptionalText(r, 160), scope = OptionalText(r, 64); bool partial = Flag(r);
        ScenicSubjectKind subject = (ScenicSubjectKind)r.ReadByte(); string? identity = OptionalText(r, 64);
        PlaceBounds? bounds = Flag(r) ? Bounds(r) : (PlaceBounds?)null;
        int count = Count(r, 8); var points = new AutonomyPosition[count];
        for (int i = 0; i < count; i++) points[i] = Point(r);
        AutonomyPosition? water = Flag(r) ? Point(r) : (AutonomyPosition?)null;
        count = Count(r, 8); var members = new PlaceBounds[count];
        for (int i = 0; i < count; i++) members[i] = Bounds(r);
        return new ScenicFeature(id, kind, center, direction, radius, height, confidence, parent, scope, partial,
            subject, identity, bounds, points, water, members);
    }
    private static int Count(BinaryReader r, int maximum)
    { int value = r.ReadByte(); if (value > maximum) throw new InvalidDataException("Camera fixture collection exceeds its limit."); return value; }
    private static void Point(BinaryWriter w, AutonomyPosition p) { w.Write(p.X); w.Write(p.Y); w.Write(p.Z); }
    private static AutonomyPosition Point(BinaryReader r) => new AutonomyPosition(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
    private static void Bounds(BinaryWriter w, PlaceBounds b) { Point(w, b.Minimum); Point(w, b.Maximum); }
    private static PlaceBounds Bounds(BinaryReader r) => new PlaceBounds(Point(r), Point(r));
    private static bool Flag(BinaryReader r)
    { byte value = r.ReadByte(); if (value > 1) throw new InvalidDataException("Invalid fixture flag."); return value == 1; }
    private static void OptionalText(BinaryWriter w, string? value, int max)
    { w.Write(value != null); if (value != null) Text(w, value, max); }
    private static string? OptionalText(BinaryReader r, int max) => Flag(r) ? Text(r, max) : null;
    private static void Text(BinaryWriter w, string text, int maximum)
    {
        if (text == null || text.Length > maximum) throw new ArgumentOutOfRangeException(nameof(text));
        byte[] bytes = Utf8.GetBytes(text);
        if (bytes.Length > maximum * 4) throw new ArgumentOutOfRangeException(nameof(text));
        w.Write((ushort)bytes.Length); w.Write(bytes);
    }
    private static string Text(BinaryReader r, int maximum)
    {
        int count = r.ReadUInt16();
        if (count > maximum * 4 || count > r.BaseStream.Length - r.BaseStream.Position)
            throw new InvalidDataException("Camera fixture text exceeds its limit.");
        string value = Utf8.GetString(r.ReadBytes(count));
        if (value.Length > maximum) throw new InvalidDataException("Camera fixture text exceeds its limit.");
        return value;
    }
}
