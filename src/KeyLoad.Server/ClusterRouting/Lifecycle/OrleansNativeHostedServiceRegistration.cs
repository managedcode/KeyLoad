namespace KeyLoad.Server.Features.ClusterRouting;

internal static class OrleansNativeHostedServiceRegistration
{
    private const string NativeSiloHostedService = "Orleans.Hosting.SiloHostedService";

    internal static void Guard(IServiceCollection services)
    {
        var original = services.Single(service => service.ServiceType == typeof(IHostedService)
            && service.ImplementationType?.FullName == NativeSiloHostedService);
        var native = original.ImplementationType
            ?? throw new InvalidOperationException(nameof(OrleansNativeHostedServiceRegistration));
        if (!services.Any(service => service.ServiceType == native))
        {
            services.Add(new ServiceDescriptor(native, native, original.Lifetime));
        }
        var index = services.IndexOf(original);
        services[index] = new ServiceDescriptor(typeof(IHostedService), provider =>
            new OrleansCreatedSiloHostedService((IHostedService)provider.GetRequiredService(native)), original.Lifetime);
    }
}
