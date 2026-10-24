namespace KeyLoad.Core.Messaging;

/// <summary>Carries the actual retained transfer identity and its bounded coordination decisions.</summary>
[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteTransferCoordinationProtocol.HintAlias)]
public sealed record RemoteTransferCoordinationHint(
    [property: global::Orleans.Id(RemoteTransferCoordinationFields.Source)] QueueLaneRef Source,
    [property: global::Orleans.Id(RemoteTransferCoordinationFields.Destination)] QueueLaneRef Destination,
    [property: global::Orleans.Id(RemoteTransferCoordinationFields.TransferId)] Guid TransferId,
    [property: global::Orleans.Id(RemoteTransferCoordinationFields.PrincipalId)] string PrincipalId,
    [property: global::Orleans.Id(RemoteTransferCoordinationFields.Fingerprint)] string Fingerprint,
    [property: global::Orleans.Id(RemoteTransferCoordinationFields.IntentDigest)] string IntentDigest,
    [property: global::Orleans.Id(RemoteTransferCoordinationFields.SourceCut)] CommitToken SourceCut,
    [property: global::Orleans.Id(RemoteTransferCoordinationFields.AcceptGeneration)] long AcceptGeneration = RemoteTransferAttemptProtocol.FirstGeneration,
    [property: global::Orleans.Id(RemoteTransferCoordinationFields.AcceptAttemptCeiling)] int? AcceptAttemptCeiling = null,
    [property: global::Orleans.Id(RemoteTransferCoordinationFields.AcceptPolicyGeneration)] long AcceptPolicyGeneration = RemoteTransferRepairProtocol.InitialGeneration,
    [property: global::Orleans.Id(RemoteTransferCoordinationFields.CompleteGeneration)] long CompleteGeneration = RemoteTransferRepairProtocol.InitialGeneration,
    [property: global::Orleans.Id(RemoteTransferCoordinationFields.RepairCeiling)] int? RepairCeiling = null);
