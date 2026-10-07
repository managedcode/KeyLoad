using System.Text.Json;
using KeyLoad.CrashHost;
using KeyLoad.CrashHost.Features.DocumentStorage;

namespace KeyLoad.RecoveryTests.Features.TimeSeries;

internal static class SampleRollupProcessAssertions
{
    private const int MinimumBytes = 1;
    private const long InitialRevision = 1;
    private const long RecoveredRevision = 2;
    private const long RefreshedRevision = 3;
    private const long DroppedRevision = 4;
    private const long HealthyRevision = 5;
    private const long SourceSequence = 4;
    private const long Count = 3;
    private const double Sum = 12;
    private const double Minimum = 2;
    private const double Maximum = 6;
    private const double Average = 4;
    private const int HalfMinute = 30;
    private const int OffsetHours = -5;

    internal static async Task VerifyStageAsync(string root, string mode, bool drop, CancellationToken token)
    {
        switch (mode)
        {
            case SampleRollupCrashContract.PrepareMode:
                await LiteralAsync(root, SampleRollupCrashContract.PreparedFile, InitialRevision, false, token);
                break;
            case SampleRollupCrashContract.RefreshFaultMode:
            case SampleRollupCrashContract.DropFaultMode:
                await LiteralAsync(root, SampleRollupCrashContract.BeforeFile, InitialRevision, false, token);
                break;
            case SampleRollupCrashContract.RefreshRecoverMode:
            case SampleRollupCrashContract.DropRecoverMode:
                await LiteralAsync(root, SampleRollupCrashContract.RecoveredFile, RecoveredRevision, drop, token);
                await LiteralAsync(root, SampleRollupCrashContract.HealthyRefreshFile, RefreshedRevision, false, token);
                await LiteralAsync(root, SampleRollupCrashContract.HealthyDropFile, DroppedRevision, true, token);
                await LiteralAsync(root, SampleRollupCrashContract.HealthyFile, HealthyRevision, false, token);
                break;
            case SampleRollupCrashContract.VerifyMode:
                await LiteralAsync(root, SampleRollupCrashContract.FinalFile, HealthyRevision, false, token);
                var healthy = await ReadAsync(root, SampleRollupCrashContract.HealthyFile, token);
                var final = await ReadAsync(root, SampleRollupCrashContract.FinalFile, token);
                await Assert.That(JsonDefaults.Serialize(final).SequenceEqual(JsonDefaults.Serialize(healthy))).IsTrue();
                break;
            default:
                throw new InvalidOperationException(SampleRollupCrashContract.Invalid);
        }
    }

    private static async Task LiteralAsync(string root, string file, long revision, bool absent, CancellationToken token)
    {
        var actual = await ReadAsync(root, file, token);
        var start = SampleRollupCrashContract.Start;
        var end = SampleRollupCrashContract.End;
        SampleRecord[] expected = [new(SampleRollupCrashContract.Series, new("a", start, 2), 1, SampleRollupCrashContract.Tags),
            new(SampleRollupCrashContract.Series, new("b", start.AddSeconds(HalfMinute), 4), 2, SampleRollupCrashContract.Tags),
            new(SampleRollupCrashContract.Series, new("c", start.AddSeconds(HalfMinute).ToOffset(TimeSpan.FromHours(OffsetHours)), 6), 3, SampleRollupCrashContract.Tags),
            new(SampleRollupCrashContract.Series, new("boundary", end, 8), 4, SampleRollupCrashContract.Tags)];
        await Assert.That(actual.Samples.SequenceEqual(expected)).IsTrue();
        await Assert.That(actual.Rollup).IsEqualTo(new SampleRollupResult(revision, absent ? null :
            new(start, end, SourceSequence, start, new(Count, Sum, Minimum, Maximum, Average))));
        var raw = await File.ReadAllBytesAsync(Path.Combine(root, SampleRollupCrashContract.RawFile), token);
        await Assert.That(actual.RawImage).IsEqualTo(Convert.ToHexString(raw));
        var originalReceipt = await File.ReadAllBytesAsync(Path.Combine(root, SampleRollupCrashContract.AcknowledgedReceiptFile), token);
        await Assert.That(actual.AcknowledgedReceipt).IsEqualTo(Convert.ToHexString(originalReceipt));
        var prepared = file == SampleRollupCrashContract.PreparedFile ? actual
            : await ReadAsync(root, SampleRollupCrashContract.PreparedFile, token);
        var expectedPosition = revision switch
        {
            InitialRevision => prepared.Position,
            RecoveredRevision => checked(prepared.Position + SampleRollupCrashContract.PositionStep),
            RefreshedRevision => checked(prepared.Position + RecoveredRevision),
            DroppedRevision => checked(prepared.Position + RefreshedRevision),
            HealthyRevision => checked(prepared.Position + SourceSequence),
            _ => throw new InvalidOperationException(SampleRollupCrashContract.Invalid)
        };
        await Assert.That(actual.Position).IsEqualTo(expectedPosition);
        var receipt = NativeSerialization.Deserialize<CommitReceipt>(originalReceipt);
        await Assert.That(receipt.CommandId).IsEqualTo(SampleRollupCrashContract.AcknowledgedId);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(SampleRollupCrashContract.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Position).IsEqualTo(prepared.Position);
        await Assert.That(receipt.Token.Incarnation).IsNotEqualTo(Guid.Empty);
        await Assert.That(receipt.Token.OwnershipEpoch).IsEqualTo(InitialRevision);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
        await Assert.That(receipt.Mutations.Length).IsEqualTo(SampleRollupCrashContract.PositionStep);
        var expectedMutation = new MutationReceipt(SampleRollupProtocol.RefreshKind,
            SampleRollupCrashContract.Set, SampleRollupCrashContract.Series, InitialRevision);
        await Assert.That(NativeSerialization.Serialize(receipt.Mutations[SampleRollupCrashContract.MutationIndex])
            .SequenceEqual(NativeSerialization.Serialize(expectedMutation))).IsTrue();
    }

    private static async Task<SampleRollupCrashSnapshot> ReadAsync(string root, string file, CancellationToken token)
    {
        var path = Path.Combine(root, file);
        var info = new FileInfo(path);
        if (!info.Exists || (info.Attributes & FileAttributes.ReparsePoint) != 0
            || info.Length < MinimumBytes || info.Length > CommandIdempotencyCrashContract.EvidenceMaximumBytes)
        { throw new InvalidOperationException(SampleRollupCrashContract.Invalid); }
        return JsonSerializer.Deserialize<SampleRollupCrashSnapshot>(await File.ReadAllBytesAsync(path, token), JsonDefaults.Options)
            ?? throw new InvalidOperationException(SampleRollupCrashContract.Invalid);
    }
}
