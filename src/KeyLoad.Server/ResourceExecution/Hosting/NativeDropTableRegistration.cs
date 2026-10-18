using KeyLoad.Clustering;

namespace KeyLoad.Server.Features.ResourceExecution;

internal static class NativeDropTableRegistration
{
    internal static void Register(IServiceCollection services)
        => services.AddScoped<INativeDropTableExecution>(provider =>
            ActivatorUtilities.CreateInstance<NativeDropTableExecution>(provider,
                provider.GetRequiredService<PartitionStorageScope>().Host));
}
