using System.Security.Cryptography;

namespace KeyLoad.Orleans;

internal static class AnnProjectionJobProtocol
{
    internal const string StateAlias = "keyload.orleans.ann-projection-job-state.v1";
    internal const string GrainAlias = "keyload-ann-projection-coordinator-v1";
    internal const string StateName = "ann-original-maintenance";
    internal const string JobName = "ann-native-maintenance-v1";
    internal const string RequestDigestKey = "annRequestSha256";
    internal const string Invalid = "The native ANN durable operation binding is invalid.";
    internal const string Unresolved = "The original native ANN durable operation is unresolved.";
    internal const string Exhausted = "The native ANN durable operation exceeds its admitted journal bound.";
    internal const int InterfaceVersion = 1;
    internal const int MetadataCount = 1;
    internal const int FirstGeneration = 1;

    internal static string GrainKey(ProjectionConsumerRef consumer, long generation, Guid nodeId)
    {
        global::KeyLoad.Core.DatabaseEngine.ValidatePartition(consumer.Partition);
        global::KeyLoad.Core.JsonData.Identifier(consumer.Name);
        if (nodeId == Guid.Empty)
        { throw Errors.Fail(ErrorCode.Validation, Invalid); }
        if (generation < FirstGeneration)
        { throw Errors.Fail(ErrorCode.Validation, Invalid); }
        return Convert.ToHexString(SHA256.HashData(NativeSerialization.Serialize(new Identity(consumer, generation, nodeId))));
    }

    internal static string RequestDigest(AnnMaintenanceRequest request)
        => Convert.ToHexString(SHA256.HashData(NativeSerialization.Serialize(request)));

    internal static bool SameRequest(AnnMaintenanceRequest first, AnnMaintenanceRequest second)
        => NativeSerialization.Serialize(first).AsSpan().SequenceEqual(NativeSerialization.Serialize(second));

    [global::Orleans.GenerateSerializer, global::Orleans.Alias(IdentityAlias)]
    internal sealed record Identity([property: global::Orleans.Id(0)] ProjectionConsumerRef Consumer,
        [property: global::Orleans.Id(1)] long Generation,
        [property: global::Orleans.Id(2)] Guid NodeId);
    private const string IdentityAlias = "keyload.orleans.ann-projection-job-identity.v1";
}
