using System.Runtime.ExceptionServices;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class ScopedRangeStoreFixture : IDisposable
{
    private const string TemporaryDirectoryPrefix = "keyload-scoped-range-";
    private const string GuidFormat = "N";
    private const string CloseNotJoined = "The original scoped range owner close is unknown; retain its root.";
    private readonly string directory = Path.Combine(Path.GetTempPath(),
        TemporaryDirectoryPrefix + Guid.NewGuid().ToString(GuidFormat));
    private bool closeAttempted;
    private bool closeReturned;
    private ExceptionDispatchInfo? originalCloseFailure;

    public ZoneTreeStore Store
    {
        get;
    }

    public ScopedRangeStoreFixture() => Store = new(new(directory), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());

    public void Dispose()
    {
        if (closeAttempted)
        {
            originalCloseFailure?.Throw();
            if (!closeReturned)
            {
                throw new InvalidOperationException(CloseNotJoined);
            }
        }
        else
        {
            CloseOriginalStore();
        }
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }

    private void CloseOriginalStore()
    {
        closeAttempted = true;
        try
        {
            Store.Dispose();
            closeReturned = true;
        }
        catch (Exception failure) when (KeyLoad.Clustering.NativeCqrsBoundaryErrors.IsNonFatal(failure))
        {
            originalCloseFailure = ExceptionDispatchInfo.Capture(failure);
            throw;
        }
        catch (Exception failure) when (!KeyLoad.Clustering.NativeCqrsBoundaryErrors.IsNonFatal(failure))
        {
            originalCloseFailure = ExceptionDispatchInfo.Capture(failure);
            throw;
        }
    }
}
