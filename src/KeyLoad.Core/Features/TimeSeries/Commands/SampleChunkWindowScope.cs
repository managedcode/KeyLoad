using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Core.Features.TimeSeries;

internal static class SampleChunkWindowScope
{
    internal static void Require(DatabaseEngine database, IKeyValueView view, PrincipalRecord principal,
        PartitionRef partition, string set, string series, Guid id)
    {
        SampleChunkWindowValidation.Identity(series, id);
        database.Authorization.Require(principal, partition, set, Capability.SeriesManage | Capability.SeriesRead);
        _ = database.Resource(view, partition, set, ResourceKind.TimeSeries);
    }

    internal static SampleChunkWindow Load(IKeyValueView view, PartitionRef partition, string set,
        string series, Guid id, long revision, IOptions<TimeSeriesExecutionOptions> options, SampleChunkReadCharge charge)
    {
        var window = SampleChunkStorage.Read<SampleChunkWindow>(view, SampleChunkKeys.Window(partition, set, series, id), charge)
            ?? throw Errors.Fail(ErrorCode.NotFound, SampleChunkLifecycleProtocol.Missing);
        SampleChunkWindowValidation.State(window, id, options.Value.MaximumChunkWindowRecords,
            options.Value.MaximumChunkCorrections);
        if (window.Revision != revision || revision < SampleChunkLifecycleProtocol.First)
        { throw Errors.Fail(ErrorCode.RevisionConflict, SampleChunkLifecycleProtocol.RevisionConflict); }
        if (window.State == SampleChunkWindowState.Dropped)
        { throw Errors.Fail(ErrorCode.HistoryUnavailable, SampleChunkLifecycleProtocol.Missing); }
        return window;
    }
}
