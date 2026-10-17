using KeyLoad.Clustering;

namespace KeyLoad.Server.Features.ResourceExecution;

internal static class NativeDropTableRegistration
{
    internal static void Register(IServiceCollection services)
        => services.AddSingleton<INativeDropTableExecution, NativeDropTableExecution>();
}
