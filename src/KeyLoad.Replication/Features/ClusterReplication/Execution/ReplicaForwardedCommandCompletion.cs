using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.Replication;

/// <summary>Cancels and joins the exact queue completion before its incoming receiver-owned work can be released.</summary>
internal static class ReplicaForwardedCommandCompletion
{
    internal static async Task<OperationResult> AwaitAsync(AdmittedCommand pending,
        IReplicaForwardedCommandWork work, CancellationToken cancellationToken)
    {
        try
        {
            return await pending.Completion.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            var failures = new List<Exception>
            {
                Errors.Fail(ErrorCode.UnknownWriteOutcome, ReplicaProtocol.InterruptedWrite)
            };
            await CancelAsync(work, failures).ConfigureAwait(false);
            // The original pending task owns the work. Never join it with the canceled caller token.
            await ReplicaShutdownStage.ObserveAsync(pending.Completion, failures).ConfigureAwait(false);
            var fatal = CqrsRuntimeFailures.FindFatal(new AggregateException(failures));
            if (fatal is not null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(fatal).Throw();
            }
            ReplicaShutdownStage.ThrowIfAny(failures);
            throw;
        }
    }
    private static async Task CancelAsync(IReplicaForwardedCommandWork work, List<Exception> failures)
    {
        try
        {
            await work.CancelAsync().ConfigureAwait(false);
        }
        catch (Exception error) when (CqrsRuntimeFailures.FindFatal(error) is null)
        {
            failures.Add(error);
        }
        catch (Exception error) when (CqrsRuntimeFailures.FindFatal(error) is not null)
        {
            failures.Add(error);
        }
    }

    internal static Exception Preserve(Exception? original, Exception cleanup)
        => CqrsRuntimeFailures.FindFatal(original) ?? CqrsRuntimeFailures.FindFatal(cleanup)
            ?? (original is null || ReferenceEquals(original, cleanup)
                ? cleanup : new AggregateException(original, cleanup));

}
