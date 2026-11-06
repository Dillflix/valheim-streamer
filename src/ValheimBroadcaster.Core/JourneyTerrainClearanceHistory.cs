namespace ValheimBroadcaster.Core;

/// <summary>Only a complete sweep whose endpoint actually rendered establishes
/// an outdoor position above ground. If a rebuilt tile rises into that position,
/// retain the evidence while climbing in the same vertical column.</summary>
public sealed class JourneyTerrainClearanceHistory
{
    private int sweptFrame = -1, shownFrame = -1;
    private AutonomyPosition sweptEnd;
    private AutonomyPosition? clearColumn;

    public void RecordSweptEnd(int frame, AutonomyPosition end)
    { sweptFrame = frame; sweptEnd = end; }

    public void Presented(int frame, AutonomyPosition actual)
    {
        if (frame < 0 || frame < shownFrame) return;
        if (sweptFrame == frame && actual.X == sweptEnd.X && actual.Y == sweptEnd.Y && actual.Z == sweptEnd.Z) clearColumn = actual;
        else if (frame - (long)shownFrame > 2 || !InClearColumn(actual)) clearColumn = null;
        shownFrame = frame;
    }

    public bool CanRecoverAt(int frame, AutonomyPosition from) =>
        shownFrame >= 0 && frame >= shownFrame && frame - (long)shownFrame <= 2 && InClearColumn(from);

    private bool InClearColumn(AutonomyPosition position) => clearColumn.HasValue &&
        clearColumn.Value.X == position.X && clearColumn.Value.Z == position.Z && clearColumn.Value.Y <= position.Y;

    public void Reset() { sweptFrame = shownFrame = -1; clearColumn = null; }
}
