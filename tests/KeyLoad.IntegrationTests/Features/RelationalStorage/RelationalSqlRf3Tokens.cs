using System.Collections.Immutable;

namespace KeyLoad.IntegrationTests.Features.RelationalStorage;

/// <summary>Independent typed-row and cross-model data for real RF3 SQL callers.</summary>
internal static class RelationalSqlRf3Tokens
{
    internal const string TenantPrefix = "aisql-rf3-";
    internal const string Database = "agentdb";
    internal const string Domain = "agent";
    internal const string Table = "agentrows";
    internal const string Documents = "documents";
    internal const string Streams = "events";
    internal const string Queue = "jobs";
    internal const string Graph = "links";
    internal const string SeriesSet = "samples";
    internal const string Blobs = "files";
    internal const string FirstId = "first";
    internal const string SecondId = "second";
    internal const string InvalidId = "invalid";
    internal const string Key = "key";
    internal const string Title = "title";
    internal const string FirstTitle = "alpha";
    internal const string Count = "count";
    internal const string TitlePath = "/title";
    internal const string CountPath = "/count";
    internal const string TitleIndex = "by-title";
    internal const string FirstRow = "{\"key\":\"first\",\"title\":\"alpha\",\"count\":1}";
    internal const string SecondRow = "{\"key\":\"second\",\"title\":\"beta\",\"count\":2}";
    internal const string DuplicateTitleRow = "{\"key\":\"second\",\"title\":\"alpha\",\"count\":2}";
    internal const string InvalidTypeRow = "{\"key\":\"invalid\",\"title\":\"invalid\",\"count\":\"wrong\"}";
    internal const string PatchValue = "3";
    internal const string InvalidPatchValue = "\"wrong\"";
    internal const string EmptyJson = "{}";
    internal const string DocumentId = "document";
    internal const string EventId = "event";
    internal const string EventType = "created";
    internal const string StreamId = "stream";
    internal const string MessageId = "message";
    internal const string EdgeId = "edge";
    internal const string EdgeLabel = "related";
    internal const string VectorField = "/embedding";
    internal const string VectorSpace = "agent-vector";
    internal const string VectorModel = "model";
    internal const string VectorVersion = "1";
    internal const string SeriesId = "series";
    internal const string SampleId = "sample";
    internal const string BlobId = "absent";
    internal const string RolledBackId = "rolled-back";
    internal const int FirstRevision = 1;
    internal const int PatchedRevision = 2;
    internal const int PatchedCount = 3;
    internal const int Dimension = 2;
    internal const int OneResult = 1;
    internal const int NoResults = 0;
    internal const int RootAndNeighbor = 2;
    internal const int FirstEventRevision = 1;
    internal const long InitialEventRevision = 0;
    internal const float UnitVector = 1;
    internal const float ZeroVector = 0;
    internal const double SampleValue = 5;
    internal static readonly DateTimeOffset SampleAt = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
    internal static ImmutableArray<RelationalColumn> Columns =>
    [new(Key, RelationalColumnType.Text), new(Title, RelationalColumnType.Text), new(Count, RelationalColumnType.Int64)];
}
