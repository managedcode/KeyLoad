using Orleans.Serialization;

namespace KeyLoad.Orleans;

internal static class GrainNativeByteCounter
{
    internal static long Measure<T>(Serializer<T> serializer, T value, int maximumBytes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(serializer);
        cancellationToken.ThrowIfCancellationRequested();
        using var destination = new GrainNativeCountingWriter(maximumBytes, cancellationToken);
        serializer.Serialize(value, destination);
        cancellationToken.ThrowIfCancellationRequested();
        return destination.Length;
    }
}
