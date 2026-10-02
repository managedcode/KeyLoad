using MongoDB.Bson;
using MongoDB.Driver;

namespace KeyLoad.Comparisons.Targets;

internal static class MongoSchema
{
    public const string DatabasePrefix = "benchmark_";
    public const string DatabaseSeparator = ":";
    public const string DocumentsCollection = "documents";
    public const string EdgesCollection = "edges";
    public const string EventsCollection = "events";
    public const string IdField = "_id";
    public const string BodyField = "body";
    public const string FromField = "from";
    public const string ToField = "to";
    public const string EventIdField = "eventId";
    public const string RevisionField = "revision";
    public const string JsonField = "json";
    public const string GraphOutputField = "reachableEdges";
    public const string TargetName = "MongoDB";
    public const string ProfileVersion = "unverified";
    public const string SingleTopology = "one server; no replica copies";
    public const string ReplicatedTopology = "one primary plus two data-bearing secondaries";
    public const string WriteAcknowledgement = "one-event-per-stream unique-document emulation; w=majority, journal=true; not full event-store equivalence";
    public const string ReadContract = "primary reads with majority read concern; event identity, revision and payload returned for outer untimed oracle validation";
    public const string NetworkTls = "MongoDB transport settings from connection string";
    public const string Authenticated = "connection-string credentials configured";
    public const string Unauthenticated = "no MongoDB credentials configured";
    public const string CollectionProbePrefix = "copy_probe_";
    public const string ReplicaSetStatusCommand = "replSetGetStatus";
    public const string AdminDatabase = "admin";
    public const string HelloCommand = "hello";
    public const string WritablePrimaryField = "isWritablePrimary";
    public const string ReplicaSetNameField = "setName";
    public const string RouterMessageField = "msg";
    public const string RouterMessage = "isdbgrid";
    public const string BuildInfoCommand = "buildInfo";
    public const string DefaultReadWriteConcernCommand = "getDefaultRWConcern";
    public const string MembersField = "members";
    public const string MemberHostField = "name";
    public const string MemberHealthField = "health";
    public const string MemberStateField = "stateStr";
    public const string MemberArbiterField = "arbiterOnly";
    public const string PrimaryState = "PRIMARY";
    public const string SecondaryState = "SECONDARY";
    public const string VersionField = "version";
    public const string DefaultWriteConcernField = "defaultWriteConcern";
    public const string DefaultWriteConcernModeField = "w";
    public const string MajorityMode = "majority";
    public const string FailureUnsupportedTopology = "MongoTopologyMustBeSingleOrThreeNodeReplicaSet";
    public const string FailureReplicaSetShape = "MongoReplicaSetMustHaveOnePrimaryAndTwoDataSecondaries";
    public const string FailureReplicaMemberVersion = "MongoReplicaMemberVersionMismatch";
    public const string FailureDataCopyMissing = "MongoSecondaryDataCopyProbeFailed";
    public const string FailureDuplicateStreamAccepted = "MongoStreamUniqueInsertAcceptedDuplicate";
    public const string FailureDuplicateStreamUnexpected = "MongoStreamDuplicateHadUnexpectedFailure";
    public const string FailureReadCardinality = "MongoStreamReadCardinalityMismatch";
    public const string FailureEventMismatch = "MongoStreamEventMismatch";
    public const string FailureUnsupportedScenario = "MongoUnsupportedScenario";
    public const string FailureNotInitialized = "MongoTargetNotInitialized";
    public const string FailureVersionUnavailable = "MongoServerVersionUnavailable";
    public const string FailureDefaultConcernUnavailable = "MongoDefaultWriteConcernUnavailable";
    public const string FailureCopyProbeTimeout = "MongoSecondaryCopyProbeTimeout";
    public const string FailureDuplicateKey = "MongoExpectedDuplicateKey";
    public const string FailureMemberAddress = "MongoReplicaMemberAddressInvalid";
    public const string ConnectionPrefix = "mongodb://";
    public const string InvariantFormat = "N";
    public const string HealthyState = "healthy";
    public const string SingleState = "single";
    public const string SingleObservation = "native hello writable primary; standalone or one-member replica status observed";
    public const string GuidFormat = "N";
    public const string EventIdFormat = "D";
    public const string ConcernSeparator = ", journal=";
    public const string ObservationMembers = "members=";
    public const string ObservationRoles = "roles=";
    public const string ObservationVersions = "versions=";
    public const string ObservationWriteConcern = "clientWriteConcern=";
    public const string ObservationReadConcern = "clientReadConcern=";
    public const string ObservationReadPreference = "clientReadPreference=";
    public const string ProbeJsonPrefix = "{\"probe\":\"";
    public const string ProbeJsonSuffix = "\"}";
    public const string CredentialConfigured = "connection-string credentials configured";
    public const string CredentialAbsent = "no MongoDB credentials configured";
    public const string TlsConfigured = "MongoDB TLS configured";
    public const string TlsAbsent = "MongoDB TLS disabled by connection string";
    public const string TlsVerified = "MongoDB TLS with certificate verification";
    public const string TlsUnverified = "MongoDB TLS with certificate verification disabled";
    public const string AuthenticatedNoClientCertificate = "MongoDB database credentials; no TLS client certificate";
    public const string UnauthenticatedNoClientCertificate = "no MongoDB credentials or TLS client certificate";
    public const string DuplicateProbePrefix = "unique_stream_probe_";
    public const string GraphLookupOperator = "$graphLookup";
    public const string GraphUnwindOperator = "$unwind";
    public const string GraphMatchOperator = "$match";
    public const string GraphGroupOperator = "$group";
    public const string GraphSortOperator = "$sort";
    public const string GraphStartField = "startWith";
    public const string GraphFromCollection = "from";
    public const string GraphConnectFromField = "connectFromField";
    public const string GraphConnectToField = "connectToField";
    public const string GraphMaxDepthField = "maxDepth";
    public const string GraphAsField = "as";
    public const string GraphEdgeDestination = "$reachableEdges.to";
    public const string GraphEdgeDestinationPath = "reachableEdges.to";
    public const string GraphEdgePrefix = "$reachableEdges";
    public const string GraphGroupId = "_id";
    public const string GraphNotEqualOperator = "$ne";
    public const string GraphIdPath = "$";
    public const string GraphAscending = "1";
    public const int GraphBaseDepth = 1;
    public const int GraphGroupDirection = 1;
    public const int GraphSortDirection = 1;
    public const int DuplicateProbeCount = 2;
    public const int SingleEventCount = 1;
    public const int InitialEventRevision = 1;
    public const int CommandEnabledValue = 1;
    public const int ConsistentVersionCount = 1;
    public const int SingleNodeCount = 1;
    public const int ReplicaNodeCount = 3;
    public const int PrimaryNodeCount = 1;
    public const int SecondaryNodeCount = 2;
    public const int HealthyMemberValue = 1;
    public const int CleanupTimeoutSeconds = 15;
    public const int ProbePollMilliseconds = 100;

    public static WriteConcern MajorityJournalWriteConcern => WriteConcern.WMajority.With(journal: true);

    public static BsonDocument Document(BenchmarkDocument document)
        => new() { [IdField] = document.Id, [BodyField] = document.Json };

    public static BsonDocument Event(string streamId, Guid eventId, string json)
        => new()
        {
            [IdField] = streamId,
            [EventIdField] = eventId.ToString(EventIdFormat),
            [RevisionField] = InitialEventRevision,
            [JsonField] = json
        };
}
