using System.Collections.Immutable;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.Comparisons;

internal sealed class DocumentRepetitionOwner(DocumentComparisonSelection selection, int repetition, int operations, int initialRecords)
{
    internal DocumentComparisonSelection Selection => selection;
    internal int Repetition => repetition;
    internal DocumentComparisonCorpus Corpus { get; } = new(initialRecords, selection.Clients);
    internal DocumentComparisonSchedule Schedule { get; } = new(selection.Scenario, new DocumentComparisonCorpus(initialRecords, selection.Clients), operations);
    internal DocumentMeasurementState State { get; } = new(operations);
    internal List<IDocumentComparisonSession> Clients { get; } = [];
    private readonly List<string> errors = [];
    private readonly List<Exception> failures = [];
    internal IComparisonTarget? Target { get; set; }
    internal TargetProfile? Profile { get; set; }
    internal DocumentReadbackEvidence? Readback { get; set; }
    internal ClientResources? Resources { get; set; }
    internal bool Unsupported { get; set; }
    internal bool SingleOperation { get; set; } = true;
    internal double SetupSeconds { get; set; }
    internal double WarmupSeconds { get; set; }
    internal double MeasuredSeconds { get; set; }
    internal double VerificationSeconds { get; set; }
    internal double CleanupSeconds { get; set; }
    internal void Observe(Exception error) { failures.Add(error); errors.Add(ComparisonErrors.Safe(error)); }
    internal void Observe(string error) => errors.Add(error);
    internal DocumentComparisonRepetition Complete()
    {
        var histogram = State.Latency.Snapshot();
        if (!Unsupported && !Corpus.InitializationTiming.Complete)
        {
            errors.Add(DocumentProtocolText.DocumentPhaseInstrumentationIncomplete);
        }

        if (!SingleOperation)
        {
            errors.Add(DocumentProtocolText.NativeOperationIncludesVerificationRead);
        }

        if (histogram.OverflowCount != DocumentMeasurementValues.NoObservedItems)
        {
            errors.Add(DocumentProtocolText.DocumentLatencyHistogramOverflow);
        }

        errors.AddRange(State.FailureCategories());
        var fatal = State.Fatal ?? failures.Select(CqrsRuntimeFailures.FindFatal).FirstOrDefault(error => error is not null);
        if (fatal is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(fatal).Throw();
        }

        var qualified = !Unsupported && Corpus.InitializationTiming.Complete && errors.Count == DocumentMeasurementValues.NoObservedItems && State.Acknowledged == operations && State.Failed == DocumentMeasurementValues.NoObservedItems
            && State.Canceled == DocumentMeasurementValues.NoObservedItems && State.Attempts == operations && Readback?.Records == Schedule.Final
            && Clients.Count == selection.Clients && histogram.Count == State.Attempts;
        return new(repetition, Profile, Unsupported ? DocumentProtocolText.Unsupported : qualified ? DocumentProtocolText.Measured : DocumentProtocolText.Failed, qualified,
            errors.Distinct().ToImmutableArray(), Schedule.Initial, Schedule.Added, Schedule.Deleted, Schedule.Final,
            Readback?.Records ?? DocumentMeasurementValues.NoObservedItems, Readback?.ExpectedSha256, Readback?.ActualSha256, selection.Clients, Clients.Count,
            State.Peak, operations, State.Attempts, State.Acknowledged, State.Failed, State.Canceled,
            State.Active, SingleOperation, new(SetupSeconds, WarmupSeconds, MeasuredSeconds, VerificationSeconds, CleanupSeconds, Corpus.InitializationTiming.LoadSeconds,
                Corpus.InitializationTiming.IndexBuildSeconds, Corpus.InitializationTiming.IndexBuildApplicable, Corpus.InitializationTiming.Complete),
            histogram, Resources);
    }
}
