namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal enum ResourcePairBindingFault
{
    MissingLifetime, MalformedLifetime, ConflictingLifetime, MissingDefinition
}

internal static class ResourcePairBindingProtocol
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
    internal const string Blobs = "pair-blobs";
    internal const string Table = "pair-typed";
    internal const string Root = "root";
    internal const string Denied = "pair-denied";
    internal const string SchedulerAdmin = "pair-scheduler-admin";
    internal const string Utc = "UTC";
    internal const string DeniedDetail = "The principal cannot perform this operation in this scope.";
    internal const string QueryUser = "pair-query-reader";
    internal const string Wildcard = "*";
    internal const string LifetimeSpace = "resource-lifetime-v1";
    internal const string DocumentJson = """{ "amount": 17, "raw": "\u0061" }""";
    internal const string EventJson = """{"kind":"Created","amount":17}""";
    internal const string StoredEventJson = """{"amount":17,"kind":"Created"}""";
    internal const string QueueJson = """{"job":"pair","amount":17}""";
    internal const string StoredQueueJson = """{"amount":17,"job":"pair"}""";
    internal const string EventType = "Created";
    internal const string Headers = "{}";
    internal const string PutKind = "putDocument";
    internal const string AppendKind = "appendEvents";
    internal const string EnqueueKind = "enqueue";
    internal const string RevisionDetail = "The expected revision does not match.";
    internal const string RootKey = "KeyLoad.ResourcePair.OriginalRoot";
    internal const string ReplicaRootKey = "KeyLoad.ResourcePair.OriginalReplicaRoot";
    internal const string SubmissionFailuresKey = "KeyLoad.ResourcePair.OriginalSubmissionFailures";
    internal const string OwnerKey = "KeyLoad.ResourcePair.OriginalOwner";
    internal const string LifetimeKey = "KeyLoad.ResourcePair.OwnerLifetime";
    internal const string Invalid = "The genuine resource pair whole-operation state is incomplete.";
    internal const string ColdStoreKey = "KeyLoad.ResourcePair.ColdStore";
    internal const string ColdAdmissionKey = "KeyLoad.ResourcePair.ColdAdmission";
    internal const string ColdLifetimeKey = "KeyLoad.ResourcePair.ColdLifetime";
    internal const string DeniedPrefix = "pair-denied-";
    internal const string RowSeparator = ":";
    internal const string BlobId = "pair-object";
    internal const string ChainDomain = "blob-chain-v1";
    internal const string SqlInsert = "INSERT INTO [pair-typed] (id,amount) VALUES ('typed-one',17)";
    internal const string SqlAccessPath = "bounded-full-scan";
    internal const string SqlSelect = "SELECT id,amount FROM [pair-typed]";
    internal const string TypedId = "typed-one";
    internal const string TypedJson = """{"id":"typed-one","amount":17}""";
    internal const string DirectoryPrefix = "keyload-resource-pair-";
    internal const string GuidFormat = "N";
    internal const string BatchPrefix = "mixed-";
    internal const string ColdPrefix = "cold-";
    internal const string IdColumn = "id";
    internal const string ValueColumn = "amount";
    private const byte FirstPayloadByte = 0x31;
    private const byte SecondPayloadByte = 0x32;
    private const byte ThirdPayloadByte = 0x33;
    private const byte LastPayloadByte = 0x34;
    internal const int Zero = 0;
    internal const int One = 1;
    internal const int TwoColdWaves = 2;
    internal const int Amount = 17;
    internal const int BlobBytes = 4;
    internal const long InitialRevision = 0L;
    internal const long FirstRevision = 1L;
    internal const long FirstEpoch = 1L;
    internal const long NextPolicyEpoch = 2L;
    internal const char HexZero = '0';
    internal const char HexOne = '1';
    internal static readonly byte[] Payload = [FirstPayloadByte, SecondPayloadByte, ThirdPayloadByte, LastPayloadByte];
    internal static readonly string[] Models = [Documents, Events, Queue];
}
