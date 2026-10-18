using ManagedCode.Communication.CQRS;

namespace KeyLoad.Comparisons;

internal static class BenchmarkSessionFailure
{
    internal static bool IsNonfatal(Exception error) => CqrsRuntimeFailures.FindFatal(error) is null;
}
