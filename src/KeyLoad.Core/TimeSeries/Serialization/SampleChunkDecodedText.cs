namespace KeyLoad.Core.TimeSeries;

internal sealed record SampleChunkDecodedText(string SeriesId, string[] EventIds,
    string[] TagDictionary, int[] TagIndexes);
