using KeyLoad.Storage;

namespace KeyLoad.Core.Features.TimeSeries;

internal static class SampleChunkStorage
{
    internal static T? Read<T>(IKeyValueView view, byte[] key, SampleChunkReadCharge charge) where T : class
    {
        T? value = null;
        view.ReadValue(key, bytes => value = NativeSerialization.Deserialize<T>(bytes), charge.Charge);
        return value;
    }

    internal static T Require<T>(IKeyValueView view, byte[] key, SampleChunkReadCharge charge) where T : class
        => Read<T>(view, key, charge) ?? throw Errors.Fail(ErrorCode.Corruption, SampleChunkLifecycleProtocol.Corrupt);

    internal static void Write<T>(IAtomicTransaction tx, byte[] key, T value, int maximumBytes)
    {
        if (NativeSerialization.Measure(value) > maximumBytes)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, SampleChunkLifecycleProtocol.Exhausted); }
        tx.PutRecord(key, value);
    }

    internal static List<SampleChunkWindow> Windows(IKeyValueView view, PartitionRef partition, string set,
        string series, TimeSeriesExecutionOptions options, SampleChunkReadCharge charge)
    {
        var windows = new List<SampleChunkWindow>();
        var scan = view.VisitRange(SampleChunkKeys.Windows(partition, set, series), options.MaximumChunkWindows,
            (key, value) =>
            {
                var window = NativeSerialization.Deserialize<SampleChunkWindow>(value);
                SampleChunkWindowValidation.State(window, window.WindowId,
                    options.MaximumChunkWindowRecords, options.MaximumChunkCorrections);
                if (!key.SequenceEqual(SampleChunkKeys.Window(partition, set, series, window.WindowId)))
                { throw Errors.Fail(ErrorCode.Corruption, SampleChunkLifecycleProtocol.Corrupt); }
                windows.Add(window);
                return true;
            }, observer: charge.Charge);
        if (scan.HasMore)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, SampleChunkLifecycleProtocol.Exhausted); }
        var active = windows.Where(window => window.State != SampleChunkWindowState.Dropped)
            .OrderBy(window => window.FromUtcTicks).ToArray();
        for (var index = SampleChunkLifecycleProtocol.FirstIndex + SampleChunkLifecycleProtocol.First;
            index < active.Length; index++)
        {
            if (active[index - SampleChunkLifecycleProtocol.First].UntilUtcTicks > active[index].FromUtcTicks)
            { throw Errors.Fail(ErrorCode.Corruption, SampleChunkLifecycleProtocol.Corrupt); }
        }
        return windows;
    }
}
