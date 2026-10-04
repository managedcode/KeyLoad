using System.Buffers.Binary;
using System.Security.Cryptography;
using KeyLoad.Diagnostics.Features.ResourceExecution;
using static KeyLoad.Storage.ZoneTree.ZoneTreePersistenceFormat;

namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeJournalPublication
{
    internal static void Publish(ZoneTreeStoreRuntime runtime, ZoneTreeTransaction transaction,
        StorageMutation[] changes, long nextPosition)
    {
        var payload = transaction.PreparePayload();
        var header = CreateHeader(payload, nextPosition);
        try
        {
            WriteAndFlushJournal(runtime, header, payload, nextPosition);
            runtime.Options.FaultObserver?.Invoke(CommitStage.JournalFlushed, nextPosition, 0);
            for (var i = 0; i < changes.Length; i++)
            {
                runtime.Apply(changes[i]);
                runtime.Options.FaultObserver?.Invoke(CommitStage.MutationApplied, nextPosition, i);
            }

            runtime.SetPosition(nextPosition);
            runtime.Options.FaultObserver?.Invoke(CommitStage.ApplyCompleted, nextPosition, changes.Length);
        }
        catch (Exception)
        {
            runtime.Poisoned = true;
            throw Errors.Fail(ErrorCode.UnknownWriteOutcome, UnknownWriteOutcome);
        }
    }

    private static void WriteAndFlushJournal(ZoneTreeStoreRuntime runtime, byte[] header, byte[] payload,
        long nextPosition)
    {
        var started = DatabasePhaseTelemetry.Begin();
        try
        {
            runtime.Journal.Write(header);
            runtime.Options.FaultObserver?.Invoke(CommitStage.HeaderWritten, nextPosition, 0);
            runtime.Journal.Write(payload);
            runtime.Options.FaultObserver?.Invoke(CommitStage.PayloadWritten, nextPosition, 0);
            runtime.Journal.Flush(true);
        }
        catch (Exception)
        {
            DatabasePhaseTelemetry.End(DatabasePhaseKind.AtomicJournalWriteFlush, DatabasePhaseOutcome.Faulted, started);
            throw;
        }

        DatabasePhaseTelemetry.End(DatabasePhaseKind.AtomicJournalWriteFlush, DatabasePhaseOutcome.Completed, started);
    }

    private static byte[] CreateHeader(byte[] payload, long nextPosition)
    {
        var header = new byte[HeaderLength];
        BinaryPrimitives.WriteUInt64LittleEndian(header, JournalMagic);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(PayloadLengthOffset), payload.Length);
        BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(SequenceOffset), nextPosition);
        SHA256.HashData(payload, header.AsSpan(ChecksumOffset));
        return header;
    }
}
