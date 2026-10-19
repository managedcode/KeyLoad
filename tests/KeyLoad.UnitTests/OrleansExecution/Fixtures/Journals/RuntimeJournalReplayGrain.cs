#pragma warning disable ORLEANSEXP005
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;

namespace KeyLoad.UnitTests.Features.OrleansExecution;

/// <summary>Runtime activation used by the execution journal replay integration test.</summary>
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

#pragma warning restore ORLEANSEXP005
