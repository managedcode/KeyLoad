using System.Collections.Immutable;

namespace KeyLoad.Clustering;

internal sealed record RequestWorkOriginalSettlement(Exception? OriginalFailure, ImmutableArray<Exception> CleanupFailures)
{
    internal Exception? CleanupOrJoinFailure
    {
        get
        {
            Exception? failure = null;
            foreach (var cleanup in CleanupFailures)
            {
                failure = RequestWorkSettlement.Preserve(failure, cleanup);
            }
            return failure;
        }
    }
}
