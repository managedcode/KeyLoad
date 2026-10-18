namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed partial record NativeComparisonHarnessOptions
{
    private const int MaximumNativeDatabaseFlowTimeoutMinutes = 5;
    private const int MaximumFixtureDisposalTimeoutSeconds = 30;
    private const int MaximumSubscriberCleanupTimeoutSeconds = 30;
    private const int MaximumNeo4jRequestTimeoutSeconds = 30;
    private const int MaximumNeo4jMutationTimeoutSeconds = 15;
    private const int MaximumNeo4jFailureCleanupTimeoutSeconds = 15;
    private const int MaximumOpenSearchRequestTimeoutSeconds = 30;
    private const int MaximumOpenSearchIndexCleanupTimeoutSeconds = 10;
    private const int MaximumMongoAuthenticationTimeoutSeconds = 2;
    private const int MaximumMongoReadinessTimeoutSeconds = 300;
    private const int MaximumMongoReadinessCleanupTimeoutSeconds = 30;
    private const int MaximumGitHubNativeProcessTimeoutMinutes = 120;
    private const int MaximumGitHubNativeProcessCleanupTimeoutSeconds = 10;
    private const int MaximumKeyLoadPublicChecksTimeoutMinutes = 5;
    private const int MaximumKeyLoadMcpCleanupTimeoutSeconds = 30;
    private const int MaximumKeyLoadFaultChecksTimeoutMinutes = 10;
    private const int MaximumKeyLoadFaultCliTimeoutSeconds = 30;
    private const int MaximumKeyLoadFaultRestartTimeoutSeconds = 120;
    private const int MaximumKeyLoadFaultProcessDrainTimeoutSeconds = 5;
    private const int MaximumPostgresSlotObservationTimeoutSeconds = 60;
    private const int MaximumComparisonDataCleanupTimeoutSeconds = 30;
    private const int MaximumProgressWriteTimeoutSeconds = 5;
    private const int MaximumMongoAuthenticationMaximumPoolSize = 4;
    private const int MaximumProgressFileBufferBytes = 512;
    private const int MaximumNativeToolReadBufferCharacters = 4096;
    private const int MaximumNativeToolMaximumOutputCharacters = 65536;
    private const int MaximumKeyLoadFaultMaximumEvidenceBytes = 262144;

    public TimeSpan NativeDatabaseFlowTimeout { get; init; } = TimeSpan.FromMinutes(MaximumNativeDatabaseFlowTimeoutMinutes);
    public TimeSpan FixtureDisposalTimeout { get; init; } = TimeSpan.FromSeconds(MaximumFixtureDisposalTimeoutSeconds);
    public TimeSpan SubscriberCleanupTimeout { get; init; } = TimeSpan.FromSeconds(MaximumSubscriberCleanupTimeoutSeconds);
    public TimeSpan Neo4jRequestTimeout { get; init; } = TimeSpan.FromSeconds(MaximumNeo4jRequestTimeoutSeconds);
    public TimeSpan Neo4jMutationTimeout { get; init; } = TimeSpan.FromSeconds(MaximumNeo4jMutationTimeoutSeconds);
    public TimeSpan Neo4jFailureCleanupTimeout { get; init; } = TimeSpan.FromSeconds(MaximumNeo4jFailureCleanupTimeoutSeconds);
    public TimeSpan OpenSearchRequestTimeout { get; init; } = TimeSpan.FromSeconds(MaximumOpenSearchRequestTimeoutSeconds);
    public TimeSpan OpenSearchIndexCleanupTimeout { get; init; } = TimeSpan.FromSeconds(MaximumOpenSearchIndexCleanupTimeoutSeconds);
    public TimeSpan MongoAuthenticationTimeout { get; init; } = TimeSpan.FromSeconds(MaximumMongoAuthenticationTimeoutSeconds);
    public TimeSpan MongoReadinessTimeout { get; init; } = TimeSpan.FromSeconds(MaximumMongoReadinessTimeoutSeconds);
    public TimeSpan MongoReadinessCleanupTimeout { get; init; } = TimeSpan.FromSeconds(MaximumMongoReadinessCleanupTimeoutSeconds);
    public TimeSpan GitHubNativeProcessTimeout { get; init; } = TimeSpan.FromMinutes(MaximumGitHubNativeProcessTimeoutMinutes);
    public TimeSpan GitHubNativeProcessCleanupTimeout { get; init; } = TimeSpan.FromSeconds(MaximumGitHubNativeProcessCleanupTimeoutSeconds);
    public TimeSpan KeyLoadPublicChecksTimeout { get; init; } = TimeSpan.FromMinutes(MaximumKeyLoadPublicChecksTimeoutMinutes);
    public TimeSpan KeyLoadMcpCleanupTimeout { get; init; } = TimeSpan.FromSeconds(MaximumKeyLoadMcpCleanupTimeoutSeconds);
    public TimeSpan KeyLoadFaultChecksTimeout { get; init; } = TimeSpan.FromMinutes(MaximumKeyLoadFaultChecksTimeoutMinutes);
    public TimeSpan KeyLoadFaultCliTimeout { get; init; } = TimeSpan.FromSeconds(MaximumKeyLoadFaultCliTimeoutSeconds);
    public TimeSpan KeyLoadFaultRestartTimeout { get; init; } = TimeSpan.FromSeconds(MaximumKeyLoadFaultRestartTimeoutSeconds);
    public TimeSpan KeyLoadFaultProcessDrainTimeout { get; init; } = TimeSpan.FromSeconds(MaximumKeyLoadFaultProcessDrainTimeoutSeconds);
    public TimeSpan PostgresSlotObservationTimeout { get; init; } = TimeSpan.FromSeconds(MaximumPostgresSlotObservationTimeoutSeconds);
    public TimeSpan ComparisonDataCleanupTimeout { get; init; } = TimeSpan.FromSeconds(MaximumComparisonDataCleanupTimeoutSeconds);
    public TimeSpan ProgressWriteTimeout { get; init; } = TimeSpan.FromSeconds(MaximumProgressWriteTimeoutSeconds);
    public int MongoAuthenticationMaximumPoolSize { get; init; } = MaximumMongoAuthenticationMaximumPoolSize;
    public int ProgressFileBufferBytes { get; init; } = MaximumProgressFileBufferBytes;
    public int NativeToolReadBufferCharacters { get; init; } = MaximumNativeToolReadBufferCharacters;
    public int NativeToolMaximumOutputCharacters { get; init; } = MaximumNativeToolMaximumOutputCharacters;
    public int KeyLoadFaultMaximumEvidenceBytes { get; init; } = MaximumKeyLoadFaultMaximumEvidenceBytes;

    private const int MaximumKeyLoadFaultExitedTimeoutSeconds = 20;
    public TimeSpan KeyLoadFaultExitedTimeout { get; init; } = TimeSpan.FromSeconds(MaximumKeyLoadFaultExitedTimeoutSeconds);

    private const int MaximumKeyLoadFaultAttemptTimeoutSeconds = 30;
    public TimeSpan KeyLoadFaultAttemptTimeout { get; init; } = TimeSpan.FromSeconds(MaximumKeyLoadFaultAttemptTimeoutSeconds);

    private const int MaximumKeyLoadFaultReadinessTimeoutSeconds = 90;
    public TimeSpan KeyLoadFaultReadinessTimeout { get; init; } = TimeSpan.FromSeconds(MaximumKeyLoadFaultReadinessTimeoutSeconds);

    private const int MaximumKeyLoadFaultPollIntervalMilliseconds = 250;
    public TimeSpan KeyLoadFaultPollInterval { get; init; } = TimeSpan.FromMilliseconds(MaximumKeyLoadFaultPollIntervalMilliseconds);

    private const int MaximumKeyLoadFaultOutputCharacters = 16384;
    public int KeyLoadFaultOutputCharacters { get; init; } = MaximumKeyLoadFaultOutputCharacters;

    private const int MaximumKeyLoadFaultErrorCharacters = 2048;
    public int KeyLoadFaultErrorCharacters { get; init; } = MaximumKeyLoadFaultErrorCharacters;

    private const int MaximumMongoReadinessOutputCharacters = 32768;
    public int MongoReadinessOutputCharacters { get; init; } = MaximumMongoReadinessOutputCharacters;

    private const int MaximumMongoReadinessErrorCharacters = 4096;
    public int MongoReadinessErrorCharacters { get; init; } = MaximumMongoReadinessErrorCharacters;

    private const int CeilingMaximumResourceDiagnosticRecords = 128;
    public int MaximumResourceDiagnosticRecords { get; init; } = CeilingMaximumResourceDiagnosticRecords;

    private bool IsAuxiliaryValid()
        => NativeDatabaseFlowTimeout > TimeSpan.Zero && NativeDatabaseFlowTimeout.Ticks <= MaximumNativeDatabaseFlowTimeoutMinutes * TimeSpan.TicksPerMinute
            && FixtureDisposalTimeout > TimeSpan.Zero && FixtureDisposalTimeout.Ticks <= MaximumFixtureDisposalTimeoutSeconds * TimeSpan.TicksPerSecond
            && SubscriberCleanupTimeout > TimeSpan.Zero && SubscriberCleanupTimeout.Ticks <= MaximumSubscriberCleanupTimeoutSeconds * TimeSpan.TicksPerSecond
            && Neo4jRequestTimeout.Ticks % TimeSpan.TicksPerSecond == 0 && Neo4jRequestTimeout > TimeSpan.Zero && Neo4jRequestTimeout.Ticks <= MaximumNeo4jRequestTimeoutSeconds * TimeSpan.TicksPerSecond
            && Neo4jMutationTimeout.Ticks % TimeSpan.TicksPerSecond == 0 && Neo4jMutationTimeout > TimeSpan.Zero && Neo4jMutationTimeout.Ticks <= MaximumNeo4jMutationTimeoutSeconds * TimeSpan.TicksPerSecond
            && Neo4jFailureCleanupTimeout > TimeSpan.Zero && Neo4jFailureCleanupTimeout.Ticks <= MaximumNeo4jFailureCleanupTimeoutSeconds * TimeSpan.TicksPerSecond
            && OpenSearchRequestTimeout > TimeSpan.Zero && OpenSearchRequestTimeout.Ticks <= MaximumOpenSearchRequestTimeoutSeconds * TimeSpan.TicksPerSecond
            && OpenSearchIndexCleanupTimeout > TimeSpan.Zero && OpenSearchIndexCleanupTimeout.Ticks <= MaximumOpenSearchIndexCleanupTimeoutSeconds * TimeSpan.TicksPerSecond
            && MongoAuthenticationTimeout > TimeSpan.Zero && MongoAuthenticationTimeout.Ticks <= MaximumMongoAuthenticationTimeoutSeconds * TimeSpan.TicksPerSecond
            && MongoReadinessTimeout > TimeSpan.Zero && MongoReadinessTimeout.Ticks <= MaximumMongoReadinessTimeoutSeconds * TimeSpan.TicksPerSecond
            && MongoReadinessCleanupTimeout > TimeSpan.Zero && MongoReadinessCleanupTimeout.Ticks <= MaximumMongoReadinessCleanupTimeoutSeconds * TimeSpan.TicksPerSecond
            && GitHubNativeProcessTimeout > TimeSpan.Zero && GitHubNativeProcessTimeout.Ticks <= MaximumGitHubNativeProcessTimeoutMinutes * TimeSpan.TicksPerMinute
            && GitHubNativeProcessCleanupTimeout > TimeSpan.Zero && GitHubNativeProcessCleanupTimeout.Ticks <= MaximumGitHubNativeProcessCleanupTimeoutSeconds * TimeSpan.TicksPerSecond
            && KeyLoadPublicChecksTimeout > TimeSpan.Zero && KeyLoadPublicChecksTimeout.Ticks <= MaximumKeyLoadPublicChecksTimeoutMinutes * TimeSpan.TicksPerMinute
            && KeyLoadMcpCleanupTimeout > TimeSpan.Zero && KeyLoadMcpCleanupTimeout.Ticks <= MaximumKeyLoadMcpCleanupTimeoutSeconds * TimeSpan.TicksPerSecond
            && KeyLoadFaultChecksTimeout > TimeSpan.Zero && KeyLoadFaultChecksTimeout.Ticks <= MaximumKeyLoadFaultChecksTimeoutMinutes * TimeSpan.TicksPerMinute
            && KeyLoadFaultCliTimeout > TimeSpan.Zero && KeyLoadFaultCliTimeout.Ticks <= MaximumKeyLoadFaultCliTimeoutSeconds * TimeSpan.TicksPerSecond
            && KeyLoadFaultRestartTimeout > TimeSpan.Zero && KeyLoadFaultRestartTimeout.Ticks <= MaximumKeyLoadFaultRestartTimeoutSeconds * TimeSpan.TicksPerSecond
            && KeyLoadFaultProcessDrainTimeout > TimeSpan.Zero && KeyLoadFaultProcessDrainTimeout.Ticks <= MaximumKeyLoadFaultProcessDrainTimeoutSeconds * TimeSpan.TicksPerSecond
            && PostgresSlotObservationTimeout > TimeSpan.Zero && PostgresSlotObservationTimeout.Ticks <= MaximumPostgresSlotObservationTimeoutSeconds * TimeSpan.TicksPerSecond
            && ComparisonDataCleanupTimeout > TimeSpan.Zero && ComparisonDataCleanupTimeout.Ticks <= MaximumComparisonDataCleanupTimeoutSeconds * TimeSpan.TicksPerSecond
            && ProgressWriteTimeout > TimeSpan.Zero && ProgressWriteTimeout.Ticks <= MaximumProgressWriteTimeoutSeconds * TimeSpan.TicksPerSecond
            && MongoAuthenticationMaximumPoolSize is >= PositiveMinimum and <= MaximumMongoAuthenticationMaximumPoolSize
            && ProgressFileBufferBytes is >= PositiveMinimum and <= MaximumProgressFileBufferBytes
            && NativeToolReadBufferCharacters is >= PositiveMinimum and <= MaximumNativeToolReadBufferCharacters
            && NativeToolMaximumOutputCharacters is >= PositiveMinimum and <= MaximumNativeToolMaximumOutputCharacters
            && KeyLoadFaultMaximumEvidenceBytes is >= PositiveMinimum and <= MaximumKeyLoadFaultMaximumEvidenceBytes
            && NativeToolReadBufferCharacters <= NativeToolMaximumOutputCharacters
            && KeyLoadFaultExitedTimeout > TimeSpan.Zero && KeyLoadFaultExitedTimeout.Ticks <= MaximumKeyLoadFaultExitedTimeoutSeconds * TimeSpan.TicksPerSecond
            && KeyLoadFaultAttemptTimeout > TimeSpan.Zero && KeyLoadFaultAttemptTimeout.Ticks <= MaximumKeyLoadFaultAttemptTimeoutSeconds * TimeSpan.TicksPerSecond
            && KeyLoadFaultReadinessTimeout > TimeSpan.Zero && KeyLoadFaultReadinessTimeout.Ticks <= MaximumKeyLoadFaultReadinessTimeoutSeconds * TimeSpan.TicksPerSecond
            && KeyLoadFaultPollInterval > TimeSpan.Zero && KeyLoadFaultPollInterval.Ticks <= MaximumKeyLoadFaultPollIntervalMilliseconds * TimeSpan.TicksPerMillisecond
            && KeyLoadFaultOutputCharacters is >= PositiveMinimum and <= MaximumKeyLoadFaultOutputCharacters
            && KeyLoadFaultErrorCharacters is >= PositiveMinimum and <= MaximumKeyLoadFaultErrorCharacters
            && MongoReadinessOutputCharacters is >= PositiveMinimum and <= MaximumMongoReadinessOutputCharacters
            && MongoReadinessErrorCharacters is >= PositiveMinimum and <= MaximumMongoReadinessErrorCharacters
            && KeyLoadFaultPollInterval < KeyLoadFaultReadinessTimeout
            && MaximumResourceDiagnosticRecords is >= PositiveMinimum and <= CeilingMaximumResourceDiagnosticRecords;
}
