using KeyLoad.Orleans;
namespace KeyLoad.UnitTests.Features.Messaging;

internal static class NativeSagaTimeoutCleanup
{
    internal static async Task ObserveAsync(Func<Task> operation, ICollection<Exception> failures)
    {
        try
        {
            await operation();
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
    }

    internal static void ThrowFailures(IReadOnlyCollection<Exception> failures)
    {
        if (failures.Count == 1)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures.Single()).Throw();
        }

        if (failures.Count > 1)
        {
            throw new AggregateException(failures);
        }
    }
}
