namespace KeyLoad.Storage.ZoneTree;

[Orleans.GenerateSerializer, Orleans.Alias(ZoneTreeRestoreSlotEnvelope.SerializerAlias)]
internal sealed record ZoneTreeRestoreSlotEnvelope(
    [property: Orleans.Id(ZoneTreeRestoreSlotEnvelope.PayloadField)] ReadOnlyMemory<byte> Payload,
    [property: Orleans.Id(ZoneTreeRestoreSlotEnvelope.ChecksumField)] ReadOnlyMemory<byte> Checksum)
{
    internal const string SerializerAlias = "keyload.backup.cluster-restore.slot-envelope.v1";
    private const int PayloadField = 0;
    private const int ChecksumField = 1;
}
