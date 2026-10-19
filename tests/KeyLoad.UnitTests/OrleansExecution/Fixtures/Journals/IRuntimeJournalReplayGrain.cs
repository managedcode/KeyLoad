namespace KeyLoad.UnitTests.Features.OrleansExecution;

/// <summary>Exercises binary durable-state writes, activation replay, and deletion.</summary>
[Alias(RuntimeJournalReplayProtocol.InterfaceAlias)]
internal interface IRuntimeJournalReplayGrain : IGrainWithStringKey
{
    /// <summary>Writes a payload to the execution journal.</summary>
    /// <param name="value">The bytes to persist.</param>
    /// <returns>A task which completes after the write is acknowledged.</returns>
    Task SetAsync(byte[] value);

    /// <summary>Reads the payload recovered for the current activation.</summary>
    /// <returns>The persisted bytes, or null if no state exists.</returns>
    Task<byte[]?> ReadAsync();

    /// <summary>Returns the unique token assigned to this activation.</summary>
    /// <returns>The activation token.</returns>
    Task<string> GetActivationTokenAsync();

    /// <summary>Requests deactivation after the current call completes.</summary>
    /// <returns>A task which completes after the deactivation request is accepted.</returns>
    Task DeactivateAsync();

    /// <summary>Deletes the durable state for this grain.</summary>
    /// <returns>A task which completes after deletion is acknowledged.</returns>
    Task DeleteAsync();
}
