using KeyLoad.Clustering;

namespace KeyLoad.Server.Features.ResourceExecution;

internal static class DropTableRegistration
{
    internal static void Register(IServiceCollection services)
        => services.AddScoped<IDropTableExecution>(provider =>
            ActivatorUtilities.CreateInstance<DropTableExecution>(provider,
                provider.GetRequiredService<PartitionStorageScope>().Host));
}
