using System.Collections.Immutable;

namespace KeyLoad.Clustering;

internal sealed record NativeRequestWorkOriginalSettlement(Exception? OriginalFailure, ImmutableArray<Exception> CleanupFailures)
{
    internal Exception? CleanupOrJoinFailure
    {
        get
        {
            Exception? failure = null;
            foreach (var cleanup in CleanupFailures)
            {
                failure = NativeRequestWorkSettlement.Preserve(failure, cleanup);
            }
            return failure;
        }
    }
}
