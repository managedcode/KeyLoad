namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal static class TimescaleTimeSeriesIntensiveAppendGuardSql
{
    internal const string Scope = """
        IF p_set IS NULL OR octet_length(convert_to(p_set, 'UTF8')) NOT BETWEEN 1 AND 256
            OR p_series IS NULL OR octet_length(convert_to(p_series, 'UTF8')) NOT BETWEEN 1 AND 256 THEN
            RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'Invalid intensive scope';
        END IF;

        """;

    internal const string Arrays = """
        IF p_command IS NULL OR p_events IS NULL OR p_times IS NULL OR p_values IS NULL
            OR array_ndims(p_events) IS DISTINCT FROM 1 OR array_lower(p_events, 1) IS DISTINCT FROM 1
            OR array_ndims(p_times) IS DISTINCT FROM 1 OR array_lower(p_times, 1) IS DISTINCT FROM 1
            OR array_ndims(p_values) IS DISTINCT FROM 1 OR array_lower(p_values, 1) IS DISTINCT FROM 1
            OR cardinality(p_events) NOT BETWEEN 1 AND 256
            OR cardinality(p_events) IS DISTINCT FROM cardinality(p_times)
            OR cardinality(p_events) IS DISTINCT FROM cardinality(p_values) THEN
            RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'Invalid intensive arrays';
        END IF;
        IF array_position(p_events, NULL::text) IS NOT NULL
            OR array_position(p_times, NULL::timestamptz) IS NOT NULL
            OR array_position(p_values, NULL::float8) IS NOT NULL
            OR p_tags IS NULL OR jsonb_typeof(p_tags) IS DISTINCT FROM 'object'
            OR octet_length(convert_to(p_tags::text, 'UTF8')) > 4096 THEN
            RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'Invalid intensive content';
        END IF;

        """;

    internal const string Samples = """
        FOR v_ordinal IN 1..cardinality(p_events) LOOP
            IF octet_length(convert_to(p_events[v_ordinal], 'UTF8')) NOT BETWEEN 1 AND 256
                OR NOT isfinite(p_times[v_ordinal])
                OR p_times[v_ordinal] < TIMESTAMPTZ '0001-01-01 00:00:00.000001+00'
                OR p_times[v_ordinal] > TIMESTAMPTZ '9999-12-31 23:59:59.999999+00'
                OR NOT (p_values[v_ordinal] > '-Infinity'::float8
                    AND p_values[v_ordinal] < 'Infinity'::float8) THEN
                RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'Invalid intensive sample';
            END IF;
        END LOOP;

        """;
}
