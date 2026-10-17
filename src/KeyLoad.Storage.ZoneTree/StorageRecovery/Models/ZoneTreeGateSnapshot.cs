namespace KeyLoad.Storage.ZoneTree;

/// <summary>Ephemeral native write-gate contention for one open node-local store.</summary>
/// <param name="SessionId">Existing nonpersisted identity of this store opening.</param>
/// <param name="WaitingWriters">Native pending write-lock users, including commits and maintenance.</param>
public readonly record struct ZoneTreeGateSnapshot(Guid SessionId, int WaitingWriters);
