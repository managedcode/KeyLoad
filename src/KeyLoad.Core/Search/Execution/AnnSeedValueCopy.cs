namespace KeyLoad.Core.Features.Search;

/// <summary>Copies validated ANN seed values while retaining the original per-value work debit.</summary>
internal static class AnnSeedValueCopy
{
    private const int FirstRecordIndex = 0;

    internal static float[] Copy(VectorRecord vector, AnnSeedWork work)
    {
        var values = new float[vector.Values.Length];
        for (var index = FirstRecordIndex; index < values.Length; index++)
        {
            work.Charge();
            var value = vector.Values[index];
            if (!float.IsFinite(value))
            {
                throw Errors.Fail(ErrorCode.Corruption, AnnSeedBuffer.CorruptSource);
            }
            values[index] = value;
        }
        return values;
    }
}
