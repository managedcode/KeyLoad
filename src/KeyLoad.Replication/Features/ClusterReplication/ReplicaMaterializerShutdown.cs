using System.Threading.Channels;

namespace KeyLoad.Replication;

internal static class ReplicaMaterializerShutdown
{
    internal static async Task DisposeAsync(CancellationTokenSource lifetime, ChannelWriter<bool> work, Task worker,
        SemaphoreSlim applyGate, SemaphoreSlim protocolGate, Action publishTerminal)
    {
        var cancellation = lifetime.CancelAsync();
        work.TryComplete();
        try
        {
            await Task.WhenAll(cancellation, worker).ConfigureAwait(false);
        }
        finally
        {
            publishTerminal();
            await applyGate.WaitAsync().ConfigureAwait(false);
            applyGate.Dispose();
            protocolGate.Dispose();
            lifetime.Dispose();
        }
    }
}
