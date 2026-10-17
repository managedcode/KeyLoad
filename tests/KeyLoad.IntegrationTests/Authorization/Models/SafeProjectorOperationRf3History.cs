namespace KeyLoad.IntegrationTests.Features.Authorization;

internal sealed class SafeProjectorOperationRf3History(SafeProjectorOperationRf3State state)
{
    internal SafeProjectorOperationRf3State State { get; } = state;
    internal CommandRequest? PublicPatch
    {
        get; set;
    }
    internal CommitReceipt? PublicReceipt
    {
        get; set;
    }
    internal CommandRequest? HealthyPatch
    {
        get; set;
    }
    internal CommitReceipt? HealthyReceipt
    {
        get; set;
    }
    internal List<(CommandRequest Request, string Detail, long Epoch)> Refusals { get; } = [];
    internal List<(Guid Id, PrincipalRecord Principal)> PrincipalCommands { get; } = [(state.PrincipalId, state.InitialPrincipal)];
    internal Dictionary<long, DateTimeOffset> CommitTimes { get; } = [];
    internal long Revision => HealthyPatch is not null ? SafeProjectorOperationRf3Protocol.HealthyRevision
        : PublicPatch is not null ? SafeProjectorOperationRf3Protocol.UpdatedRevision : SafeProjectorOperationRf3Protocol.InitialRevision;
    internal string RawJson => HealthyPatch is not null ? SafeProjectorOperationRf3Protocol.HealthyJson
        : PublicPatch is not null ? SafeProjectorOperationRf3Protocol.UpdatedJson : SafeProjectorOperationRf3Protocol.InitialJson;
    internal string SafeJson => HealthyPatch is not null ? SafeProjectorOperationRf3Protocol.HealthySafe
        : PublicPatch is not null ? SafeProjectorOperationRf3Protocol.UpdatedSafe : SafeProjectorOperationRf3Protocol.InitialSafe;
    internal string Status => HealthyPatch is not null ? SafeProjectorOperationRf3Protocol.HealthyStatus
        : PublicPatch is not null ? SafeProjectorOperationRf3Protocol.UpdatedStatus : SafeProjectorOperationRf3Protocol.InitialStatus;
}
