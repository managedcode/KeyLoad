using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

/// <summary>Owns the original contender cleanup proof before healthy storage can be released.</summary>
internal sealed class FunctionalGateFileContenderSettlement(bool enabled)
{
    private const string Unjoined = "The original competing owner processes have not joined; retain the healthy application and files.";
    private const string AlreadyOwned = "The competing owner settlement task already has an owner.";
    private Task<bool>? settlement;
    internal bool Enabled { get; } = enabled;

    internal void Register(Task<bool> original)
    {
        ArgumentNullException.ThrowIfNull(original);
        if (!Enabled || Interlocked.CompareExchange(ref settlement, original, null) is not null)
        {
            throw new InvalidOperationException(AlreadyOwned);
        }
    }

    internal async Task<bool> JoinAsync(List<Exception> failures, CancellationToken token)
    {
        if (Volatile.Read(ref settlement) is not { } original)
        {
            return true;
        }
        var released = false;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            released = await original.WaitAsync(token).ConfigureAwait(false);
            if (!released)
            {
                throw new InvalidOperationException(Unjoined);
            }
        }, failures).ConfigureAwait(false);
        return released;
    }
}
