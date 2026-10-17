using KeyLoad.Clustering;

namespace KeyLoad.Server.Features.DocumentStorage;

internal static class RemoteDocumentRuntimeLifecycle
{
    private const string AttachedReceiverFailure = "The original native model receiver is already attached.";

    internal static void Attach(RemoteDocumentRuntime runtime, ref RemoteModelNativeRuntime? nativeReceiver, RemoteModelNativeRuntime receiver)
    {
        ArgumentNullException.ThrowIfNull(receiver);
        if (Interlocked.CompareExchange(ref nativeReceiver, receiver, null) is not null)
        {
            throw new InvalidOperationException(AttachedReceiverFailure);
        }
        receiver.Attach(runtime);
    }

    internal static async Task SettleClientAsync(Task terminal, List<Exception> failures)
    {
        await terminal.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        if (terminal.Exception is { } terminalFailure)
        {
            failures.AddRange(terminalFailure.InnerExceptions);
        }
        else if (terminal.IsCanceled)
        {
            failures.Add(new TaskCanceledException(terminal));
        }
    }

    internal static async Task SettleCreationAsync(RemoteDocumentRuntime runtime, Exception primary)
    {
        var failures = new List<Exception> { primary };
        try
        {
            await runtime.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
