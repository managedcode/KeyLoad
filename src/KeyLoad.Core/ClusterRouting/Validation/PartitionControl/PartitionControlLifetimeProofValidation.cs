namespace KeyLoad.Core.Features.ClusterRouting.Validation;

internal static class PartitionControlLifetimeProofValidation
{
    internal static bool Same<T>(T expected, T actual)
        => NativeSerialization.Serialize(expected).AsSpan().SequenceEqual(NativeSerialization.Serialize(actual));
}
