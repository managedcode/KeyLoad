namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal static class TimescaleTimeSeriesIntensiveAggregateSql
{
    private const string Start = """
        CREATE FUNCTION "__KEYLOAD_SCHEMA__".kld_tsi_aggregate(
            p_set text, p_series text, p_from timestamptz, p_until timestamptz, p_max_samples int4)
        RETURNS TABLE(sample_count int8, sample_sum float8, sample_min float8, sample_max float8, sample_avg float8)
        LANGUAGE plpgsql STABLE SECURITY INVOKER CALLED ON NULL INPUT PARALLEL UNSAFE
        SET search_path = pg_catalog, "__KEYLOAD_SCHEMA__", pg_temp
        SET TimeZone = 'UTC'
        AS $kld$
        DECLARE
            v_values float8[];
            v_count int8;
        BEGIN

        """;

    private const string Body = """
            IF p_from IS NULL OR p_max_samples IS NULL OR p_max_samples NOT BETWEEN 1 AND 10000 THEN
                RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'Invalid intensive aggregate bounds';
            END IF;
            SELECT array_agg(b.sample_value ORDER BY b.sample_time, b.sample_sequence), count(*)
            INTO v_values, v_count FROM (
                SELECT s.sample_time, s.sample_sequence, s.sample_value FROM samples AS s
                WHERE s.set_name = p_set AND s.series_id = p_series AND s.sample_time >= p_from
                    AND (p_until IS NULL OR s.sample_time < p_until)
                ORDER BY s.sample_time, s.sample_sequence LIMIT p_max_samples + 1) AS b;
            IF v_count > p_max_samples THEN
                RAISE EXCEPTION USING ERRCODE = '54000', MESSAGE = 'Intensive sample budget exceeded';
            END IF;
            RETURN QUERY SELECT count(v.value), coalesce(sum(v.value), 0::float8),
                min(v.value), max(v.value), avg(v.value) FROM unnest(v_values) AS v(value);
        END;
        $kld$;
        """;

    internal static string Create(string schemaName)
        => TimescaleTimeSeriesIntensiveSchema.BindSchema(Start + TimescaleTimeSeriesIntensiveAppendGuardSql.Scope + Body, schemaName);
}
