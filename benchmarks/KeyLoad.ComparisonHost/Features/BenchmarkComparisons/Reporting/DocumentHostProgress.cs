using System.Globalization;
using System.Text;
using KeyLoad.Comparisons;
using Microsoft.Extensions.Options;

namespace KeyLoad.ComparisonHost.Features.BenchmarkComparisons;

/// <summary>Emits bounded live progress from original settled operation outcomes.</summary>
internal sealed class DocumentHostProgress(IOptions<NativeComparisonExecutionOptions> execution, TimeProvider clock)
{
    private const int NoRepetition = -1;
    private const string ProgressFormat = "KeyLoadBenchmarkProgress phase=measure repetition={0} completed={1} total={2} failed={3} elapsedSeconds={4:F3}";
    private static readonly CompositeFormat ProgressTemplate = CompositeFormat.Parse(ProgressFormat);
    private readonly System.Threading.Lock gate = new();
    private long lastObserved = clock.GetTimestamp();
    private int repetition = NoRepetition;

    internal void Observe(DocumentComparisonProgress progress)
    {
        lock (gate)
        {
            var now = clock.GetTimestamp();
            var newRepetition = progress.Repetition != repetition;
            repetition = progress.Repetition;
            var completed = progress.Acknowledged + progress.Failed + progress.Canceled;
            if (!newRepetition && completed < progress.Planned && clock.GetElapsedTime(lastObserved, now) < execution.Value.ProgressHeartbeatInterval)
            { return; }
            lastObserved = now;
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, ProgressTemplate, repetition, completed,
                progress.Planned, progress.Failed + progress.Canceled, progress.ElapsedSeconds));
        }
    }
}
