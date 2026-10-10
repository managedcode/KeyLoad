using System.Collections.Immutable;
using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Core.Features.TimeSeries;

internal static class SampleChunkWindowReader
{
    internal static SampleChunkWindowResult Read(DatabaseEngine database, IKeyValueView view, string principalId,
        ReadSampleChunkWindowRequest request, ReadExecutionBudget budget,
        IOptions<TimeSeriesExecutionOptions> options, long cutPosition)
    {
        SampleChunkWindowValidation.Identity(request.SeriesId, request.WindowId);
        if (request.Limit < SampleChunkLifecycleProtocol.First || request.Limit > database.Limits.MaxResults)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, SampleChunkLifecycleProtocol.Exhausted); }
        var principal = database.Principal(view, principalId, database.EvaluationClock.GetUtcNow());
        database.Authorization.Require(principal, request.Partition, request.Set, Capability.SeriesRead);
        var resource = database.Resource(view, request.Partition, request.Set, ResourceKind.TimeSeries);
        var tag = SampleChunkTagPredicate.Prepare(database, principal, resource, request);
        var charge = new SampleChunkReadCharge(database.Limits.MaxQueryReadBytes);
        var window = SampleChunkStorage.Read<SampleChunkWindow>(view, SampleChunkKeys.Window(request.Partition,
            request.Set, request.SeriesId, request.WindowId), charge)
            ?? throw Errors.Fail(ErrorCode.NotFound, SampleChunkLifecycleProtocol.Missing);
        SampleChunkWindowValidation.State(window, request.WindowId, options.Value.MaximumChunkWindowRecords,
            options.Value.MaximumChunkCorrections);
        var watermark = SampleRollupRecords.Watermark(view, request.Partition, request.Set, request.SeriesId, charge.Charge);
        if (window.SourceSequence > watermark.Sequence)
        { throw Errors.Fail(ErrorCode.Corruption, SampleChunkLifecycleProtocol.Corrupt); }
        if (window.State == SampleChunkWindowState.Dropped)
        { throw Errors.Fail(ErrorCode.HistoryUnavailable, SampleChunkLifecycleProtocol.Missing); }
        var records = Records(view, request, window, options, charge, budget, tag);
        var selected = ImmutableArray.CreateBuilder<SampleRecord>();
        foreach (var record in records)
        {
            budget.Check();
            SampleChunkWindowValidation.Record(record, window, request.SeriesId);
            if (watermark.Floor is { } floor && record.Sample.Timestamp.UtcTicks < floor
                || !SampleChunkTagPredicate.Matches(record.TagsJson, tag, request.TagValue))
            { continue; }
            if (selected.Count >= request.Limit)
            { throw Errors.Fail(ErrorCode.BudgetExceeded, SampleChunkLifecycleProtocol.Exhausted); }
            var projected = record with
            {
                TagsJson = database.Authorization.Project(principal,
                resource.FieldPolicies, record.TagsJson, out _)
            };
            budget.CheckResult(projected);
            selected.Add(projected);
        }
        var result = new SampleChunkWindowResult(window.WindowId, new(window.FromUtcTicks, TimeSpan.Zero),
            new(window.UntilUtcTicks, TimeSpan.Zero), window.Generation, window.Revision, window.SourceSequence,
            watermark.Floor, selected.ToImmutable(), cutPosition);
        budget.CheckResult(result);
        budget.Check();
        return result;
    }

    private static SampleRecord[] Records(IKeyValueView view, ReadSampleChunkWindowRequest request,
        SampleChunkWindow window, IOptions<TimeSeriesExecutionOptions> options, SampleChunkReadCharge charge,
        ReadExecutionBudget budget, string[]? tag)
    {
        if (window.State == SampleChunkWindowState.Open)
        { return window.OpenRecords.OrderBy(record => record.Sample.Timestamp.UtcTicks).ThenBy(record => record.Sequence).ToArray(); }
        var work = SampleChunkWork.Observe(budget);
        var manifest = SampleChunkManifestReader.Read(view, request.Partition, request.Set, request.SeriesId,
            window, options.Value.MaximumChunkWindowRecords, charge, options, work);
        if (window.CorrectionSequences.IsEmpty && !manifest.TagJsonValues.Any(json =>
            SampleChunkTagPredicate.Matches(json, tag, request.TagValue)))
        { return []; }
        return SampleChunkGenerationReader.Read(view, request.Partition, request.Set, request.SeriesId,
            window, manifest, options, charge, work);
    }
}
