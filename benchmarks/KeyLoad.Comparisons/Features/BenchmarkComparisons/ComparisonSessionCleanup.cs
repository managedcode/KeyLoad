namespace KeyLoad.Comparisons;

internal static class ComparisonSessionCleanup
{
    internal const string Failure = "ComparisonSessionCleanupFailed";

    internal static async Task<bool> CloseAsync(IEnumerable<IComparisonSession> sessions, int timeoutSeconds)
    {
        try
        {
            await Task.WhenAll(sessions.Select(session => CloseOneAsync(session, timeoutSeconds)));
            return true;
        }
        catch (ComparisonFailureException)
        {
            return false;
        }
    }

    private static async Task CloseOneAsync(IComparisonSession session, int timeoutSeconds)
    {
        try
        {
            await session.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(timeoutSeconds));
        }
        catch (Exception)
        {
            throw new ComparisonFailureException(Failure);
        }
    }
}
