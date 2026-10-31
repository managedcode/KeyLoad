using global::Orleans.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.Clustering;

internal static class EventFeedPubSubRowEncoding
{
    private const string ETagFormat = "N";

    internal static EventFeedPubSubRow Create<T>(IServiceProvider originalServices, GrainId grainId, T state)
    {
        ArgumentNullException.ThrowIfNull(originalServices);
        if (state is null)
        {
            throw Errors.Fail(ErrorCode.Validation, EventFeedPubSubProtocol.Invalid);
        }
        var value = originalServices.GetRequiredService<Serializer<T>>().SerializeToArray(state);
        return new(grainId, typeof(T), value, Guid.NewGuid().ToString(ETagFormat));
    }
}
