using global::Orleans.Storage;
using global::Orleans.Streams;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.Clustering;

internal static class EventFeedProviderRegistration
{
    internal static void Register(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder) /* KLD0041: BCL null validation accepts the builder ISiloBuilder reference through its object parameter. */;
        builder.ConfigureServices(static services =>
        {
            services.AddKeyedSingleton<IGrainStorage>(
                EventFeedPubSubProtocol.ProviderName, static (originalServices, _) =>
                    new EventFeedPubSubStorage(originalServices)) /* KLD0041: The native keyed DI registration requires an object service key; this existing pub-sub provider uses its fixed string key and typed IGrainStorage factory. */;
        });
        builder.AddMemoryStreams(EventFeedPubSubProtocol.ProviderName, static provider =>
            provider.ConfigureComponent<IQueueAdapterFactory>(EventFeedAdapterCreation.Create));
    }
}
