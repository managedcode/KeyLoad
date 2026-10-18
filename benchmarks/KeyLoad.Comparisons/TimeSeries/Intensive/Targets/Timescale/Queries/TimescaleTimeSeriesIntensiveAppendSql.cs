namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal static class TimescaleTimeSeriesIntensiveAppendSql
{
    private const string BatchStart = """
        CREATE FUNCTION "__KEYLOAD_SCHEMA__".kld_tsi_append_batch(
            p_set text, p_series text, p_command uuid, p_events text[],
            p_times timestamptz[], p_values float8[], p_tags jsonb)
        RETURNS TABLE(input_ordinal int4, command_id uuid, sample_sequence int8)
        LANGUAGE plpgsql VOLATILE SECURITY INVOKER CALLED ON NULL INPUT PARALLEL UNSAFE
        SET search_path = pg_catalog, "__KEYLOAD_SCHEMA__", pg_temp
        SET TimeZone = 'UTC'
        AS $kld$
        DECLARE
            v_ordinal int4;
            v_last int8;
            v_existing record;
            v_sequences int8[] := ARRAY[]::int8[];
        BEGIN

        """;

    private const string BatchBody = """
            SELECT c.last_sequence INTO v_last FROM series_counter AS c
            WHERE c.set_name = p_set AND c.series_id = p_series FOR UPDATE;
            IF NOT FOUND THEN
                RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'Unknown intensive counter';
            END IF;
            FOR v_ordinal IN 1..cardinality(p_events) LOOP
                SELECT e.* INTO v_existing FROM event_identity AS e
                WHERE e.set_name = p_set AND e.series_id = p_series AND e.event_id = p_events[v_ordinal];
                IF FOUND THEN
                    IF v_existing.sample_time IS DISTINCT FROM p_times[v_ordinal]
                        OR v_existing.sample_value IS DISTINCT FROM p_values[v_ordinal]
                        OR v_existing.tags_json IS DISTINCT FROM p_tags THEN
                        RAISE EXCEPTION USING ERRCODE = '23505', MESSAGE = 'Intensive event conflict';
                    END IF;
                    v_sequences := array_append(v_sequences, v_existing.sample_sequence);
                ELSE
                    v_last := v_last + 1;
                    INSERT INTO event_identity(set_name, series_id, event_id, sample_time,
                        sample_value, sample_sequence, tags_json)
                    VALUES (p_set, p_series, p_events[v_ordinal], p_times[v_ordinal],
                        p_values[v_ordinal], v_last, p_tags);
                    INSERT INTO samples(set_name, series_id, event_id, sample_time,
                        sample_value, sample_sequence, tags_json)
                    VALUES (p_set, p_series, p_events[v_ordinal], p_times[v_ordinal],
                        p_values[v_ordinal], v_last, p_tags);
                    v_sequences := array_append(v_sequences, v_last);
                END IF;
            END LOOP;
            UPDATE series_counter AS c SET last_sequence = v_last
            WHERE c.set_name = p_set AND c.series_id = p_series;
            RETURN QUERY SELECT r.ordinal::int4, p_command, r.sequence
            FROM unnest(v_sequences) WITH ORDINALITY AS r(sequence, ordinal) ORDER BY r.ordinal;
        END;
        $kld$;
        """;

    private const string Scalar = """
        CREATE FUNCTION "__KEYLOAD_SCHEMA__".kld_tsi_append_one(
            p_set text, p_series text, p_command uuid, p_event text,
            p_time timestamptz, p_value float8, p_tags jsonb)
        RETURNS TABLE(command_id uuid, sample_sequence int8)
        LANGUAGE sql VOLATILE SECURITY INVOKER CALLED ON NULL INPUT PARALLEL UNSAFE
        SET search_path = pg_catalog, "__KEYLOAD_SCHEMA__", pg_temp
        SET TimeZone = 'UTC'
        AS $kld$
            SELECT r.command_id, r.sample_sequence
            FROM kld_tsi_append_batch(p_set, p_series, p_command,
                ARRAY[p_event]::text[], ARRAY[p_time]::timestamptz[], ARRAY[p_value]::float8[], p_tags) AS r
            ORDER BY r.input_ordinal;
        $kld$;
        """;

    internal static string Create(string schemaName)
        => TimescaleTimeSeriesIntensiveSchema.BindSchema(BatchStart +
            TimescaleTimeSeriesIntensiveAppendGuardSql.Scope + TimescaleTimeSeriesIntensiveAppendGuardSql.Arrays +
            TimescaleTimeSeriesIntensiveAppendGuardSql.Samples + BatchBody + Scalar, schemaName);
}
