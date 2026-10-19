namespace KeyLoad.Storage.ZoneTree;

/// <summary>Ephemeral execution write-gate contention for one open node-local store.</summary>
/// <param name="SessionId">Existing nonpersisted identity of this store opening.</param>
/// <param name="WaitingWriters">Execution pending write-lock users, including commits and maintenance.</param>
public readonly record struct ZoneTreeGateSnapshot(Guid SessionId, int WaitingWriters);
