using KeyLoad.Orleans;

namespace KeyLoad.Server;

internal static class NativeJobLifecycleRegistration
{
    private const string SingletonFactoryRequired = "Native jobs require their exact singleton lifecycle factory.";
    private const string IncompatibleParticipant = "The native job lifecycle participant is incompatible.";
    internal static bool IsLifecycle(ServiceDescriptor descriptor)
        => !descriptor.IsKeyedService && descriptor.ServiceType == typeof(ILifecycleParticipant<ISiloLifecycle>);

    internal static void Decorate(IServiceCollection services, IReadOnlyCollection<ServiceDescriptor> existing)
    {
        var added = services.Where(IsLifecycle).Except(existing).Single();
        var nativeType = NativeJobGraphRegistration.ManagerType(services);
        if (added.Lifetime != ServiceLifetime.Singleton || added.ImplementationFactory is not { } factory)
        {
            throw new InvalidOperationException(SingletonFactoryRequired);
        }
        services.Remove(added);
        services.AddSingleton<ILifecycleParticipant<ISiloLifecycle>>(provider =>
        {
            var native = factory(provider);
            if (native.GetType() != nativeType
                || !ReferenceEquals(native, provider.GetRequiredService(nativeType))
                || native is not ILifecycleParticipant<ISiloLifecycle> participant)
            {
                throw new InvalidOperationException(IncompatibleParticipant);
            }
            return new NativeJobLifecycleParticipant(participant,
                provider.GetRequiredService<NativeRequestWorkOwner>());
        });
    }
}
