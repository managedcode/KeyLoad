namespace KeyLoad.Comparisons.Targets;

internal static class PostgresSchemaCommands
{
    internal const string DimensionRangeError = "PostgreSQL vector dimensions must be between 2 and 1024.";
    internal const string SetCreateContext = "SELECT pg_catalog.set_config('keyload.benchmark.run_id', $1::uuid::text, true), pg_catalog.set_config('keyload.benchmark.owner_id', $2::uuid::text, true), pg_catalog.set_config('keyload.benchmark.dimensions', $3::integer::text, true), pg_catalog.set_config('keyload.benchmark.lock_key', $4::bigint::text, true)";
    internal const string SetCleanupContext = "SELECT pg_catalog.set_config('keyload.benchmark.run_id', $1::uuid::text, true), pg_catalog.set_config('keyload.benchmark.owner_id', $2::uuid::text, true), pg_catalog.set_config('keyload.benchmark.lock_key', $3::bigint::text, true)";
    internal const string AcquireNamespaceLock = "SELECT pg_catalog.pg_advisory_xact_lock($1::bigint)";
    internal const string CreateExtension = "CREATE EXTENSION IF NOT EXISTS vector";
    internal const string VerifySettings = "SELECT current_setting('server_version'), current_setting('fsync'), current_setting('synchronous_commit'), (SELECT extversion FROM pg_extension WHERE extname = 'vector'), (SELECT ssl FROM pg_stat_ssl WHERE pid=pg_backend_pid())";

    internal const string CreateOwnedSchema = """
        DO $keyload$
        DECLARE
            schema_name text := 'bench_' || replace(current_setting('keyload.benchmark.run_id')::uuid::text, '-', '');
            owner_id text := current_setting('keyload.benchmark.owner_id')::uuid::text;
            dimensions integer := current_setting('keyload.benchmark.dimensions')::integer;
        BEGIN
            IF dimensions < 2 OR dimensions > 1024 THEN
                RAISE EXCEPTION 'PostgreSQL vector dimensions are outside the supported range';
            END IF;
            EXECUTE pg_catalog.format('CREATE SCHEMA %I', schema_name);
            EXECUTE pg_catalog.format('CREATE TABLE %I.documents(id text COLLATE "C" PRIMARY KEY, body jsonb NOT NULL, embedding vector(%s))', schema_name, dimensions);
            EXECUTE pg_catalog.format('CREATE TABLE %I.queue(id text COLLATE "C" PRIMARY KEY, body jsonb NOT NULL, state text NOT NULL DEFAULT ''ready'', lease_owner uuid, lease_until timestamptz, attempts integer NOT NULL DEFAULT 0)', schema_name);
            EXECUTE pg_catalog.format('CREATE INDEX %I ON %I.queue(state, id)', 'queue_ready', schema_name);
            EXECUTE pg_catalog.format('CREATE TABLE %I.edges(source text COLLATE "C", target text COLLATE "C", PRIMARY KEY(source,target))', schema_name);
            EXECUTE pg_catalog.format('CREATE TABLE %I.events(stream_id text COLLATE "C" PRIMARY KEY, event_id uuid NOT NULL, revision bigint NOT NULL CHECK(revision=1), body jsonb NOT NULL)', schema_name);
            EXECUTE pg_catalog.format('COMMENT ON SCHEMA %I IS %L', schema_name, owner_id);
        END;
        $keyload$
        """;

    internal const string DropIfOwnedSchema = """
        DO $keyload$
        DECLARE
            schema_name text := 'bench_' || replace(current_setting('keyload.benchmark.run_id')::uuid::text, '-', '');
            owner_id text := current_setting('keyload.benchmark.owner_id')::uuid::text;
            stored_owner text;
        BEGIN
            SELECT pg_catalog.obj_description(n.oid, 'pg_namespace')
            INTO stored_owner
            FROM pg_catalog.pg_namespace AS n
            WHERE n.nspname = schema_name;
            IF stored_owner = owner_id THEN
                EXECUTE pg_catalog.format('DROP SCHEMA %I CASCADE', schema_name);
            END IF;
        END;
        $keyload$
        """;

    internal const string GraphNeighbors = "SELECT target FROM edges WHERE source=$1 ORDER BY target";
    internal const string GraphTraverse = """
        WITH RECURSIVE reachable(id,depth) AS (
            SELECT $1::text COLLATE "C",0
            UNION SELECT e.target,r.depth+1 FROM reachable r JOIN edges e ON e.source=r.id WHERE r.depth<$2)
        SELECT DISTINCT id FROM reachable WHERE id<>$1 ORDER BY id
        """;
}
