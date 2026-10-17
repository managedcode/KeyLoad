#pragma warning disable ORLEANSEXP005
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Exercises binary durable-state writes, activation replay, and deletion.</summary>
[Alias(RuntimeJournalReplayProtocol.InterfaceAlias)]
internal interface IRuntimeJournalReplayGrain : IGrainWithStringKey
{
    /// <summary>Writes a payload to the native journal.</summary>
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

/// <summary>Runtime activation used by the native journal replay integration test.</summary>
[GrainType(RuntimeJournalReplayProtocol.InterfaceAlias)]
internal sealed class RuntimeJournalReplayGrain : DurableGrain, IRuntimeJournalReplayGrain
{
    private const string StateName = "payload";
    private readonly IDurableValue<byte[]> value;
    private readonly string activationToken = Guid.NewGuid().ToString("N");

    /// <summary>Creates the activation and resolves its durable state value.</summary>
    public RuntimeJournalReplayGrain()
    {
        value = ServiceProvider.GetRequiredKeyedService<IDurableValue<byte[]>>(StateName);
    }

    /// <inheritdoc />
    public async Task SetAsync(byte[] next)
    {
        value.Value = next;
        await WriteStateAsync().ConfigureAwait(true);
    }

    /// <inheritdoc />
    public Task<byte[]?> ReadAsync() => Task.FromResult(value.Value);

    /// <inheritdoc />
    public Task<string> GetActivationTokenAsync() => Task.FromResult(activationToken);

    /// <inheritdoc />
    public Task DeactivateAsync()
    {
        DeactivateOnIdle();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task DeleteAsync()
        => await ((IJournaledStateManager)StateManager).DeleteStateAsync(CancellationToken.None).ConfigureAwait(true);
}

internal static class RuntimeJournalReplayProtocol
{
    internal const string InterfaceAlias = "keyload-tests-runtime-journal-replay-v1";
}
#pragma warning restore ORLEANSEXP005
