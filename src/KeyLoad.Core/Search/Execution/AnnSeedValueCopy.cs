namespace KeyLoad.Core.Search;

/// <summary>Copies validated ANN seed values while retaining the original per-value work debit.</summary>
internal static class AnnSeedValueCopy
{
    internal static float[] Copy(VectorRecord vector, AnnSeedWork work)
    {
        var values = new float[vector.Values.Length];
        for (var index = 0; index < values.Length; index++)
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
