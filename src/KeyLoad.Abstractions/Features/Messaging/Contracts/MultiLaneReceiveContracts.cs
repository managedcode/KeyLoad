using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Composes bounded independent lane claims; the outer ID has no durable group receipt.</summary>
/// <param name="RequestId">Outer correlation identity, distinct from every stable lane request ID.</param>
/// <param name="Requests">Ordered independent stable receive requests.</param>
[Orleans.GenerateSerializer, Orleans.Alias(NativeContractAliases.MultiLaneReceiveRequest)]
public sealed record MultiLaneReceiveRequest([property: Orleans.Id(0)] Guid RequestId,
    [property: Orleans.Id(1)] ImmutableArray<ReceiveRequest> Requests);

/// <summary>Describes the known result of an independent lane attempt.</summary>
public enum QueueLaneReceiveStatus
{
    /// <summary>The canonical lane claim committed, possibly with no deliveries.</summary>
    Committed,
    /// <summary>The lane returned a definitive safe rejection.</summary>
    Rejected,
    /// <summary>The submitted lane's write outcome is uncertain.</summary>
    Unknown,
    /// <summary>No claim was dispatched after an earlier uncertain attempt.</summary>
    NotAttempted
}

/// <summary>Carries exactly one committed result, safe failure, or absence of an attempt.</summary>
/// <param name="RequestId">Original stable lane receive identity.</param>
/// <param name="Lane">Original lane reference.</param>
/// <param name="Status">Truthful independent lane status.</param>
/// <param name="Result">Exact canonical committed claim, present only for Committed.</param>
/// <param name="Error">Exact reported error, present only for Rejected or Unknown.</param>
/// <param name="SafeDetail">Bounded safe detail accompanying Error.</param>
[Orleans.GenerateSerializer, Orleans.Alias(NativeContractAliases.QueueLaneReceiveOutcome)]
public sealed record QueueLaneReceiveOutcome([property: Orleans.Id(0)] Guid RequestId,
    [property: Orleans.Id(1)] QueueLaneRef Lane, [property: Orleans.Id(2)] QueueLaneReceiveStatus Status,
    [property: Orleans.Id(3)] ReceiveResult? Result = null, [property: Orleans.Id(4)] ErrorCode? Error = null,
    [property: Orleans.Id(5)] string? SafeDetail = null);

/// <summary>Returns all independent outcomes in original request order, without a group commit token.</summary>
/// <param name="RequestId">Outer request correlation identity.</param>
/// <param name="Outcomes">One outcome per original lane request.</param>
/// <param name="StopError">Original parent expiry error when remaining lanes were never dispatched.</param>
/// <param name="SafeDetail">Bounded safe detail for StopError.</param>
[Orleans.GenerateSerializer, Orleans.Alias(NativeContractAliases.MultiLaneReceiveResult)]
public sealed record MultiLaneReceiveResult([property: Orleans.Id(0)] Guid RequestId,
    [property: Orleans.Id(1)] ImmutableArray<QueueLaneReceiveOutcome> Outcomes,
    [property: Orleans.Id(2)] ErrorCode? StopError = null, [property: Orleans.Id(3)] string? SafeDetail = null);
