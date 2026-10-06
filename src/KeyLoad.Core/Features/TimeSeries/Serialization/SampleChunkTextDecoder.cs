namespace KeyLoad.Core.Features.TimeSeries;

internal sealed record SampleChunkDecodedText(string SeriesId, string[] EventIds,
    string[] TagDictionary, int[] TagIndexes);

internal static class SampleChunkTextDecoder
{
    internal static SampleChunkDecodedText Decode(SampleChunkPayload payload, ReadExecutionBudget budget)
    {
        var seriesReader = new SampleChunkReader(payload.Series.Span);
        var seriesId = SampleChunkText.ReadFramed(ref seriesReader, budget);
        seriesReader.RequireEnd();

        var eventIds = DecodeEventIds(payload, budget);
        var (dictionary, indexes) = DecodeTags(payload, budget);
        return new(seriesId, eventIds, dictionary, indexes);
    }

    private static string[] DecodeEventIds(SampleChunkPayload payload, ReadExecutionBudget budget)
    {
        const int IndexInitialValue = 0;

        var result = new string[payload.RecordCount];
        var unique = new HashSet<string>(payload.RecordCount, StringComparer.Ordinal);
        var reader = new SampleChunkReader(payload.EventIds.Span);
        for (var index = IndexInitialValue; index < result.Length; index++)
        {
            budget.Check();
            var eventId = SampleChunkText.ReadFramed(ref reader, budget);
            SampleChunkWire.Require(unique.Add(eventId));
            result[index] = eventId;
        }
        reader.RequireEnd();
        return result;
    }

    private static (string[] Dictionary, int[] Indexes) DecodeTags(SampleChunkPayload payload,
        ReadExecutionBudget budget)
    {
        const int IndexInitialValue = 0;

        var reader = new SampleChunkReader(payload.Tags.Span);
        var count = (int)reader.ReadVarUInt();
        var dictionary = new string[count];
        var unique = new HashSet<string>(count, StringComparer.Ordinal);
        for (var index = IndexInitialValue; index < dictionary.Length; index++)
        {
            budget.Check();
            var text = SampleChunkText.ReadFramed(ref reader, budget);
            SampleChunkWire.Require(unique.Add(text));
            dictionary[index] = text;
        }
        var indexes = new int[payload.RecordCount];
        for (var index = IndexInitialValue; index < indexes.Length; index++)
        {
            budget.Check();
            indexes[index] = (int)reader.ReadVarUInt();
        }
        reader.RequireEnd();
        return (dictionary, indexes);
    }
}
