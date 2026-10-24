namespace KeyLoad.Comparisons.Targets;

public sealed partial class PostgresTarget
{
    private const string FinalFlushSettingsQuery = "SELECT current_setting('fsync'), current_setting('synchronous_commit')";
    private const string EnabledSetting = "on", FinalFlushFailure = "PostgresFinalDocumentFlushRequired";
    private const int FsyncField = 0, SynchronousCommitField = 1;
    // PostgreSQL "Populating a Database" loads through one COPY per transaction (https://www.postgresql.org/docs/current/populate.html); 10,000 rows keeps all 16 clients loading.
    private const int CopyBatchRows = 10_000;
    /// <inheritdoc/>
    public int IngestionBatchRecords => CopyBatchRows;
    /// <inheritdoc/>
    public Task InitializeDocumentsAsync(DocumentComparisonInitialization initialization, CancellationToken token)
    {
        DocumentInitialization.Validate(initialization);
        return InitializeAsync(initialization.Corpus, token);
    }
    /// <inheritdoc/>
    public async Task VerifyDocumentCopiesAsync(DocumentComparisonSchedule schedule, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        await using var connection = await source.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (var settings = connection.CreateCommand())
        {
            settings.CommandText = FinalFlushSettingsQuery;
            await using var reader = await settings.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false) || reader.GetString(FsyncField) != EnabledSetting || reader.GetString(SynchronousCommitField) != EnabledSetting)
            {
                throw new ComparisonFailureException(FinalFlushFailure);
            }
        }
        // Captures a fresh WAL boundary after every measured session and the full primary oracle have joined.
        // Existing execution observation waits for BOTH named standbys to flush AND replay through this boundary.
        var observed = await new PostgresTopology(lifecycleOptions, provider: timeProvider)
            .ObserveCopiesAsync(connection, topology, Profile, cancellationToken).ConfigureAwait(false);
        Profile = PostgresDocumentCopyProof.BindFinalState(observed, Profile, schedule);
    }
    /// <inheritdoc/>
    public Task<IDocumentComparisonSession> OpenDocumentSessionAsync(CancellationToken token)
        => DocumentSessionAcquisition<Npgsql.NpgsqlConnection>.OpenAsync(
            cancellation => source.OpenConnectionAsync(cancellation).AsTask(), AdmitDocumentConnectionAsync,
            connection => new PostgresComparisonSession(connection, topK, graphDepth,
                -DocumentMeasurementValues.SingleItemCount, lifecycleOptions, provider: timeProvider), token);
    private static async Task AdmitDocumentConnectionAsync(Npgsql.NpgsqlConnection connection, CancellationToken token)
    {
        await using var probe = connection.CreateCommand();
        probe.CommandText = DocumentProtocolText.SELECT1;
        _ = await probe.ExecuteScalarAsync(token).ConfigureAwait(false);
    }
}
