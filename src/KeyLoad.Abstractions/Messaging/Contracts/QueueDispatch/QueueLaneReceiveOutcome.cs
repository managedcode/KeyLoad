namespace KeyLoad;

/// <summary>Carries exactly one committed result, safe failure, or absence of an attempt.</summary>
/// <param name="RequestId">Original stable lane receive identity.</param>
/// <param name="Lane">Original lane reference.</param>
/// <param name="Status">Truthful independent lane status.</param>
/// <param name="Result">Exact canonical committed claim, present only for Committed.</param>
/// <param name="Error">Exact reported error, present only for Rejected or Unknown.</param>
/// <param name="SafeDetail">Bounded safe detail accompanying Error.</param>
[Orleans.GenerateSerializer, Orleans.Alias(ContractAliases.QueueLaneReceiveOutcome)]
public sealed record QueueLaneReceiveOutcome([property: Orleans.Id(0)] Guid RequestId,
    [property: Orleans.Id(1)] QueueLaneRef Lane, [property: Orleans.Id(2)] QueueLaneReceiveStatus Status,
    [property: Orleans.Id(3)] ReceiveResult? Result = null, [property: Orleans.Id(4)] ErrorCode? Error = null,
    [property: Orleans.Id(5)] string? SafeDetail = null);
