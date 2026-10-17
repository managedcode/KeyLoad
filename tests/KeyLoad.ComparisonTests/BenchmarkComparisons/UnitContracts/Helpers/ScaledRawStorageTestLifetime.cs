using KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>Preserves a real test-body failure alongside an independent owned-close failure.</summary>
internal static class ScaledRawStorageTestLifetime
{
    internal static async Task RunAsync<T>(Func<T> createOwner, Func<T, Task> body)
        where T : IDisposable
    {
        var owner = createOwner();
        Exception? bodyFailure = null;
        var bodyCompleted = false;
        try
        {
            await body(owner);
            bodyCompleted = true;
        }
        catch (Exception failure) when (RawStorageFixtureFailures.IsNonFatal(failure))
        {
            bodyFailure = failure;
            throw;
        }
        finally
        {
            if (bodyCompleted || bodyFailure is not null)
            {
                Close(owner, bodyFailure);
            }
        }
    }

    private static void Close<T>(T owner, Exception? bodyFailure)
        where T : IDisposable
    {
        try
        {
            owner.Dispose();
        }
        catch (Exception cleanupFailure) when (bodyFailure is not null
            && RawStorageFixtureFailures.IsNonFatal(cleanupFailure))
        {
            throw new AggregateException(bodyFailure, cleanupFailure);
        }
    }
}
