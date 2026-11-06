using System.Security.Cryptography;
using KeyLoad.Server;
using ManagedCode.Communication.Commands;
using P = KeyLoad.ProcessRecoveryTests.AnalyticsStore.UserAnalyticsProcessProtocol;

namespace KeyLoad.ProcessRecoveryTests.AnalyticsStore;

internal static class UserAnalyticsProcessState
{
    private const int EmptyLength = 0;
    private const int MaximumRecords = 256;
    private const int MaximumEvidenceBytes = 1_048_576;
    private const string Separator = ":";

    internal static UserAnalyticsProcessOriginal Capture(PartitionHost host, Command<DatabaseCommand> operation, CommitReceipt receipt)
    {
        var store = host.Database.Store;
        var identity = store.Identity;
        var rows = store.Read(view =>
        {
            var page = view.Scan([], MaximumRecords);
            if (page.HasMore)
            {
                throw new InvalidOperationException(P.Invalid);
            }

            return page.Records.Select(row => Convert.ToHexString(row.Key.Span) + Separator
                + Convert.ToHexString(row.Value.Span)).ToArray();
        });
        return new(identity with
        {
            SigningKey = ReadOnlyMemory<byte>.Empty
        }, store.Position, rows, operation,
            receipt, SHA256.HashData(identity.SigningKey.Span));
    }

    internal static void Require(PartitionHost host, UserAnalyticsProcessOriginal expected)
    {
        var actual = Capture(host, expected.Operation, expected.Receipt);
        if (actual.Identity.ReadGeneration < expected.Identity.ReadGeneration
            || actual.Position != expected.Position || !actual.Rows.SequenceEqual(expected.Rows, StringComparer.Ordinal)
            || !CryptographicOperations.FixedTimeEquals(actual.SigningKeyDigest, expected.SigningKeyDigest))
        {
            throw new InvalidOperationException(P.Invalid);
        }

        Equal(expected.Identity, actual.Identity with
        {
            ReadGeneration = expected.Identity.ReadGeneration
        });
    }

    internal static void Equal<T>(T expected, T actual)
    {
        if (!OrleansSerialization.Serialize(expected).AsSpan().SequenceEqual(OrleansSerialization.Serialize(actual)))
        {
            throw new InvalidOperationException(P.Invalid);
        }
    }

    internal static async Task WriteAsync(string root, UserAnalyticsProcessOriginal value, CancellationToken token)
    {
        var bytes = OrleansSerialization.Serialize(value);
        if (bytes.Length is <= EmptyLength or > MaximumEvidenceBytes)
        {
            throw new InvalidOperationException(P.Invalid);
        }

        await File.WriteAllBytesAsync(Path.Combine(root, P.EvidenceFile), bytes, token);
    }

    internal static async Task<UserAnalyticsProcessOriginal> ReadAsync(string root, CancellationToken token)
    {
        await using var input = new FileStream(Path.Combine(root, P.EvidenceFile), FileMode.Open,
            FileAccess.Read, FileShare.Read, CrashExecutionOptions.Child().Value.AuthorityReadBufferBytes, FileOptions.Asynchronous);
        if (input.Length is <= EmptyLength or > MaximumEvidenceBytes)
        {
            throw new InvalidOperationException(P.Invalid);
        }

        var bytes = new byte[checked((int)input.Length)];
        await input.ReadExactlyAsync(bytes, token);
        return OrleansSerialization.Deserialize<UserAnalyticsProcessOriginal>(bytes);
    }
}
