using Orleans;

namespace KeyLoad.Storage.ZoneTree;

[GenerateSerializer]
[Alias(ZoneTreeJournalMutation.TypeAlias)]
internal readonly struct ZoneTreeJournalMutation
{
    internal const string TypeAlias = "keyload.storage.wal.mutation.v1";
    internal const byte PutKind = 1;
    internal const byte DeleteKind = 2;

    [Id(0)]
    public ReadOnlyMemory<byte> Key { get; init; }

    [Id(1)]
    public ReadOnlyMemory<byte>? Value { get; init; }

    [Id(2)]
    public byte Kind { get; init; }
}
