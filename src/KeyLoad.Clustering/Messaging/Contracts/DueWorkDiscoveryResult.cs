using KeyLoad.Core;
using KeyLoad.Core.Features.Messaging;

namespace KeyLoad.Clustering;

/// <summary>Distinguishes a real discovery page from a rejected prefix with its actual deferred cursor.</summary>
[GenerateSerializer, Alias(DueWorkDiscoveryResult.SerializerAlias)]
public sealed record DueWorkDiscoveryResult([property: Id(0)] DueWorkPage? Page,
    [property: Id(1)] DueSweepCursor Cursor, [property: Id(2)] ErrorCode? PrefixError)
{
    internal const string SerializerAlias = "keyload.orleans.due-work-discovery-result.v1";
}
