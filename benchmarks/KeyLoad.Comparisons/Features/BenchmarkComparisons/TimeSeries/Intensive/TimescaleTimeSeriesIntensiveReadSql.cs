namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal static class TimescaleTimeSeriesIntensiveReadSql
{
    private const string RawStart = """
        CREATE FUNCTION "__KEYLOAD_SCHEMA__".kld_tsi_read(
            p_set text, p_series text, p_from timestamptz, p_until timestamptz, p_limit int4)
        RETURNS TABLE(series_id text, event_id text, sample_time timestamptz,
            sample_value float8, sample_sequence int8, tags_text text)
        LANGUAGE plpgsql STABLE SECURITY INVOKER CALLED ON NULL INPUT PARALLEL UNSAFE
        SET search_path = pg_catalog, "__KEYLOAD_SCHEMA__", pg_temp
        SET TimeZone = 'UTC'
        AS $kld$
        BEGIN

        """;

    private const string RawBody = """
            IF p_from IS NULL OR p_until IS NULL OR p_limit IS NULL OR p_limit NOT BETWEEN 1 AND 1000 THEN
                RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'Invalid intensive read bounds';
            END IF;
            RETURN QUERY SELECT s.series_id, s.event_id, s.sample_time, s.sample_value,
                s.sample_sequence, s.tags_json::text FROM samples AS s
            WHERE s.set_name = p_set AND s.series_id = p_series
                AND s.sample_time >= p_from AND s.sample_time <= p_until
            ORDER BY s.sample_time, s.sample_sequence LIMIT p_limit;
        END;
        $kld$;

        """;

    private const string LatestStart = """
        CREATE FUNCTION "__KEYLOAD_SCHEMA__".kld_tsi_latest(p_set text, p_series text, p_cut timestamptz)
        RETURNS TABLE(series_id text, event_id text, sample_time timestamptz,
            sample_value float8, sample_sequence int8, tags_text text)
        LANGUAGE plpgsql STABLE SECURITY INVOKER CALLED ON NULL INPUT PARALLEL UNSAFE
        SET search_path = pg_catalog, "__KEYLOAD_SCHEMA__", pg_temp
        SET TimeZone = 'UTC'
        AS $kld$
        BEGIN

        """;

    private const string LatestBody = """
            RETURN QUERY SELECT s.series_id, s.event_id, s.sample_time, s.sample_value,
                s.sample_sequence, s.tags_json::text FROM samples AS s
            WHERE s.set_name = p_set AND s.series_id = p_series
                AND (p_cut IS NULL OR s.sample_time <= p_cut)
            ORDER BY s.sample_time DESC, s.sample_sequence DESC LIMIT 1;
        END;
        $kld$;
        """;

    internal static string Create(string schemaName)
        => TimescaleTimeSeriesIntensiveSchema.BindSchema(RawStart +
            TimescaleTimeSeriesIntensiveAppendGuardSql.Scope + RawBody + LatestStart +
            TimescaleTimeSeriesIntensiveAppendGuardSql.Scope + LatestBody, schemaName);
}
