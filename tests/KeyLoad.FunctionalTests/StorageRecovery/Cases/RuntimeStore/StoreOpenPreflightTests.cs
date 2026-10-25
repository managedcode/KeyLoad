using System.Buffers.Binary;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.FunctionalTests.StorageRecovery;

internal sealed class StoreOpenPreflightTests
{
    private const int PayloadLengthOffset = 8;
    private const int ChecksumOffset = 20;
    private const int LengthMismatchDecrement = 1;
    private const int IncompleteHeaderBytes = 7;
    private const int ChangedByte = 1;
    private const int CurrentIdentityVersion = 7;
    private const ulong UnsupportedCheckpointMagic = ulong.MaxValue;
    private const ulong UnsupportedJournalMagic = ulong.MaxValue;
    private const string MissingReceipt = "The execution preflight inspector did not return a receipt.";

    [Test]
    public async Task R12Ac002PreflightPreservesTheIncompleteTailAndResetsTheOwnedHandle()
    {
        using var files = new StoreOpenPreflightFiles();
        files.Compact();
        await files.AppendAsync(files.Frame());
        await files.AppendAsync(files.Frame(StoreOpenPreflightFiles.NextPosition)[..^ChangedByte]);
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
        using var files = new StoreOpenPreflightFiles();
        files.Compact();
        var snapshotBytes = new FileInfo(files.Source.JournalPath).Length;
        await files.AppendAsync(files.Frame());
        using var store = new ZoneTreeStore(files.Source.Options with
        {
            MaxSnapshotBytes = snapshotBytes
        }, UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        await Assert.That(store.Position).IsEqualTo(StoreOpenPreflightFiles.TailPosition);
        await Assert.That(store.Read(view => view.ReadOwnedValue(files.Source.Key)))
            .IsEquivalentTo(StoreOpenPreflightFiles.TailValue, CollectionOrdering.Matching);
    }

    [Test, Arguments(false), Arguments(true)]
    public async Task R12Ac002ValidCheckpointAndCurrentTailRecoverTheExactCut(bool guarded)
    {
        using var files = new StoreOpenPreflightFiles();
        files.Compact();
        await files.AppendAsync(files.Frame());
        await AssertRecoveredAsync(files, guarded, StoreOpenPreflightFiles.TailPosition);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task R12Ac002IncompleteCurrentTailIsOnlyTruncatedByOrdinaryRecovery(bool guarded, bool fullHeader)
    {
        using var files = new StoreOpenPreflightFiles();
        files.Compact();
        await files.AppendAsync(files.Frame());
        var completeBytes = new FileInfo(files.Source.JournalPath).Length;
        var frame = files.Frame(StoreOpenPreflightFiles.NextPosition);
        await files.AppendAsync(fullHeader ? frame[..^ChangedByte] : frame[..IncompleteHeaderBytes]);
        await AssertRecoveredAsync(files, guarded, StoreOpenPreflightFiles.TailPosition);
        await Assert.That(new FileInfo(files.Source.JournalPath).Length).IsEqualTo(completeBytes);
    }

    [Test]
    [Arguments(false, StoreOpenPreflightDefect.Checksum)]
    [Arguments(true, StoreOpenPreflightDefect.Checksum)]
    [Arguments(false, StoreOpenPreflightDefect.Sequence)]
    [Arguments(true, StoreOpenPreflightDefect.Sequence)]
    [Arguments(false, StoreOpenPreflightDefect.Semantics)]
    [Arguments(true, StoreOpenPreflightDefect.Semantics)]
    [Arguments(false, StoreOpenPreflightDefect.UnsupportedTornPayload)]
    [Arguments(true, StoreOpenPreflightDefect.UnsupportedTornPayload)]
    [Arguments(false, StoreOpenPreflightDefect.Checkpoint)]
    [Arguments(true, StoreOpenPreflightDefect.Checkpoint)]
    [Arguments(false, StoreOpenPreflightDefect.PayloadLengthMismatch)]
    [Arguments(true, StoreOpenPreflightDefect.PayloadLengthMismatch)]
    public async Task R12Ac002CompleteCorruptionRejectsBeforeAnyProviderFileChanges(bool guarded, StoreOpenPreflightDefect defect)
    {
        using var files = new StoreOpenPreflightFiles();
        files.Compact();
        if (defect == StoreOpenPreflightDefect.Checkpoint)
        {
            await files.DamageCheckpointAsync();
        }
        else
        {
            await files.AppendAsync(files.Frame());
            await files.AppendAsync(InvalidFrame(files, defect));
        }
        var before = await files.CaptureHashesAsync();
        var expected = defect == StoreOpenPreflightDefect.UnsupportedTornPayload ? ErrorCode.FormatUnsupported : ErrorCode.Corruption;
        await AssertRejectedAsync(files, guarded, expected);
        await files.AssertHashesUnchangedAsync(before);
        await files.Source.AssertOwnerAvailableAsync();
    }

    [Test]
    public async Task CurrentOpenRejectsUnsupportedCheckpointMagicWithoutMutation()
    {
        using var files = new StoreOpenPreflightFiles();
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

    private static byte[] InvalidFrame(StoreOpenPreflightFiles files, StoreOpenPreflightDefect defect)
    {
        var frame = files.Frame(defect == StoreOpenPreflightDefect.Sequence
            ? StoreOpenPreflightFiles.TailPosition : StoreOpenPreflightFiles.NextPosition);
        if (defect == StoreOpenPreflightDefect.Checksum)
        {
            frame[ChecksumOffset] ^= ChangedByte;
        }
        if (defect == StoreOpenPreflightDefect.PayloadLengthMismatch)
        {
            var declaredLength = BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(PayloadLengthOffset));
            BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(PayloadLengthOffset), declaredLength - LengthMismatchDecrement);
        }
        if (defect == StoreOpenPreflightDefect.UnsupportedTornPayload)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(frame, UnsupportedJournalMagic);
            return frame[..WalFileFixture.HeaderBytes];
        }
        if (defect == StoreOpenPreflightDefect.Semantics)
        {
            // A valid execution payload/checksum with no mutations fails the owning journal contract.
            var payload = ZoneTreeJournalCodec.Serialize([], BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(PayloadLengthOffset)));
            return WalFileFixture.CreateFrame(payload, StoreOpenPreflightFiles.NextPosition);
        }
        return frame;
    }

    private static async Task AssertRejectedAsync(StoreOpenPreflightFiles files, bool guarded, ErrorCode expected)
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

    private static async Task AssertRecoveredAsync(StoreOpenPreflightFiles files, bool guarded, long position)
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
            await Assert.That(receipt.Value).IsEquivalentTo(StoreOpenPreflightFiles.TailValue, CollectionOrdering.Matching);
            return;
        }
        using var store = new ZoneTreeStore(files.Source.Options, UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        await Assert.That(store.Position).IsEqualTo(position);
        await Assert.That(store.Read(view => view.ReadOwnedValue(files.Source.Key)))
            .IsEquivalentTo(StoreOpenPreflightFiles.TailValue, CollectionOrdering.Matching);
    }
}
