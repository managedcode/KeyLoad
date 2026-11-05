namespace KeyLoad.Clustering;

/// <summary>Confirms the persisted sequence and decision of one command without returning its payload.</summary>
[GenerateSerializer]
[Alias(StorageCopyCommandAcknowledgement.SerializerAlias)]
public sealed record StorageCopyCommandAcknowledgement(
    [property: Id(0)] long Sequence,
    [property: Id(1)] bool Succeeded,
    [property: Id(2)] ErrorCode? Error)
{
    internal const string SerializerAlias = "keyload.storage-copy-command-acknowledgement";
}
