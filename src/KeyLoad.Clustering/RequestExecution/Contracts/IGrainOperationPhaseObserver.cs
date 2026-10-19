using ManagedCode.Communication.Commands;
namespace KeyLoad.Clustering;

/// <summary>Optional private observer of an independently owned, already-issued verified execution operation.</summary>
/// <remarks>Does not issue or modify authority and never receives a borrowed grain context.</remarks>
internal interface IGrainOperationPhaseObserver
{
    void ObserveOperation(GrainRequestProbeIdentity identity, Command<DatabaseCommand> original);
}
