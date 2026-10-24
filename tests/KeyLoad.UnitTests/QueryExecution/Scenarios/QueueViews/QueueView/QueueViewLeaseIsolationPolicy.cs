namespace KeyLoad.UnitTests.QueryExecution;

internal static class QueueViewLeaseIsolationPolicy
{
    internal static async Task SetAsync(QueueViewLeaseIsolationState state, bool inspect, long epoch)
    {
        var expected = state.Reader with
        {
            PolicyEpoch = epoch,
            Grants = [new(state.Partition.DatabaseId, QueueViewLeaseIsolationProtocol.Events, Capability.Query | Capability.EventsRead),
                new(state.Partition.DatabaseId, QueueViewLeaseIsolationProtocol.Queue,
                    Capability.Query | (inspect ? Capability.QueueInspect : Capability.None))]
        };
        await QueueViewLeaseIsolationOperations.SendAsync(state, OperationKind.ConfigurePrincipal,
            new ConfigurePrincipalRequest(expected), Guid.NewGuid(), expected);
        state.Reader = expected;
    }
}
