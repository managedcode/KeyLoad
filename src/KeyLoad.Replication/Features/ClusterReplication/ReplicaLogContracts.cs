using System.Collections.Immutable;

namespace KeyLoad.Replication;

/// <summary>Invokes a bounded authenticated operation on one configured native replica service.</summary>
public interface IReplicaTransport
{
    /// <summary>Returns the peer's exact typed JSON outcome after native envelope verification.</summary>
    /// <param name="voterId">Configured destination voter identity.</param>
    /// <param name="method">Authenticated replica operation.</param>
    /// <param name="payloadJson">Exact bounded replica JSON payload.</param>
    /// <param name="cancellationToken">Caller deadline and cancellation.</param>
    /// <returns>The verified peer outcome JSON.</returns>
    Task<string> InvokeAsync(string voterId, ReplicaRpc method, string payloadJson, CancellationToken cancellationToken);
}

/// <summary>Persists node-owned election, ordered entries, commit positions and published checkpoints.</summary>
public interface IDurableReplicaLog : IDisposable
{
    /// <summary>Gets the borrowed node-owned gate shared by protocol planning and checkpoint publication; only the drained log owner disposes it.</summary>
    SemaphoreSlim ProtocolGate { get; }
    /// <summary>Gets the current verified durable metadata.</summary>
    ReplicaHardState State { get; }
    /// <summary>Reads one retained entry without exposing the private storage buffer.</summary>
    /// <param name="index">Requested replica position.</param>
    /// <returns>The owned decoded entry, or null outside the retained suffix.</returns>
    ReplicaEntry? ReadEntry(long index);
    /// <summary>Returns the retained entry or checkpoint term at a position.</summary>
    /// <param name="index">Requested replica position, including initial position zero.</param>
    /// <returns>The term established at the requested position.</returns>
    long TermAt(long index);
    /// <summary>Reads an immutable suffix bounded by both entry count and encoded bytes.</summary>
    /// <param name="firstIndex">First retained position to include.</param>
    /// <param name="maxEntries">Maximum requested entry count.</param>
    /// <param name="maxBytes">Maximum encoded batch size.</param>
    /// <returns>The final owned ordered batch.</returns>
    ImmutableArray<ReplicaEntry> Read(long firstIndex, int maxEntries, int maxBytes);
    /// <summary>Durably advances the term and fixes at most one vote within a term.</summary>
    /// <param name="term">Monotonic election term.</param>
    /// <param name="votedFor">Configured selected voter, or null before voting.</param>
    void SaveTermAndVote(long term, string? votedFor);
    /// <summary>Validates and durably acknowledges one ordered suffix.</summary>
    /// <param name="entries">Read-only ordered input consumed synchronously.</param>
    void Append(IReadOnlyList<ReplicaEntry> entries);
    /// <summary>Durably advances the committed prefix within the current log.</summary>
    /// <param name="index">Monotonic committed position.</param>
    void Commit(long index);
    /// <summary>Acquires the protocol gate before publishing a verified checkpoint and retaining its matching tail; callers must not already hold that gate.</summary>
    /// <param name="snapshot">Verified canonical image and its exact committed cut.</param>
    void PublishSnapshot(ReplicaSnapshot snapshot);
}
