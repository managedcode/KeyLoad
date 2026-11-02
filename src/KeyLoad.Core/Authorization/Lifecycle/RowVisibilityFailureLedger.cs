using System.Runtime.ExceptionServices;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.Core.Authorization;

internal static class RowVisibilityFailureLedger
{
    internal static void Capture(Action close, List<Exception> failures)
    {
        try
        {
            close();
        }
        catch (Exception error) when (CqrsRuntimeFailures.FindFatal(error) is null) { failures.Add(error); }
        catch (Exception error) when (CqrsRuntimeFailures.FindFatal(error) is not null) { failures.Add(error); }
    }

    internal static void ThrowJoined(Exception? primary, Exception terminal)
    {
        if (primary is not null && !(ReferenceEquals(primary, terminal) /* KLD0041: This comparison requires CLR reference identity rather than an overridden value-equality implementation. */))
        {
            Throw([primary, terminal]);
        }
        Throw([terminal]);
    }

    internal static void Throw(List<Exception> failures)
    {
        if (failures.Count == RowVisibilityMemoryModel.MetadataOnly)
        {
            return;
        }
        if (failures.Count == RowVisibilityMemoryModel.SingleEntry)
        {
            ExceptionDispatchInfo.Capture(failures[RowVisibilityMemoryModel.MetadataOnly]).Throw();
        }
        throw new AggregateException(failures);
    }
}
