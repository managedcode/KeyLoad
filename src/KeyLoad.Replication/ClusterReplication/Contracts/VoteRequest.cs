namespace KeyLoad.Replication;

/// <summary>Requests a vote with the candidate's latest durable log evidence.</summary>
/// <param name="CandidateId">Configured voter requesting election.</param>
/// <param name="Term">Proposed election term.</param>
/// <param name="LastIndex">Candidate's latest durable position.</param>
/// <param name="LastTerm">Term at the candidate's latest position.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ReplicaSerializationAliases.VoteRequest)]
public sealed record VoteRequest(
    [property: Orleans.Id(0)] string CandidateId,
    [property: Orleans.Id(1)] long Term,
    [property: Orleans.Id(2)] long LastIndex,
    [property: Orleans.Id(3)] long LastTerm);
