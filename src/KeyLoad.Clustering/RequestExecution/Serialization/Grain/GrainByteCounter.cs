using Microsoft.Extensions.Options;
using Orleans.Serialization;

namespace KeyLoad.Clustering;

internal static class GrainByteCounter
{
    internal static long Measure<T>(Serializer<T> serializer, T value, int maximumBytes, IOptions<GrainRoutingOptions> options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(serializer);
        cancellationToken.ThrowIfCancellationRequested();
        using var destination = new GrainCountingWriter(maximumBytes: maximumBytes, cancellationToken: cancellationToken, options: options);
        serializer.Serialize(value, destination);
        cancellationToken.ThrowIfCancellationRequested();
        return destination.Length;
    }
}
