using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

internal static class ServerNodeUpgradeInputValidation
{
    internal static void Verify(string inputs, ServerNodeUpgradeOwner owner, IOptions<ServerNodeUpgradeExecutionOptions> executionOptions, bool allowPartial = false)
    {
        const int EmptyEntriesCount = 8;

        var inventory = ServerNodeUpgradeInventory.Capture(inputs, executionOptions: executionOptions);
        foreach (var entry in inventory.Entries)
        { VerifyEntry(inputs, entry, owner, allowPartial, executionOptions: executionOptions); }
        if (!allowPartial && inventory.Entries.Count != EmptyEntriesCount)
        { throw Errors.Fail(ErrorCode.FormatUnsupported, ServerNodeUpgradeProtocol.Invalid); }
    }

    private static void VerifyEntry(string inputs, ServerNodeUpgradeEntry entry, ServerNodeUpgradeOwner owner, bool partial, IOptions<ServerNodeUpgradeExecutionOptions> executionOptions)
    {
        const int EmptyEntryLength = 0;

        if (entry.Directory && entry.Path is ServerNodeUpgradeProtocol.Canonical or ServerNodeUpgradeProtocol.Replica)
        { return; }
        if (!entry.Directory && entry.Path is ServerNodeUpgradeProtocol.CanonicalOwnerPath or ServerNodeUpgradeProtocol.ReplicaOwnerPath && entry.Length == EmptyEntryLength)
        { return; }
        var expected = entry.Path switch
        {
            ServerNodeUpgradeProtocol.CanonicalIdentityPath => owner.CanonicalIdentitySha256,
            ServerNodeUpgradeProtocol.CanonicalJournalPath => owner.CanonicalJournalSha256,
            ServerNodeUpgradeProtocol.ReplicaIdentityPath => owner.ReplicaIdentitySha256,
            ServerNodeUpgradeProtocol.ReplicaJournalPath => owner.ReplicaJournalSha256,
            _ => throw Errors.Fail(ErrorCode.FormatUnsupported, ServerNodeUpgradeProtocol.Invalid)
        };
        if (entry.Directory || !partial && entry.Sha256 != expected)
        { throw Errors.Fail(ErrorCode.Corruption, ServerNodeUpgradeProtocol.Corrupt); }
        if (partial)
        { VerifyPrefix(Path.Combine(owner.OriginalSource, entry.Path), Path.Combine(inputs, entry.Path), entry.Length, executionOptions: executionOptions); }
    }

    private static void VerifyPrefix(string original, string copy, long length, IOptions<ServerNodeUpgradeExecutionOptions> executionOptions)
    {
        const int RemainingValidationBoundary = 0;
        const int OffsetEmptyCount = 0;
        const int EmptyCount = 0;

        ServerNodeUpgradeFiles.RequireRegularFile(original);
        using var source = ServerNodeUpgradeFiles.OpenRead(original, executionOptions: executionOptions);
        if (length > source.Length)
        { throw Errors.Fail(ErrorCode.Corruption, ServerNodeUpgradeProtocol.Corrupt); }
        using var expected = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[executionOptions.Value.FileBufferBytes];
        var remaining = length;
        while (remaining > RemainingValidationBoundary)
        {
            var count = source.Read(buffer, OffsetEmptyCount, (int)Math.Min(buffer.Length, remaining));
            if (count == EmptyCount)
            { throw Errors.Fail(ErrorCode.Corruption, ServerNodeUpgradeProtocol.Corrupt); }
            expected.AppendData(buffer, OffsetEmptyCount, count);
            remaining -= count;
        }
        using var actual = ServerNodeUpgradeFiles.OpenRead(copy, executionOptions: executionOptions);
        if (!CryptographicOperations.FixedTimeEquals(expected.GetHashAndReset(), SHA256.HashData(actual)))
        { throw Errors.Fail(ErrorCode.Corruption, ServerNodeUpgradeProtocol.Corrupt); }
    }
}
