using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.Clustering;

/// <summary>Owns one original durable-job invocation and its existing execution child settlement.</summary>
internal static class AnnProjectionJobOperation
{
    internal static async Task<T> RunAsync<T>(IServiceProvider services,
        Func<CapabilityWorkLifetime, Task<T>> execute, CancellationToken token)
    {
        T result = default!;
        await RunAsync(services, async binding =>
        {
            result = await execute(binding);
        }, token);
        return result;
    }

    internal static async Task RunAsync(IServiceProvider services,
        Func<CapabilityWorkLifetime, Task> execute, CancellationToken token)
    {
        var owner = services.GetRequiredService<Func<RequestWorkOwner>>()();
        await using var lifetime = (CapabilityWorkLifetime.Acquire(owner, Guid.NewGuid(),
            RequestWorkKind.RequestProducer, token)).Value
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, AnnProjectionJobProtocol.Invalid);
        try
        {
            await execute(lifetime);
        }
        catch (Exception failure) when (CqrsBoundaryErrors.IsNonFatal(failure))
        {
            lifetime.PrimaryError = failure;
            throw;
        }
        catch (Exception failure) when (!CqrsBoundaryErrors.IsNonFatal(failure))
        {
            lifetime.PrimaryError = failure;
            throw;
        }
    }
}
