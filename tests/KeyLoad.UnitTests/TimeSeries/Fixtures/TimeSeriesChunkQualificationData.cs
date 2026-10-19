using System.Collections.Immutable;

namespace KeyLoad.UnitTests.Features.TimeSeries;

internal static class TimeSeriesChunkQualificationData
{
    private const int FirstSampleIndex = 0;
    private const int EqualSampleIndex = 1;
    internal const int PrimaryScope = 0;
    internal const int SiblingScope = 1;
    internal const int PartitionCount = 2;
    internal const int InitialPhase = 0;
    internal const int PartialPhase = 1;
    internal const int ExpiredPhase = 2;
    internal const int DroppedPhase = 3;
    internal const long InitialPolicyEpoch = 1L;
    internal const long WithdrawnPolicyEpoch = 2L;
    internal const long RepairedPolicyEpoch = 3L;
    internal const int PlacementVersion = 1;
    internal const int OneRetentionDelete = 1;
    internal const long NextJournalIndex = 1L;
    internal const long InitialOwnerEpoch = 1L;
    internal const int ColdCycles = 2;
    internal const string PrivatePath = "/private";
    internal const string RestrictedClass = "restricted";
    internal const string RootPrincipal = "root";
    internal const string GuidFormat = "N";
    internal const string DatabaseName = "database";
    internal const string DomainName = "domain";
    internal const string TenantPrefix = "chunk-qualification-";
    internal const string ScopeDenied = "The principal cannot perform this operation in this scope.";
    internal const string FieldUseDenied = "The query requires a protected field-use grant.";
    internal const string SafePermissionDenied = "The authenticated principal is not permitted to perform this operation.";
    internal const string SafeUnavailable = "The database operation could not be completed.";
    internal const string ReadUnavailable = "The read response is unavailable.";

    internal const string Set = "chunk_scope_qualification";
    internal const string Series = "shared-series";
    internal const string Reader = "chunk-qualification-reader";
    internal const string ReadGrant = "chunk.private.read";
    internal const string UseGrant = "chunk.private.use";
    internal const string Missing = "The execution series chunk window is unavailable.";
    internal const long MergedRevision = 6L;
    internal const long DroppedRevision = 7L;
    internal const long SourceSequence = 3L;
    internal const int RefusedReadLimit = 2;
    internal const string ReadBoundExceeded = "The execution series chunk window exceeds its admitted bound.";
    internal const string SafeBoundExceeded = "The operation exceeds the available resource capacity.";
    internal const int ReadLimit = 3;
    private const int BaseTicks = 10;
    private const int PrimaryOffsetMinutes = 345;
    private const int SiblingOffsetMinutes = -210;
    private const int LateOffsetHours = 14;
    internal static readonly DateTimeOffset From = new(2001, 1, 1, 0, 0, 0, TimeSpan.Zero);
    internal static readonly DateTimeOffset Base = From.AddTicks(BaseTicks);
    internal static readonly TimeSpan WindowWidth = TimeSpan.FromHours(1);
    internal const int DenseWindowCount = 1;
    internal static readonly DateTimeOffset Until = From.Add(WindowWidth);
    internal static string Tags(bool sibling, bool projected = false)
        => projected ? sibling ? """{"scope":"b"}""" : """{"scope":"a"}"""
            : sibling ? """{"private":"beta","scope":"b"}""" : """{"private":"alpha","scope":"a"}""";
    internal static ImmutableArray<SampleData> Initial(bool sibling)
        => [new("equal-first", Base, sibling ? -8d : 2d),
            new("equal-offset", Base.ToOffset(TimeSpan.FromMinutes(sibling ? SiblingOffsetMinutes : PrimaryOffsetMinutes)), sibling ? -4d : 4d)];
    internal static SampleData Late(bool sibling) => new("late", From.AddTicks(1).ToOffset(
        TimeSpan.FromHours(sibling ? -LateOffsetHours : LateOffsetHours)), sibling ? 0d : -0d);
    internal static CommandRequest Seed(PartitionRef partition, Guid window, bool sibling)
    {
        var pair = Initial(sibling);
        return new(Guid.NewGuid(), partition,
        [
            new OpenSampleChunkWindow(Set, Series, window, From, Until),
            new AppendSamples(Set, Series, [pair[FirstSampleIndex]], Tags(sibling)),
            new AppendSamples(Set, Series, [pair[EqualSampleIndex]], Tags(sibling)),
            new AppendSamples(Set, Series, [Late(sibling)], Tags(sibling)),
            new MergeSampleChunkWindow(Set, Series, window, 5L)
        ]);
    }
    internal static ImmutableArray<MutationReceipt> SeedReceipts =>
    [new("openSampleChunkWindow", Set, Series, 1L), new("appendSamples", Set, Series, 1L),
     new("appendSamples", Set, Series, 2L), new("appendSamples", Set, Series, 3L),
     new("mergeSampleChunkWindow", Set, Series, MergedRevision)];
    internal static SampleRecord[] Rows(bool sibling, int phase, bool projected)
    {
        if (!sibling && phase >= ExpiredPhase)
        {
            return [];
        }
        var pair = Initial(sibling);
        var first = new SampleRecord(Series, pair[FirstSampleIndex], 1L, Tags(sibling, projected));
        var second = new SampleRecord(Series, pair[EqualSampleIndex], 2L, Tags(sibling, projected));
        return !sibling && phase == PartialPhase ? [first, second]
            : [new(Series, Late(sibling), SourceSequence, Tags(sibling, projected)), first, second];
    }
    internal static SampleAggregate Aggregate(bool sibling, int phase)
        => sibling ? new(3L, -12d, -8d, 0d, -4d) : phase switch
        {
            PartialPhase => new(2L, 6d, 2d, 4d, 3d),
            ExpiredPhase or DroppedPhase => new(0L, 0d, null, null, null),
            _ => new(3L, 6d, -0d, 4d, 2d)
        };
    internal static SampleRetentionStatus Retention(bool sibling, int phase, long purged, bool more)
        => sibling || phase == InitialPhase ? new(null, 0L, false) : new(phase == PartialPhase ? Base : Until, purged, more);
    internal static ReadSampleChunkWindowRequest Request(PartitionRef partition, Guid window)
        => new(partition, Set, Series, window, null, null, ReadLimit);
    internal static SampleChunkWindowResult Chunk(Guid window, bool sibling, int phase, bool projected, long cut)
        => new(window, From, Until, 2L, MergedRevision, SourceSequence,
            sibling || phase == InitialPhase ? null : phase == PartialPhase ? Base.UtcTicks : Until.UtcTicks, [.. Rows(sibling, phase, projected)], cut);
}
