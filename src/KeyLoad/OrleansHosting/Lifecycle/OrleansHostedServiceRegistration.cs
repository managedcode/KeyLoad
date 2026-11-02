namespace KeyLoad.Server.OrleansHosting;

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
        services[index] = (new ServiceDescriptor(typeof(IHostedService), provider =>
            new OrleansCreatedSiloHostedService(((IHostedService)(provider.GetRequiredService(execution) /* KLD0041: Resolve the actual Orleans hosted-service implementation Type from its native DI descriptor; the type is discovered from registered IHostedService metadata. */) /* KLD0042: The selected runtime implementation was found under the native IHostedService registration; require that exact service interface before wrapping its lifecycle. */)), original.Lifetime) /* KLD0041: Preserve the discovered Orleans hosted-service registration lifetime while using the native Type-based DI factory to join silo-created lifecycle work. */);
    }
}
