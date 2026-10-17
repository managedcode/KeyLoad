using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal sealed class ReplicaProtocolFileFailureGate(string missing)
{
    internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Exited { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal FileNotFoundException? Failure
    {
        get; private set;
    }
    internal void Release() => released.TrySetResult();
    internal async Task<OperationResult> ForwardAsync(DatabaseEngine database, ReplicatedOperation operation, CancellationToken token)
    {
        using var registration = token.Register(ReadMissing);
        Entered.TrySetResult();
        try
        {
            await released.Task.ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            return database.Apply(operation);
        }
        finally
        {
            Exited.TrySetResult();
        }
    }
    private void ReadMissing()
    {
        try
        {
            using var file = File.OpenRead(missing);
        }
        catch (FileNotFoundException original)
        {
            Failure = original;
            throw;
        }
        finally
        {
            Cancelled.TrySetResult();
        }
    }
}
