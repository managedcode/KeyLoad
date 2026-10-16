namespace KeyLoad.Replication;

/// <summary>Preserves operation-level coordinator deadlines and bounded constituent replica RPC deadlines.</summary>
internal static class ReplicaRpcDeadline
{
    internal static TimeSpan SelectIncoming(ReplicaRpc method, ReplicaExecutionOptions execution)
        => method switch
        {
            ReplicaRpc.ReadBarrier or ReplicaRpc.ControlReadBarrier => execution.ReadBarrierTimeout,
            _ => execution.CommandTimeout
        };

    internal static TimeSpan Select(ReplicaRpc method, ReplicaConfiguration configuration, ReplicaExecutionOptions execution)
        => method switch
        {
            ReplicaRpc.ReadBarrier or ReplicaRpc.ControlReadBarrier => execution.ReadBarrierTimeout,
            ReplicaRpc.Forward => execution.CommandTimeout,
            _ => configuration.RpcTimeout
        };
}
