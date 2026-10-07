using System.Buffers.Binary;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class NativeStoreOpenPreflightTests
{
    private const int PayloadLengthOffset = 8;
    private const int ChecksumOffset = 20;
    private const int LengthMismatchDecrement = 1;
    private const int IncompleteHeaderBytes = 7;
    private const int ChangedByte = 1;
    private const int CurrentIdentityVersion = 7;
    private const ulong UnsupportedCheckpointMagic = ulong.MaxValue;
    private const ulong UnsupportedJournalMagic = ulong.MaxValue;
    private const string MissingReceipt = "The native preflight inspector did not return a receipt.";

    [Test]
    public async Task R12Ac002PreflightPreservesTheIncompleteTailAndResetsTheOwnedHandle()
    {
        using var files = new NativeStoreOpenPreflightFiles();
        files.Compact();
        await files.AppendAsync(files.Frame());
        await files.AppendAsync(files.Frame(NativeStoreOpenPreflightFiles.NextPosition)[..^ChangedByte]);
        var before = await files.CaptureHashesAsync();
        var options = files.Source.Options.ResolveExecutionOptions(UnitExecutionOptions.StorageExecution());
        using (var ownership = new FileStream(Path.Combine(files.Source.DirectoryPath, ZoneTreePersistenceFormat.OwnerLockFileName),
                   FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            using var journal = ZoneTreeStoreFiles.OpenJournal(options, FileMode.Open);
            journal.Position = ChangedByte;
            ZoneTreeJournalPreflight.Validate(journal, options, files.Source.Identity.FormatVersion);
            await Assert.That(journal.Position).IsEqualTo(0L);
        }
        await files.AssertHashesUnchangedAsync(before);
    }

    [Test]
    public async Task R12Ac002JournalTailRetainsTheOrdinaryRecoverySnapshotBudgetContract()
    {
        using var files = new NativeStoreOpenPreflightFiles();
        files.Compact();
        var snapshotBytes = new FileInfo(files.Source.JournalPath).Length;
        await files.AppendAsync(files.Frame());
        using var store = new ZoneTreeStore(files.Source.Options with { MaxSnapshotBytes = snapshotBytes }, UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        await Assert.That(store.Position).IsEqualTo(NativeStoreOpenPreflightFiles.TailPosition);
        await Assert.That(store.Read(view => view.ReadOwnedValue(files.Source.Key)))
            .IsEquivalentTo(NativeStoreOpenPreflightFiles.TailValue, CollectionOrdering.Matching);
    }

    [Test, Arguments(false), Arguments(true)]
    public async Task R12Ac002ValidCheckpointAndCurrentTailRecoverTheExactCut(bool guarded)
    {
        using var files = new NativeStoreOpenPreflightFiles();
        files.Compact();
        await files.AppendAsync(files.Frame());
        await AssertRecoveredAsync(files, guarded, NativeStoreOpenPreflightFiles.TailPosition);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task R12Ac002IncompleteCurrentTailIsOnlyTruncatedByOrdinaryRecovery(bool guarded, bool fullHeader)
    {
        using var files = new NativeStoreOpenPreflightFiles();
        files.Compact();
        await files.AppendAsync(files.Frame());
        var completeBytes = new FileInfo(files.Source.JournalPath).Length;
        var frame = files.Frame(NativeStoreOpenPreflightFiles.NextPosition);
        await files.AppendAsync(fullHeader ? frame[..^ChangedByte] : frame[..IncompleteHeaderBytes]);
        await AssertRecoveredAsync(files, guarded, NativeStoreOpenPreflightFiles.TailPosition);
        await Assert.That(new FileInfo(files.Source.JournalPath).Length).IsEqualTo(completeBytes);
    }

    [Test]
    [Arguments(false, NativeStoreOpenPreflightDefect.Checksum)]
    [Arguments(true, NativeStoreOpenPreflightDefect.Checksum)]
    [Arguments(false, NativeStoreOpenPreflightDefect.Sequence)]
    [Arguments(true, NativeStoreOpenPreflightDefect.Sequence)]
    [Arguments(false, NativeStoreOpenPreflightDefect.Semantics)]
    [Arguments(true, NativeStoreOpenPreflightDefect.Semantics)]
    [Arguments(false, NativeStoreOpenPreflightDefect.UnsupportedTornPayload)]
    [Arguments(true, NativeStoreOpenPreflightDefect.UnsupportedTornPayload)]
    [Arguments(false, NativeStoreOpenPreflightDefect.Checkpoint)]
    [Arguments(true, NativeStoreOpenPreflightDefect.Checkpoint)]
    [Arguments(false, NativeStoreOpenPreflightDefect.PayloadLengthMismatch)]
    [Arguments(true, NativeStoreOpenPreflightDefect.PayloadLengthMismatch)]
    public async Task R12Ac002CompleteCorruptionRejectsBeforeAnyProviderFileChanges(bool guarded, NativeStoreOpenPreflightDefect defect)
    {
        using var files = new NativeStoreOpenPreflightFiles();
        files.Compact();
        if (defect == NativeStoreOpenPreflightDefect.Checkpoint)
        {
            await files.DamageCheckpointAsync();
        }
        else
        {
            await files.AppendAsync(files.Frame());
            await files.AppendAsync(InvalidFrame(files, defect));
        }
        var before = await files.CaptureHashesAsync();
        var expected = defect == NativeStoreOpenPreflightDefect.UnsupportedTornPayload ? ErrorCode.FormatUnsupported : ErrorCode.Corruption;
        await AssertRejectedAsync(files, guarded, expected);
        await files.AssertHashesUnchangedAsync(before);
        await files.Source.AssertOwnerAvailableAsync();
    }

    [Test]
    public async Task CurrentOpenRejectsUnsupportedCheckpointMagicWithoutMutation()
    {
        using var files = new NativeStoreOpenPreflightFiles();
        files.Compact();
        await files.SetCheckpointMagicAsync(UnsupportedCheckpointMagic);
        var before = await files.CaptureHashesAsync();

        var error = Assert.ThrowsExactly<KeyLoadException>(() =>
        {
            using var store = new ZoneTreeStore(files.Source.Options, UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        });

        await Assert.That(error.Code).IsEqualTo(ErrorCode.FormatUnsupported);
        await files.AssertHashesUnchangedAsync(before);
        await files.Source.AssertOwnerAvailableAsync();
    }

    private static byte[] InvalidFrame(NativeStoreOpenPreflightFiles files, NativeStoreOpenPreflightDefect defect)
    {
        var frame = files.Frame(defect == NativeStoreOpenPreflightDefect.Sequence
            ? NativeStoreOpenPreflightFiles.TailPosition : NativeStoreOpenPreflightFiles.NextPosition);
        if (defect == NativeStoreOpenPreflightDefect.Checksum)
        {
            frame[ChecksumOffset] ^= ChangedByte;
        }
        if (defect == NativeStoreOpenPreflightDefect.PayloadLengthMismatch)
        {
            var declaredLength = BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(PayloadLengthOffset));
            BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(PayloadLengthOffset), declaredLength - LengthMismatchDecrement);
        }
        if (defect == NativeStoreOpenPreflightDefect.UnsupportedTornPayload)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(frame, UnsupportedJournalMagic);
            return frame[..WalFileFixture.HeaderBytes];
        }
        if (defect == NativeStoreOpenPreflightDefect.Semantics)
        {
            // A valid native payload/checksum with no mutations fails the owning journal contract.
            var payload = ZoneTreeJournalCodec.Serialize([], BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(PayloadLengthOffset)));
            return WalFileFixture.CreateFrame(payload, NativeStoreOpenPreflightFiles.NextPosition);
        }
        return frame;
    }

    private static async Task AssertRejectedAsync(NativeStoreOpenPreflightFiles files, bool guarded, ErrorCode expected)
    {
        if (guarded)
        {
            var result = await files.Source.InspectAsync(cancellationToken: TestContext.Current!.Execution.CancellationToken);
            await ExistingStoreInspectionAssertions.FailedAsync(result, expected.ToString());
            return;
        }
        var error = Assert.ThrowsExactly<KeyLoadException>(() =>
        {
            using var store = new ZoneTreeStore(files.Source.Options, UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        });
        await Assert.That(error.Code).IsEqualTo(expected);
    }

    private static async Task AssertRecoveredAsync(NativeStoreOpenPreflightFiles files, bool guarded, long position)
    {
        if (guarded)
        {
            var result = await files.Source.InspectAsync(cancellationToken: TestContext.Current!.Execution.CancellationToken);
            await ExistingStoreInspectionAssertions.SettledAsync(result);
            await Assert.That(result.ExitCode).IsEqualTo(0);
            var receipt = result.Receipt ?? throw new InvalidOperationException(MissingReceipt);
            await Assert.That(receipt.Success).IsTrue();
            await Assert.That(receipt.FormatVersion).IsEqualTo(CurrentIdentityVersion);
            await Assert.That(receipt.NodeId).IsEqualTo(files.Source.Identity.NodeId);
            await Assert.That(receipt.Incarnation).IsEqualTo(files.Source.Identity.Incarnation);
            await Assert.That(receipt.Position).IsEqualTo(position);
            await Assert.That(receipt.FailureTypes).IsEmpty();
            await Assert.That(receipt.ErrorCode).IsNull();
            await Assert.That(receipt.Value).IsEquivalentTo(NativeStoreOpenPreflightFiles.TailValue, CollectionOrdering.Matching);
            return;
        }
        using var store = new ZoneTreeStore(files.Source.Options, UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        await Assert.That(store.Position).IsEqualTo(position);
        await Assert.That(store.Read(view => view.ReadOwnedValue(files.Source.Key)))
            .IsEquivalentTo(NativeStoreOpenPreflightFiles.TailValue, CollectionOrdering.Matching);
    }
}

internal enum NativeStoreOpenPreflightDefect { Checksum, Sequence, Semantics, UnsupportedTornPayload, Checkpoint, PayloadLengthMismatch }
