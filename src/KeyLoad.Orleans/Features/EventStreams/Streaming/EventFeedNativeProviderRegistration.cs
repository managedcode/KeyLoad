using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using global::Orleans.Storage;
using global::Orleans.Streams;

namespace KeyLoad.Orleans;

internal static class EventFeedNativeProviderRegistration
{
    internal static void Register(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ConfigureServices(static services => services.AddKeyedSingleton<IGrainStorage>(
            EventFeedPubSubProtocol.ProviderName, static (originalServices, _) =>
                new EventFeedPubSubStorage(originalServices,
                    originalServices.GetRequiredService<IOptions<DatabaseLimits>>(),
                    originalServices.GetRequiredService<IOptions<GrainRoutingOptions>>())));
        builder.AddMemoryStreams(EventFeedPubSubProtocol.ProviderName, static provider =>
            provider.ConfigureComponent<IQueueAdapterFactory>(EventFeedNativeAdapterCreation.Create));
    }
}
