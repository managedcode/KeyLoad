using System.Collections.Immutable;
using System.Runtime.ExceptionServices;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.Client;

internal static class QueueWorkerFailureLedger
{
    private const int SingleFailureCount = 1;
    private const int FirstFailureIndex = 0;
    internal const string OperationEvidenceKey = "KeyLoad.QueueWorkerOperations";

    internal static bool IsNonFatal(Exception error) => CqrsRuntimeFailures.FindFatal(error) is null;

    internal static void ThrowIfFatal(IReadOnlyList<Exception> failures,
        IReadOnlyList<QueueWorkerOperationObservation> operations)
    {
        if (failures.All(IsNonFatal))
        {
            return;
        }
        var evidence = operations.Select(row => new QueueWorkerOperationIdentity(
            row.Receive?.RequestId ?? row.Command!.CommandId, row.Command?.Action, row.Stage)).ToImmutableArray();
        var original = failures.Count == SingleFailureCount ? failures[FirstFailureIndex]
            : new AggregateException(failures);
        try
        {
            ((original.Data /* KLD0041: Preserve the original fatal exception and attach its exact immutable operation-evidence value through the standard CLR Exception.Data contract; this occurs only on fatal settlement. */)[OperationEvidenceKey] /* KLD0041: Preserve the original fatal exception and attach its exact immutable operation-evidence value through the standard CLR Exception.Data contract; this occurs only on fatal settlement. */) = (evidence /* KLD0042: The original fatal exception exposes only object-valued CLR metadata; box this immutable evidence once on the fatal-error path without changing its published type. */);
        }
        catch (Exception attachment) when (IsNonFatal(attachment))
        {
            original = WithAttachmentFailure(original, attachment, evidence);
        }
        catch (Exception attachment) when (!IsNonFatal(attachment))
        {
            original = WithAttachmentFailure(original, attachment, evidence);
        }
        ExceptionDispatchInfo.Capture(original).Throw();
    }

    private static AggregateException WithAttachmentFailure(Exception original, Exception attachment,
        ImmutableArray<QueueWorkerOperationIdentity> evidence)
    {
        var aggregate = new AggregateException(original, attachment);
        ((aggregate.Data /* KLD0041: Preserve the original fatal exception and attach its exact immutable operation-evidence value through the standard CLR Exception.Data contract; this occurs only on fatal settlement. */)[OperationEvidenceKey] /* KLD0041: Preserve the original fatal exception and attach its exact immutable operation-evidence value through the standard CLR Exception.Data contract; this occurs only on fatal settlement. */) = (evidence /* KLD0042: The original fatal exception exposes only object-valued CLR metadata; box this immutable evidence once on the fatal-error path without changing its published type. */);
        return aggregate;
    }
}
