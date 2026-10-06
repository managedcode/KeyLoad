using ManagedCode.Communication.CQRS;

namespace KeyLoad.Comparisons;

internal static class OpenLoopExceptionBoundary
{
    internal static bool IsNonFatal(Exception error) => CqrsRuntimeFailures.FindFatal(error) is null;
}
