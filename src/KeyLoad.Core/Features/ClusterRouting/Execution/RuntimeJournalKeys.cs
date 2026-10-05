using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ClusterRouting.Execution;

internal static class RuntimeJournalKeys
{
    internal static byte[] Catalog() => KeyCodec.Encode(Contracts.RuntimeJournalProtocol.Space,
        Contracts.RuntimeJournalProtocol.CurrentVersion, Contracts.RuntimeJournalProtocol.MarkerKey);

    internal static byte[] Quota() => KeyCodec.Encode(Contracts.RuntimeJournalProtocol.Space,
        Contracts.RuntimeJournalProtocol.CurrentVersion, Contracts.RuntimeJournalProtocol.QuotaKey);

    internal static byte[] Header(string name) => KeyCodec.Encode(Contracts.RuntimeJournalProtocol.Space,
        Contracts.RuntimeJournalProtocol.CurrentVersion, Contracts.RuntimeJournalProtocol.HeaderKey, name);

    internal static byte[] HeadersPrefix() => KeyCodec.Encode(Contracts.RuntimeJournalProtocol.Space,
        Contracts.RuntimeJournalProtocol.CurrentVersion, Contracts.RuntimeJournalProtocol.HeaderKey);

    internal static byte[] Chunk(string name, int index) => KeyCodec.Encode(Contracts.RuntimeJournalProtocol.Space,
        Contracts.RuntimeJournalProtocol.CurrentVersion, Contracts.RuntimeJournalProtocol.ChunkKey, name, index);

    internal static byte[] ChunksPrefix(string name) => KeyCodec.Encode(Contracts.RuntimeJournalProtocol.Space,
        Contracts.RuntimeJournalProtocol.CurrentVersion, Contracts.RuntimeJournalProtocol.ChunkKey, name);

    internal static byte[] ChunksPrefix() => KeyCodec.Encode(Contracts.RuntimeJournalProtocol.Space,
        Contracts.RuntimeJournalProtocol.CurrentVersion, Contracts.RuntimeJournalProtocol.ChunkKey);
}
