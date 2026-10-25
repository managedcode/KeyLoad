namespace KeyLoad.ClusterTests.Authorization;

internal sealed class SafeProjectorOperationClusterHistory(SafeProjectorOperationClusterState state)
{
    internal SafeProjectorOperationClusterState State { get; } = state;
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
    internal long Revision => HealthyPatch is not null ? SafeProjectorOperationClusterProtocol.HealthyRevision
        : PublicPatch is not null ? SafeProjectorOperationClusterProtocol.UpdatedRevision : SafeProjectorOperationClusterProtocol.InitialRevision;
    internal string RawJson => HealthyPatch is not null ? SafeProjectorOperationClusterProtocol.HealthyJson
        : PublicPatch is not null ? SafeProjectorOperationClusterProtocol.UpdatedJson : SafeProjectorOperationClusterProtocol.InitialJson;
    internal string SafeJson => HealthyPatch is not null ? SafeProjectorOperationClusterProtocol.HealthySafe
        : PublicPatch is not null ? SafeProjectorOperationClusterProtocol.UpdatedSafe : SafeProjectorOperationClusterProtocol.InitialSafe;
    internal string Status => HealthyPatch is not null ? SafeProjectorOperationClusterProtocol.HealthyStatus
        : PublicPatch is not null ? SafeProjectorOperationClusterProtocol.UpdatedStatus : SafeProjectorOperationClusterProtocol.InitialStatus;
}
