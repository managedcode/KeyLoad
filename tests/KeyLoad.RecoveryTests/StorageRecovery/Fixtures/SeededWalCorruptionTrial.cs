using System.Security.Cryptography;
using KeyLoad.RecoveryHost;

namespace KeyLoad.RecoveryTests.Features.StorageRecovery;

internal sealed class SeededWalCorruptionTrial(int seed)
{
    private const string JournalName = "commands.wal";
    private const int MaximumJournalBytes = 1_048_576;
    private const int ByteBits = 8;
    private const string Invalid = "The seeded native WAL original is unavailable or changed.";
    internal string Root { get; } = Path.Combine(Path.GetTempPath(), "keyload-seeded-wal-corruption-" + Guid.NewGuid().ToString("N"));
    internal int Seed { get; } = seed;
    internal byte[] Original { get; private set; } = [];
    internal byte[] Damaged { get; private set; } = [];
    internal Dictionary<string, string> OriginalFiles { get; private set; } = new(StringComparer.Ordinal);
    internal Dictionary<string, string> DamagedFiles { get; private set; } = new(StringComparer.Ordinal);
    internal List<Dictionary<string, string>> RefusalFiles { get; } = [];
    internal Dictionary<string, string> RestoredFiles { get; set; } = new(StringComparer.Ordinal);
    internal List<Dictionary<string, string>> FollowUpFiles { get; } = [];
    internal int Offset => Original.Length - 1;
    internal byte Mask => (byte)(1 << (Seed % ByteBits));
    internal string OriginalHash => Convert.ToHexStringLower(SHA256.HashData(Original));
    internal string DamagedHash => Convert.ToHexStringLower(SHA256.HashData(Damaged));
    internal long Position
    {
        get; private set;
    }
    internal async Task CaptureAsync(CancellationToken token)
    {
        if (Seed <= 0)
        {
            throw new InvalidOperationException(Invalid);
        }
        var file = new FileInfo(Path.Combine(Root, JournalName));
        if (!file.Exists || file.LinkTarget is not null || file.Length is <= 0 or > MaximumJournalBytes)
        {
            throw new InvalidOperationException(Invalid);
        }
        RecoveryFileInventory.AssertNativeHandlesReleased(Root);
        Original = await File.ReadAllBytesAsync(file.FullName, token);
        Position = SeededWalCorruptionFrame.Validate(Original);
        var cut = NativeSerialization.Deserialize<NativeChecksumProfileCut>(await File.ReadAllBytesAsync(
            Path.Combine(Root, NativeChecksumProfileProtocol.CutFile), token));
        await Assert.That(Position).IsEqualTo(cut.Position);
        await Assert.That(cut.Rows.Length).IsGreaterThanOrEqualTo(NativeChecksumProfileProtocol.SeedRecords);
        OriginalFiles = await RecoveryFileInventory.CaptureAsync(Root, token);
        Damaged = Original.ToArray();
        Damaged[Offset] ^= Mask;
    }
    internal async Task DamageAsync(CancellationToken token)
    {
        if (Original.Length == 0 || Damaged.Length != Original.Length)
        {
            throw new InvalidOperationException(Invalid);
        }
        await WriteExactAsync(Damaged, token);
        DamagedFiles = await RecoveryFileInventory.CaptureAsync(Root, token);
    }
    internal async Task RepairAsync(CancellationToken token)
    {
        if (Original.Length == 0)
        {
            throw new InvalidOperationException(Invalid);
        }
        await WriteExactAsync(Original, token);
    }
    private async Task WriteExactAsync(byte[] bytes, CancellationToken token)
    {
        RecoveryFileInventory.AssertNativeHandlesReleased(Root);
        await using var file = new FileStream(Path.Combine(Root, JournalName), new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.ReadWrite,
            Share = FileShare.None,
            Options = FileOptions.Asynchronous
        });
        if (file.Length != bytes.LongLength)
        {
            throw new InvalidOperationException(Invalid);
        }
        await file.WriteAsync(bytes, token);
        await file.FlushAsync(token);
        RandomAccess.FlushToDisk(file.SafeFileHandle);
    }
}
