using System.Security.Cryptography;

namespace KeyLoad.Server;

internal static class ServerNodeUpgradeInputValidation
{
    internal static void Verify(string inputs, ServerNodeUpgradeOwner owner, bool allowPartial = false)
    {
        var inventory = ServerNodeUpgradeInventory.Capture(inputs);
        foreach (var entry in inventory.Entries)
        { VerifyEntry(inputs, entry, owner, allowPartial); }
        if (!allowPartial && inventory.Entries.Count != 8)
        { throw Errors.Fail(ErrorCode.FormatUnsupported, ServerNodeUpgradeProtocol.Invalid); }
    }

    private static void VerifyEntry(string inputs, ServerNodeUpgradeEntry entry, ServerNodeUpgradeOwner owner, bool partial)
    {
        if (entry.Directory && entry.Path is ServerNodeUpgradeProtocol.Canonical or ServerNodeUpgradeProtocol.Replica)
        { return; }
        if (!entry.Directory && entry.Path is "database/owner.lock" or "replica/owner.lock" && entry.Length == 0)
        { return; }
        var expected = entry.Path switch
        {
            "database/identity.json" => owner.CanonicalIdentitySha256,
            "database/commands.wal" => owner.CanonicalJournalSha256,
            "replica/identity.json" => owner.ReplicaIdentitySha256,
            "replica/commands.wal" => owner.ReplicaJournalSha256,
            _ => throw Errors.Fail(ErrorCode.FormatUnsupported, ServerNodeUpgradeProtocol.Invalid)
        };
        if (entry.Directory || !partial && entry.Sha256 != expected)
        { throw Errors.Fail(ErrorCode.Corruption, ServerNodeUpgradeProtocol.Corrupt); }
        if (partial)
        { VerifyPrefix(Path.Combine(owner.OriginalSource, entry.Path), Path.Combine(inputs, entry.Path), entry.Length); }
    }

    private static void VerifyPrefix(string original, string copy, long length)
    {
        ServerNodeUpgradeFiles.RequireRegularFile(original);
        using var source = ServerNodeUpgradeFiles.OpenRead(original);
        if (length > source.Length)
        { throw Errors.Fail(ErrorCode.Corruption, ServerNodeUpgradeProtocol.Corrupt); }
        using var expected = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[ServerNodeUpgradeProtocol.BufferBytes];
        var remaining = length;
        while (remaining > 0)
        {
            var count = source.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
            if (count == 0)
            { throw Errors.Fail(ErrorCode.Corruption, ServerNodeUpgradeProtocol.Corrupt); }
            expected.AppendData(buffer, 0, count);
            remaining -= count;
        }
        using var actual = ServerNodeUpgradeFiles.OpenRead(copy);
        if (!CryptographicOperations.FixedTimeEquals(expected.GetHashAndReset(), SHA256.HashData(actual)))
        { throw Errors.Fail(ErrorCode.Corruption, ServerNodeUpgradeProtocol.Corrupt); }
    }
}
