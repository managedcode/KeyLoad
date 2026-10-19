namespace KeyLoad.Server.Features.OrleansHosting;

internal static class OrleansHostedServiceRegistration
{
    private const string RuntimeSiloHostedService = "Orleans.Hosting.SiloHostedService";

    internal static void Guard(IServiceCollection services)
    {
        var original = services.Single(service => service.ServiceType == typeof(IHostedService)
            && service.ImplementationType?.FullName == RuntimeSiloHostedService);
        var execution = original.ImplementationType
            ?? throw new InvalidOperationException(nameof(OrleansHostedServiceRegistration));
        if (!services.Any(service => service.ServiceType == execution))
        {
            services.Add(new ServiceDescriptor(execution, execution, original.Lifetime));
        }
        var index = services.IndexOf(original);
        services[index] = new ServiceDescriptor(typeof(IHostedService), provider =>
            new OrleansCreatedSiloHostedService((IHostedService)provider.GetRequiredService(execution)), original.Lifetime);
    }
}
