using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Core.Features.TimeSeries;

internal sealed class SampleChunkAppendOwner
{
    private readonly DatabaseEngine database;
    private readonly IAtomicTransaction transaction;
    private readonly PartitionRef partition;
    private readonly string set;
    private readonly string series;
    private readonly IOptions<TimeSeriesExecutionOptions> options;
    private readonly List<SampleChunkWindow> windows;
    private readonly SampleChunkReadCharge charge;

    internal SampleChunkAppendOwner(DatabaseEngine database, IAtomicTransaction transaction, PartitionRef partition,
        string set, string series, IOptions<TimeSeriesExecutionOptions> options)
    {
        this.database = database; this.transaction = transaction; this.partition = partition;
        this.set = set; this.series = series; this.options = options;
        charge = new(database.Limits.MaxQueryReadBytes);
        windows = SampleChunkStorage.Windows(transaction, partition, set, series, options.Value, charge);
    }

    internal void Append(SampleRecord record, DateTimeOffset evaluatedAt)
    {
        var index = windows.FindIndex(window => window.State != SampleChunkWindowState.Dropped
            && record.Sample.Timestamp.UtcTicks >= window.FromUtcTicks && record.Sample.Timestamp.UtcTicks < window.UntilUtcTicks);
        if (index < SampleChunkLifecycleProtocol.FirstIndex) { return; }
        var window = windows[index];
        var next = window with { SourceSequence = record.Sequence,
            Revision = checked(window.Revision + SampleChunkLifecycleProtocol.First) };
        if (window.State == SampleChunkWindowState.Open)
        {
            if (window.OpenRecords.Length >= options.Value.MaximumChunkWindowRecords)
            { throw Errors.Fail(ErrorCode.ResourceExhausted, SampleChunkLifecycleProtocol.Exhausted); }
            next = next with { OpenRecords = window.OpenRecords.Add(record) };
            if (next.OpenRecords.Length >= SampleChunkWire.MaximumRecords || evaluatedAt.UtcTicks >= window.UntilUtcTicks)
            { next = SampleChunkWindowSeal.Seal(database, transaction, partition, set, series, next, options); }
        }
        else { next = Correct(window, next, record); }
        SampleChunkStorage.Write(transaction, SampleChunkKeys.Window(partition, set, series, next.WindowId),
            next, database.Limits.MaxBatchBytes);
        windows[index] = next;
    }

    private SampleChunkWindow Correct(SampleChunkWindow original, SampleChunkWindow next, SampleRecord record)
    {
        var manifest = SampleChunkManifestReader.Read(transaction, partition, set, series, original,
            options.Value.MaximumChunkWindowRecords, charge, options);
        if (original.CorrectionSequences.Length >= options.Value.MaximumChunkCorrections
            || manifest.RecordCount + original.CorrectionSequences.Length >= options.Value.MaximumChunkWindowRecords)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, SampleChunkLifecycleProtocol.Exhausted); }
        if (original.CorrectionSequences.IsEmpty && windows.Count(window => window.MaintenanceCommandId != Guid.Empty)
            >= options.Value.MaximumPendingChunkWindows)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, SampleChunkLifecycleProtocol.Exhausted); }
        SampleChunkStorage.Write(transaction, SampleChunkKeys.Correction(partition, set, series, original.WindowId, record.Sequence),
            record, database.Limits.MaxBatchBytes);
        return next with { CorrectionSequences = original.CorrectionSequences.Add(record.Sequence),
            MaintenanceCommandId = new SampleChunkMaintenanceIdentity(partition, set, series,
                original.WindowId, original.Generation, next.Revision).CommandId() };
    }
}
