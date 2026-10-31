using global::Orleans.Storage;
using global::Orleans.Streams;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.Clustering;

internal static class EventFeedProviderRegistration
{
    internal static void Register(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ConfigureServices(static services => services.AddKeyedSingleton<IGrainStorage>(
            EventFeedPubSubProtocol.ProviderName, static (originalServices, _) =>
                new EventFeedPubSubStorage(originalServices)));
        builder.AddMemoryStreams(EventFeedPubSubProtocol.ProviderName, static provider =>
            provider.ConfigureComponent<IQueueAdapterFactory>(EventFeedAdapterCreation.Create));
    }
}
