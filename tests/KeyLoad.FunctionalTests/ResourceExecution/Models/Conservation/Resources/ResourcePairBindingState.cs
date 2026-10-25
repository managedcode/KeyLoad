using KeyLoad.FunctionalTests.Authorization;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.FunctionalTests.ResourceExecution;

internal enum ResourcePairBindingFault
{
    MissingLifetime, MalformedLifetime, ConflictingLifetime, MissingDefinition
}

internal sealed class ResourcePairBindingState(TestDatabase source, ResourcePairBindingFault fault)
{
    internal const string Corpus = "pair-corpus";
    internal const string CorpusSubject = "pair-corpus-subject";
    internal const string CorpusPublisher = "pair-corpus-publisher";
    internal const string CorpusDocument = "corpus-one";
    internal const string CorpusField = "/text";
    internal const string CorpusTerm = "needle";
    internal const string CorpusJson = """{"text":"needle"}""";
    internal const string CorpusDeniedDetail = "BM25 corpus maintenance requires current administrator authority.";
    internal const string Documents = "pair-documents";
    internal const string Events = "pair-events";
    internal const string Queue = "pair-jobs";
    internal const string Table = "pair-typed";
    internal const string Root = "root";
    internal const string SchedulerAdmin = "pair-scheduler-admin";
    internal const string Utc = "UTC";
    internal const string DeniedDetail = "The principal cannot perform this operation in this scope.";
    internal const string QueryUser = "pair-query-reader";
    internal const string LifetimeSpace = "resource-lifetime";
    internal const string DocumentJson = """{ "amount": 17, "raw": "\u0061" }""";
    internal const string EventJson = """{"kind":"Created","amount":17}""";
    internal const string QueueJson = """{"job":"pair","amount":17}""";
    internal const string EventType = "Created";
    internal const string Headers = "{}";
    internal const string PutKind = "putDocument";
    internal const string AppendKind = "appendEvents";
    internal const string EnqueueKind = "enqueue";
    internal const string RevisionDetail = "The expected revision does not match.";
    internal const string SubmissionFailuresKey = "KeyLoad.ResourcePair.OriginalSubmissionFailures";
    internal const string Invalid = "The genuine resource pair whole-operation state is incomplete.";
    internal const string RowSeparator = ":";
    internal const string SqlInsert = "INSERT INTO [pair-typed] (id,amount) VALUES ('typed-one',17)";
    internal const string SqlAccessPath = "bounded-full-scan";
    internal const string SqlSelect = "SELECT id,amount FROM [pair-typed]";
    internal const string TypedId = "typed-one";
    internal const string TypedJson = """{"id":"typed-one","amount":17}""";
    internal const string GuidFormat = "N";
    internal const string BatchPrefix = "mixed-";
    internal const string ColdPrefix = "cold-";
    internal const string IdColumn = "id";
    internal const string ValueColumn = "amount";
    internal const int Zero = 0;
    internal const int One = 1;
    internal const int TwoColdWaves = 2;
    internal const int PersistedCopies = 1;
    internal const long InitialRevision = 0L;
    internal const long FirstRevision = 1L;
    internal const long FirstEpoch = 1L;
    internal const long NextPolicyEpoch = 2L;
    internal const char HexZero = '0';
    internal const char HexOne = '1';
    internal static readonly string[] Models = [Documents, Events, Queue];
    internal TestDatabase Source { get; } = source;
    internal ResourcePairBindingFault Fault { get; } = fault;
    internal List<(Command<DatabaseCommand> Original, Result<object> Result, byte[] Outcome)> Originals { get; } = [];
    internal List<(string Id, long Sequence, DateTimeOffset Time)> Mixed { get; } = [];
    internal List<(string Id, string Json)> Rows { get; } = [];
    internal Dictionary<string, (byte[] Definition, byte[] Lifetime)> Pairs { get; } = new(StringComparer.Ordinal);

    internal void Remember(Command<DatabaseCommand> original, Result<object> result, ZoneTreeStore? store = null)
        => Originals.Add((original, result, RequiredProcessingPolicyAssertions.Outcome(store ?? Source.Store,
            Source.Partition, original.UserId!, original.CommandId)));
}
