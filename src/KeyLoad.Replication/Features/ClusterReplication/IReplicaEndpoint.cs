namespace KeyLoad.Replication;

/// <summary>Node-owned protocol endpoint exposed by the Orleans per-silo replica service.</summary>
public interface IReplicaEndpoint
{
    /// <summary>Completes when the local early-lifecycle transport can receive replica requests.</summary>
    Task TransportReady { get; }

    /// <summary>Attaches the local Orleans transport and starts node-owned replica maintenance.</summary>
    /// <param name="transport">Authenticated, scope-bound transport owned by the Orleans host.</param>
    void AttachTransport(IReplicaTransport transport);

    /// <summary>Executes one authenticated and scope-checked internal replica request.</summary>
    /// <param name="method">Replica control or data operation to dispatch.</param>
    /// <param name="payloadJson">Encoded typed replica request.</param>
    /// <param name="cancellationToken">Caller cancellation while protocol work remains node-owned.</param>
    /// <returns>The encoded typed replica response.</returns>
    Task<string> HandleAsync(ReplicaRpc method, string payloadJson, CancellationToken cancellationToken);

    /// <summary>Drains replica maintenance after Orleans membership shutdown.</summary>
    /// <param name="cancellationToken">Cancellation of the caller's wait for the shared shutdown task.</param>
    /// <returns>The shared drain completion, observed with caller cancellation.</returns>
    Task StopAsync(CancellationToken cancellationToken);
}
