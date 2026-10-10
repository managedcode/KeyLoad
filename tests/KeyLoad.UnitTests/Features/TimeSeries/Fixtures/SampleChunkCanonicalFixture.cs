using System.Collections.Immutable;

namespace KeyLoad.UnitTests.Features.TimeSeries;

internal sealed class SampleChunkCanonicalFixture : IDisposable
{
    internal const string Set = SampleAggregateTestData.Set;
    internal const string Series = SampleAggregateTestData.Series;
    internal const string Principal = SampleAggregateTestData.RootPrincipal;
    internal const long FirstRevision = 1;
    internal const long AppendedRevision = 3;
    internal const long SealedRevision = 4;
    internal const long CorrectedRevision = 5;
    internal const long MergedRevision = 6;
    internal const long MergedGeneration = 2;
    internal const long InitialSequence = 2;
    internal const long CorrectedSequence = 3;
    internal const int FirstIndex = 0;
    internal const int SecondIndex = 1;
    private const int WindowHours = 1;
    private const int AlternateOffsetHours = -5;
    private const double FirstValue = 2;
    private const double SecondValue = 4;
    private const double NegativeZero = -0d;
    private const string FirstId = "first";
    private const string SecondId = "equal-offset";
    private const string LateId = "late";
    internal const long FirstGeneration = 1;
    internal const long NoGeneration = 0;
    internal const int OutputLimit = 32;
    internal const string Tags = "{\"visible\":17}";
    internal static readonly DateTimeOffset Start = TimeProvider.System.GetUtcNow();
    internal static readonly DateTimeOffset Until = Start.AddHours(WindowHours);
    internal static readonly ImmutableArray<SampleData> Initial =
    [new(FirstId, Start, FirstValue), new(SecondId, Start.ToOffset(TimeSpan.FromHours(AlternateOffsetHours)), SecondValue)];
    internal static readonly SampleData Late = new(LateId, Start.AddTicks(FirstRevision), NegativeZero);
    internal TestDatabase Owner { get; }
    internal Guid WindowId { get; } = Guid.NewGuid();

    internal SampleChunkCanonicalFixture(KeyLoad.Core.TimeSeriesExecutionOptions? execution = null,
        TimeProvider? timeProvider = null, bool nativeReplicaAdmission = false)
    {
        Owner = new(timeSeriesExecution: execution, timeProvider: timeProvider,
            nativeReplicaAdmission: nativeReplicaAdmission);
        try
        { SampleAggregateTestData.Configure(Owner); }
        catch (Exception original)
        {
            try
            { Owner.Dispose(); }
            catch (Exception cleanup) { throw new AggregateException(original, cleanup); }
            throw;
        }
    }

    internal OperationResult Commit(Guid id, Mutation mutation)
        => Owner.Submit(OperationKind.Batch, new CommandRequest(id, Owner.Partition, [mutation]), Principal, id);
    internal CommitReceipt Commit(Mutation mutation) => Commit(Guid.NewGuid(), mutation).Get<CommitReceipt>();
    internal void Open() => Commit(new OpenSampleChunkWindow(Set, Series, WindowId, Start, Until));
    internal void AppendInitial() => Commit(new AppendSamples(Set, Series, Initial, Tags));
    internal ReadSampleChunkWindowRequest Request => new(Owner.Partition, Set, Series, WindowId, null, null, Limit: OutputLimit);
    internal SampleChunkWindowResult Read(CancellationToken token = default)
        => Owner.Database.ReadSampleChunkWindow(Principal, Request, token);
    internal string Image() => SampleRollupWholeFlow.Image(Owner);
    internal string Raw() => SampleRollupWholeFlow.Raw(Owner);
    internal static SampleRecord[] Expected(bool late = false) => late
        ? [new(Series, Initial[FirstIndex], FirstRevision, Tags), new(Series, Initial[SecondIndex], InitialSequence, Tags), new(Series, Late, CorrectedSequence, Tags)]
        : [new(Series, Initial[FirstIndex], FirstRevision, Tags), new(Series, Initial[SecondIndex], InitialSequence, Tags)];
    public void Dispose() => Owner.Dispose();
}
