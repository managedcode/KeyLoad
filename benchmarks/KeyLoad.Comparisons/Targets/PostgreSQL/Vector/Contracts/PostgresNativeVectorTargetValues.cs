namespace KeyLoad.Comparisons.Targets;

internal static class PostgresNativeVectorTargetValues
{
    internal const string Public = ",public";
    internal const string PostgreSQLPgvector = "PostgreSQL + pgvector";
    internal const string Unverified = "unverified";
    internal const string UnverifiedNativePostgreSQLTopology = "unverified native PostgreSQL topology";
    internal const string SynchronousCommitOnLocalPostgreSQLWAL = "synchronous_commit=on; local PostgreSQL WAL flush";
    internal const string READCOMMITTEDPrimaryVectorReadback = "READ COMMITTED primary vector readback";
    internal const string NpgsqlPooledSQLTCP = "Npgsql pooled SQL/TCP";
    internal const string IsolatedMarkedBenchmarkSchema = "isolated marked benchmark schema";
    internal const string TheNativeVectorIndexHasNot = "The native vector index has not been selected.";
    internal const string APostgreSQLVectorTargetCanIngest = "A PostgreSQL vector target can ingest once.";
    internal const int VectorDimensions = 128;
    internal const int FsyncColumnOrdinal = 1;
    internal const int SynchronousCommitColumnOrdinal = 2;
    internal const int ServerVersionColumnOrdinal = 0;
    internal const int PgvectorVersionColumnOrdinal = 3;
    internal const int TlsColumnOrdinal = 4;
    internal const string NpgsqlPooledSQLTLS = "Npgsql pooled SQL/TLS";
}
