using KeyLoad.Clustering;

namespace KeyLoad.Server.ResourceExecution;

internal static class DropTableRegistration
{
    internal static void Register(IServiceCollection services)
        => services.AddScoped<IDropTableExecution>(provider =>
            (ActivatorUtilities.CreateInstance<DropTableExecution>(provider,
                provider.GetRequiredService<PartitionStorageScope>().Host) /* KLD0041: Native ActivatorUtilities receives the actual PartitionHost reference in params object[] while resolving the remaining DropTableExecution dependencies from the owning scope. */));
}
