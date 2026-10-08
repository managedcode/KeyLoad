namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private readonly Lock movementAbsenceGate = new();
    private MovementAuthorityCut? movementAbsenceCut;
    private readonly record struct MovementAuthorityCut(Guid NodeId, Guid Incarnation,
        long ReadGeneration, long Position);

    private bool HasMovementAbsence(MovementAuthorityCut cut)
    {
        lock (movementAbsenceGate)
        { return movementAbsenceCut == cut; }
    }

    private void PublishMovementAbsence(MovementAuthorityCut cut)
    {
        lock (movementAbsenceGate)
        { movementAbsenceCut = cut; }
    }
}
