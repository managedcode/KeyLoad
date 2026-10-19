using KeyLoad.Clustering;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Services;

namespace KeyLoad.Server;

internal static class PartitionStorageRegistration
{
    private const string StorageFirst = "The physical storage grain service must be registered before its native borrowers.";

    internal static void Register(ISiloBuilder silo)
    {
        var services = silo.Services;
        if (services.Any(service => !service.IsKeyedService && service.ServiceType == typeof(IGrainService)))
        {
            throw new InvalidOperationException(StorageFirst);
        }
        services.TryAddSingleton<PartitionHostApplicationOwner>();
        services.TryAddSingleton<ILocalDatabaseRuntime>(provider => provider.GetRequiredService<PartitionHostApplicationOwner>());
        services.TryAddSingleton<Func<IPartitionStoragePublication>>(provider =>
        {
            var borrowed = provider.GetRequiredService<PartitionHostApplicationOwner>();
            return () => borrowed;
        });
        services.TryAddSingleton<PartitionStorageLifecycle>();
        services.AddSingleton<ILifecycleParticipant<ISiloLifecycle>>(provider =>
            provider.GetRequiredService<PartitionStorageLifecycle>());
        silo.AddGrainService<PartitionStorageGrainService>();
    }
}
