using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Returns all independent outcomes in original request order, without a group commit token.</summary>
/// <param name="RequestId">Outer request correlation identity.</param>
/// <param name="Outcomes">One outcome per original lane request.</param>
/// <param name="StopError">Original parent expiry error when remaining lanes were never dispatched.</param>
/// <param name="SafeDetail">Bounded safe detail for StopError.</param>
[Orleans.GenerateSerializer, Orleans.Alias(ContractAliases.MultiLaneReceiveResult)]
public sealed record MultiLaneReceiveResult([property: Orleans.Id(0)] Guid RequestId,
    [property: Orleans.Id(1)] ImmutableArray<QueueLaneReceiveOutcome> Outcomes,
    [property: Orleans.Id(2)] ErrorCode? StopError = null, [property: Orleans.Id(3)] string? SafeDetail = null);
