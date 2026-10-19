using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Composes bounded independent lane claims; the outer ID has no durable group receipt.</summary>
/// <param name="RequestId">Outer correlation identity, distinct from every stable lane request ID.</param>
/// <param name="Requests">Ordered independent stable receive requests.</param>
[Orleans.GenerateSerializer, Orleans.Alias(ContractAliases.MultiLaneReceiveRequest)]
public sealed record MultiLaneReceiveRequest([property: Orleans.Id(0)] Guid RequestId,
    [property: Orleans.Id(1)] ImmutableArray<ReceiveRequest> Requests);
