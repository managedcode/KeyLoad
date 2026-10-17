namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal static class TimescaleTimeSeriesIntensiveWindowsSql
{
    private const string Start = """
        CREATE FUNCTION "__KEYLOAD_SCHEMA__".kld_tsi_windows(
            p_set text, p_series text, p_from timestamptz, p_until timestamptz,
            p_width interval, p_max_samples int4, p_max_windows int4)
        RETURNS TABLE(window_ordinal int4, window_from timestamptz, window_until timestamptz,
            sample_count int8, sample_sum float8, sample_min float8, sample_max float8, sample_avg float8)
        LANGUAGE plpgsql STABLE SECURITY INVOKER CALLED ON NULL INPUT PARALLEL UNSAFE
        SET search_path = pg_catalog, "__KEYLOAD_SCHEMA__", pg_temp
        SET TimeZone = 'UTC'
        AS $kld$
        DECLARE
            v_width_us numeric;
            v_span_us numeric;
            v_slots numeric;
            v_slot_count int4;
            v_count int8;
            v_times timestamptz[];
            v_values float8[];
        BEGIN

        """;

    private const string Guards = """
            IF p_from IS NULL OR p_until IS NULL OR NOT isfinite(p_from) OR NOT isfinite(p_until)
                OR p_until < p_from OR p_width IS NULL OR NOT isfinite(p_width)
                OR extract(year FROM p_width) <> 0 OR extract(month FROM p_width) <> 0
                OR p_max_samples IS NULL OR p_max_samples NOT BETWEEN 1 AND 10000
                OR p_max_windows IS NULL OR p_max_windows NOT BETWEEN 1 AND 1000 THEN
                RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'Invalid intensive window bounds';
            END IF;
            v_width_us := extract(epoch FROM p_width) * 1000000;
            IF v_width_us <= 0 THEN
                RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'Invalid intensive window width';
            END IF;
            v_span_us := extract(epoch FROM (p_until - p_from)) * 1000000;
            v_slots := div(v_span_us, v_width_us)
                + CASE WHEN mod(v_span_us, v_width_us) <> 0 THEN 1 ELSE 0 END;
            IF v_slots > p_max_windows THEN
                RAISE EXCEPTION USING ERRCODE = '54000', MESSAGE = 'Intensive window budget exceeded';
            END IF;
            v_slot_count := v_slots::int4;

        """;

    private const string Select = """
            SELECT array_agg(b.sample_time ORDER BY b.sample_time, b.sample_sequence),
                array_agg(b.sample_value ORDER BY b.sample_time, b.sample_sequence), count(*)
            INTO v_times, v_values, v_count FROM (
                SELECT s.sample_time, s.sample_sequence, s.sample_value FROM samples AS s
                WHERE s.set_name = p_set AND s.series_id = p_series
                    AND s.sample_time >= p_from AND s.sample_time < p_until
                ORDER BY s.sample_time, s.sample_sequence LIMIT p_max_samples + 1) AS b;
            IF v_count > p_max_samples THEN
                RAISE EXCEPTION USING ERRCODE = '54000', MESSAGE = 'Intensive sample budget exceeded';
            END IF;

        """;

    private const string Results = """
            RETURN QUERY WITH grouped AS (
                SELECT div(extract(epoch FROM (r.sample_time - p_from)) * 1000000, v_width_us)::int4 AS ordinal,
                    count(r.sample_value) AS n, sum(r.sample_value) AS total, min(r.sample_value) AS low,
                    max(r.sample_value) AS high, avg(r.sample_value) AS mean
                FROM unnest(v_times, v_values) AS r(sample_time, sample_value) GROUP BY ordinal),
            offsets AS (
                SELECT g.ordinal, g.ordinal::numeric * v_width_us AS start_us,
                    least((g.ordinal + 1)::numeric * v_width_us, v_span_us) AS end_us
                FROM generate_series(0, v_slot_count - 1) AS g(ordinal))
            SELECT b.ordinal,
                p_from + make_interval(days => div(b.start_us, 86400000000)::int4)
                    + mod(b.start_us, 86400000000)::int8 * INTERVAL '1 microsecond',
                p_from + make_interval(days => div(b.end_us, 86400000000)::int4)
                    + mod(b.end_us, 86400000000)::int8 * INTERVAL '1 microsecond',
                coalesce(a.n, 0::int8), coalesce(a.total, 0::float8), a.low, a.high, a.mean
            FROM offsets AS b LEFT JOIN grouped AS a ON a.ordinal = b.ordinal ORDER BY b.ordinal;
        END;
        $kld$;
        """;

    internal static string Create(string schemaName)
        => TimescaleTimeSeriesIntensiveSchema.BindSchema(Start + TimescaleTimeSeriesIntensiveAppendGuardSql.Scope + Guards + Select + Results, schemaName);
}
