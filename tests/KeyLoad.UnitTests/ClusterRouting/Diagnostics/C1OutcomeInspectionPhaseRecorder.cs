using System.Text;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class C1OutcomeInspectionPhaseRecorder
{
    private const int MaximumPhaseLineBytes = 512;
    private const int MaximumPhaseLines = 32;
    private const string PhaseBudgetMessage = "The outcome inspection phase observation exceeded its fixed bound.";
    private int phaseLineCount;
    private bool phaseBudgetReported;
    private readonly List<Exception> phaseFailures = [];

    internal IReadOnlyList<Exception> Failures => phaseFailures;

    internal void Record(string line)
    {
        var writer = Console.Error;
        if (phaseLineCount >= MaximumPhaseLines
            || Encoding.UTF8.GetByteCount(line) + Encoding.UTF8.GetByteCount(writer.NewLine) > MaximumPhaseLineBytes)
        {
            if (!phaseBudgetReported)
            {
                phaseBudgetReported = true;
                phaseFailures.Add(new InvalidOperationException(PhaseBudgetMessage));
            }
            return;
        }
        phaseLineCount++;
        ServerFailureObserver.Observe(() => writer.WriteLine(line), phaseFailures);
    }

}
