using System.Security.Cryptography;

namespace KeyLoad.IntegrationTests.Features.BackupRestore;

internal static class ClusterRestoreRf3ArchiveJournalMutation
{
    private const long StartOffset = 0;
    private const int OneByte = 1;
    private const int EndOfStream = -1;
    private const int NoAttributes = 0;
    private const string InvalidOwnedJournal = "The fixture-owned original archive journal is invalid.";

    internal static async Task<(byte FirstByte, byte[] Digest, long Length)> ChangeAsync(string path,
        CancellationToken cancellationToken)
    {
        var policy = IntegrationExecutionOptions.StorageExecution().Value;
        RequireRegular(path);
        await using var file = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None,
            policy.StreamBufferBytes, FileOptions.Asynchronous);
        if (file.Length < OneByte || file.Length > policy.MaxSnapshotBytes)
        { throw new InvalidOperationException(InvalidOwnedJournal); }
        var length = file.Length;
        var digest = await SHA256.HashDataAsync(file, cancellationToken).ConfigureAwait(false);
        file.Position = StartOffset;
        var original = file.ReadByte();
        if (original == EndOfStream)
        { throw new InvalidOperationException(InvalidOwnedJournal); }
        file.Position = StartOffset;
        await file.WriteAsync(new[] { checked((byte)(original ^ OneByte)) }, cancellationToken).ConfigureAwait(false);
        FlushDurably(file);
        await Assert.That(file.Length).IsEqualTo(length);
        file.Position = StartOffset;
        await Assert.That((await SHA256.HashDataAsync(file, cancellationToken).ConfigureAwait(false)).AsSpan()
            .SequenceEqual(digest)).IsFalse();
        return (checked((byte)original), digest, length);
    }

    internal static async Task RepairAsync(string path, (byte FirstByte, byte[] Digest, long Length) original,
        CancellationToken cancellationToken)
    {
        var policy = IntegrationExecutionOptions.StorageExecution().Value;
        RequireRegular(path);
        await using var file = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None,
            policy.StreamBufferBytes, FileOptions.Asynchronous);
        await Assert.That(file.Length).IsEqualTo(original.Length);
        file.Position = StartOffset;
        await file.WriteAsync(new[] { original.FirstByte }, cancellationToken).ConfigureAwait(false);
        FlushDurably(file);
        file.Position = StartOffset;
        await Assert.That((await SHA256.HashDataAsync(file, cancellationToken).ConfigureAwait(false)).AsSpan()
            .SequenceEqual(original.Digest)).IsTrue();
        await Assert.That(file.Length).IsEqualTo(original.Length);
    }

    private static void FlushDurably(FileStream file) => file.Flush(true);

    private static void RequireRegular(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != NoAttributes)
        { throw new InvalidOperationException(InvalidOwnedJournal); }
    }
}
