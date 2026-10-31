using global::Orleans.Configuration;
using global::Orleans.Providers;
using global::Orleans.Providers.Streams.Common;
using global::Orleans.Streams;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KeyLoad.Clustering;

internal static class EventFeedAdapterCreation
{
    internal static IQueueAdapterFactory Create(IServiceProvider services, string providerName)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (providerName != EventFeedPubSubProtocol.ProviderName)
        {
            throw Errors.Fail(ErrorCode.Validation, EventFeedPubSubProtocol.Invalid);
        }
        var cacheOptions = new SimpleQueueCacheOptions();
        var cache = new SimpleQueueAdapterCache(cacheOptions, providerName,
            services.GetRequiredService<ILoggerFactory>());
        var original = MemoryAdapterFactory<DefaultMemoryMessageBodySerializer>.Create(services, providerName);
        return new EventFeedQueueAdapterFactory(original, cache);
    }
}
