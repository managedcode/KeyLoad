using KeyLoad.Core;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.Clustering;

/// <summary>Owns one original durable-job invocation and its existing native child settlement.</summary>
internal static class AnnProjectionJobOperation
{
    internal static async Task<T> RunAsync<T>(IServiceProvider services, DatabaseEngine database,
        Func<NativeCapabilityWorkLifetime, Task<T>> execute, CancellationToken token)
    {
        T result = default!;
        await RunAsync(services, database, async binding =>
        {
            result = await execute(binding).ConfigureAwait(true);
        }, token).ConfigureAwait(true);
        return result;
    }

    internal static async Task RunAsync(IServiceProvider services, DatabaseEngine database,
        Func<NativeCapabilityWorkLifetime, Task> execute, CancellationToken token)
    {
        var owner = services.GetRequiredService<Func<NativeRequestWorkOwner>>()();
        await using var lifetime = NativeCapabilityWorkLifetime.Acquire(owner, Guid.NewGuid(),
            NativeRequestWorkKind.RequestProducer, database.OperationLimitsOptions, token)
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, AnnProjectionJobProtocol.Invalid);
        try
        {
            await execute(lifetime).ConfigureAwait(true);
        }
        catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure))
        {
            lifetime.PrimaryError = failure;
            throw;
        }
        catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure))
        {
            lifetime.PrimaryError = failure;
            throw;
        }
    }
}
