using KeyLoad.Core;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.Clustering;

/// <summary>Owns one original durable-job invocation and its existing native child settlement.</summary>
internal static class AnnProjectionJobOperation
{
    internal static async Task<T> RunAsync<T>(IServiceProvider services, DatabaseEngine database,
        CancellationToken token, Func<NativeCapabilityWorkLifetime, Task<T>> execute)
    {
        T result = default!;
        await RunAsync(services, database, token, async binding =>
        {
            result = await execute(binding).ConfigureAwait(true);
        }).ConfigureAwait(true);
        return result;
    }

    internal static async Task RunAsync(IServiceProvider services, DatabaseEngine database,
        CancellationToken token, Func<NativeCapabilityWorkLifetime, Task> execute)
    {
        var owner = services.GetRequiredService<Func<NativeRequestWorkOwner>>()();
        var lifetime = NativeCapabilityWorkLifetime.Acquire(owner, Guid.NewGuid(),
            NativeRequestWorkKind.RequestProducer, token, database.OperationLimitsOptions)
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, AnnProjectionJobProtocol.Invalid);
        Exception? original = null;
        try
        {
            await execute(lifetime).ConfigureAwait(true);
        }
        catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure))
        {
            original = failure;
            throw;
        }
        catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure))
        {
            original = failure;
            throw;
        }
        finally
        {
            await NativeCapabilityWorkLifetime.SettleAsync(original, lifetime, null, null).ConfigureAwait(true);
        }
    }
}
