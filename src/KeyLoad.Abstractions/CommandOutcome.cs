namespace KeyLoad;

/// <summary>Records the replayable result of a command for its principal and fingerprint.</summary>
/// <param name="Fingerprint">Identifies the canonical command fingerprint.</param>
/// <param name="PrincipalId">Identifies the principal receiving the delivery.</param>
/// <param name="Receipt">Contains the committed command receipt when successful.</param>
/// <param name="Error">Contains the domain failure when rejected.</param>
/// <param name="SafeDetail">Contains safe caller-visible failure detail.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.CommandOutcome)]
public sealed record CommandOutcome([property: Orleans.Id(0)] string Fingerprint, [property: Orleans.Id(1)] string PrincipalId, [property: Orleans.Id(2)] CommitReceipt? Receipt, [property: Orleans.Id(3)] ErrorCode? Error, [property: Orleans.Id(4)] string? SafeDetail);
