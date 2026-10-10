using System.Globalization;
using KeyLoad.IntegrationTests.Features.ClusterReplication;

namespace KeyLoad.IntegrationTests.Features.Search;

internal sealed class NativeTextMaintenanceFlowObservation
{
    private const string TimeFormat = "R";
    private const string Unobserved = "Unobserved";
    private static readonly TimeProvider Clock = TimeProvider.System;
    private readonly long started = Clock.GetTimestamp();
    private readonly Sample[] samples = Enum.GetValues<NativeTextMaintenanceFlowPhase>().Select(_ => new Sample()).ToArray();
    private readonly ClusterFailureReceipts receipts = new();
    private readonly string output;

    internal NativeTextMaintenanceFlowObservation()
        => output = receipts.RequireRunOutput(TestContext.ResultsDirectory);

    internal async Task MeasureAsync(NativeTextMaintenanceFlowPhase phase, Func<Task> operation)
    {
        var sample = Begin(phase);
        var completed = false;
        try
        { await operation().ConfigureAwait(false); completed = true; }
        finally { End(sample, completed); }
    }

    internal async Task<T> MeasureAsync<T>(NativeTextMaintenanceFlowPhase phase, Func<Task<T>> operation)
    {
        var sample = Begin(phase);
        var completed = false;
        try
        {
            var result = await operation().ConfigureAwait(false);
            completed = true;
            return result;
        }
        finally { End(sample, completed); }
    }

    internal async Task<T> FinalSearchAsync<T>(NativeTextMaintenanceFlowPhase phase,
        Func<Task<T>> operation, CancellationToken token)
    {
        var sample = Begin(phase);
        sample.CancelledBefore = token.IsCancellationRequested;
        try
        {
            var result = await operation().ConfigureAwait(false);
            return result;
        }
        finally
        {
            sample.CancelledAfter = token.IsCancellationRequested;
            End(sample, false);
        }
    }

    internal void CompleteFinalCall(NativeTextMaintenanceFlowPhase phase)
        => samples[(int)phase].Completed = true;

    internal void Save()
    {
        var lines = Enum.GetValues<NativeTextMaintenanceFlowPhase>().Where(phase => samples[(int)phase].Started)
            .Select(phase => Describe(phase, samples[(int)phase]));
        Directory.CreateDirectory(output);
        receipts.Save(output, BoundedDiagnosticLog.Bound(lines));
    }

    private Sample Begin(NativeTextMaintenanceFlowPhase phase)
    {
        var sample = samples[(int)phase];
        sample.Start = Clock.GetTimestamp();
        sample.Started = true;
        return sample;
    }

    private static void End(Sample sample, bool completed)
    {
        sample.End = Clock.GetTimestamp();
        sample.Ended = true;
        sample.Completed = completed;
    }

    private string Describe(NativeTextMaintenanceFlowPhase phase, Sample sample)
    {
        var start = Clock.GetElapsedTime(started, sample.Start).TotalMilliseconds.ToString(TimeFormat, CultureInfo.InvariantCulture);
        var end = sample.Ended
            ? Clock.GetElapsedTime(started, sample.End).TotalMilliseconds.ToString(TimeFormat, CultureInfo.InvariantCulture) : Unobserved;
        return $"NativeTextPhase={phase} StartElapsedMs={start} EndElapsedMs={end} Completed={sample.Completed} CancelledBefore={sample.CancelledBefore} CancelledAfter={sample.CancelledAfter}";
    }

    private sealed class Sample
    {
        internal long Start;
        internal long End;
        internal bool Started;
        internal bool Ended;
        internal bool Completed;
        internal bool CancelledBefore;
        internal bool CancelledAfter;
    }
}
