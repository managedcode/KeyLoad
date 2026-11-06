namespace KeyLoad;

/// <summary>Confirms the persisted sequence and decision of one command without returning its payload.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(StorageCopyCommandAcknowledgement.SerializerAlias)]
public sealed record StorageCopyCommandAcknowledgement(
    [property: Orleans.Id(0)] long Sequence,
    [property: Orleans.Id(1)] bool Succeeded,
    [property: Orleans.Id(2)] ErrorCode? Error)
{
    internal const string SerializerAlias = "keyload.storage-copy-command-acknowledgement";
}
