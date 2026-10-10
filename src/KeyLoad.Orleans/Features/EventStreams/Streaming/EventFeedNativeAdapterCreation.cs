using global::Orleans.Configuration;
using global::Orleans.Providers;
using global::Orleans.Providers.Streams.Common;
using global::Orleans.Serialization;
using global::Orleans.Streams;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KeyLoad.Orleans;

internal static class EventFeedNativeAdapterCreation
{
    internal static IQueueAdapterFactory Create(IServiceProvider services, string providerName)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (providerName != EventFeedPubSubProtocol.ProviderName)
        { throw Errors.Fail(ErrorCode.Validation, EventFeedPubSubProtocol.Invalid); }
        var limits = services.GetRequiredService<IOptions<DatabaseLimits>>();
        limits.Value.Validate();
        var cacheOptions = new SimpleQueueCacheOptions { CacheSize = limits.Value.MaxResults };
        var cache = new SimpleQueueAdapterCache(cacheOptions, providerName,
            services.GetRequiredService<ILoggerFactory>());
        var admission = new EventFeedHintBatchAdmission(
            services.GetRequiredService<Serializer<MemoryMessageBody>>(), limits,
            services.GetRequiredService<IOptions<GrainRoutingOptions>>());
        var original = MemoryAdapterFactory<DefaultMemoryMessageBodySerializer>.Create(services, providerName);
        return new EventFeedQueueAdapterFactory(original, cache, admission);
    }
}
