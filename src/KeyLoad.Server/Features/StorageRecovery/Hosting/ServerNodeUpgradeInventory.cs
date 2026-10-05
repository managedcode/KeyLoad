using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace KeyLoad.Server;

internal sealed record ServerNodeUpgradeEntry(string Path, bool Directory, long Length, string Sha256);

internal sealed record ServerNodeUpgradeInventory(string Sha256, IReadOnlyList<ServerNodeUpgradeEntry> Entries)
{
    internal string FileDigest(string path)
        => Entries.FirstOrDefault(entry => !entry.Directory && entry.Path == path)?.Sha256
            ?? throw Errors.Fail(ErrorCode.FormatUnsupported, ServerNodeUpgradeProtocol.Invalid);

    internal int BackupCount => Entries.Count(entry => entry.Directory
        && entry.Path.StartsWith(ServerNodeUpgradeProtocol.Backups + ServerNodeUpgradeProtocol.PathSeparator, StringComparison.Ordinal)
        && entry.Path.Count(character => character == '/') == 1);

    internal static ServerNodeUpgradeInventory Capture(string directory,
        IReadOnlyDictionary<string, FileStream>? held = null, bool excludePreparedReceipt = false, bool excludeProgressReceipt = false)
    {
        ServerNodeUpgradePaths.CheckAncestors(directory, false);
        var collector = new ServerNodeUpgradeInventoryCollector(directory, held, excludePreparedReceipt, excludeProgressReceipt);
        var entries = collector.Collect();
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var entry in entries)
        { Append(digest, entry); }
        return new(Convert.ToHexStringLower(digest.GetHashAndReset()), entries);
    }

    internal void RequireSame(ServerNodeUpgradeInventory current)
    {
        if (!string.Equals(Sha256, current.Sha256, StringComparison.Ordinal))
        { throw Errors.Fail(ErrorCode.Corruption, ServerNodeUpgradeProtocol.Corrupt); }
    }

    private static void Append(IncrementalHash digest, ServerNodeUpgradeEntry entry)
    {
        var path = Encoding.UTF8.GetBytes(entry.Path);
        Span<byte> prefix = stackalloc byte[sizeof(int) + sizeof(byte) + sizeof(long)];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, path.Length);
        prefix[sizeof(int)] = entry.Directory ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteInt64LittleEndian(prefix[(sizeof(int) + sizeof(byte))..], entry.Length);
        digest.AppendData(prefix[..sizeof(int)]);
        digest.AppendData(path);
        digest.AppendData(prefix[sizeof(int)..]);
        digest.AppendData(Convert.FromHexString(entry.Sha256));
    }
}
