using global::Orleans.Storage;
using global::Orleans.Streams;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KeyLoad.Clustering;

internal static class EventFeedProviderRegistration
{
    internal static void Register(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ConfigureServices(static services => services.AddKeyedSingleton<IGrainStorage>(
            EventFeedPubSubProtocol.ProviderName, static (originalServices, _) =>
                new EventFeedPubSubStorage(originalServices,
                    originalServices.GetRequiredService<IOptions<GrainRoutingOptions>>())));
        builder.AddMemoryStreams(EventFeedPubSubProtocol.ProviderName, static provider =>
            provider.ConfigureComponent<IQueueAdapterFactory>(EventFeedAdapterCreation.Create));
    }
}
