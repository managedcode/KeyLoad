namespace KeyLoad.Core;

internal readonly record struct EventVectorEncodedRow(ReadOnlyMemory<byte> Key, ReadOnlyMemory<byte> Value)
{
    internal long EncodedBytes => checked((long)Key.Length + Value.Length);

    internal static EventVectorEncodedRow Create<T>(byte[] key, T value, EventVectorAdmissionPolicy admission)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(admission);
        admission.RequireEncodedBytes(checked(key.LongLength + NativeSerialization.Measure(value)));
        var encoded = NativeSerialization.Serialize(value);
        var result = new EventVectorEncodedRow(key, encoded);
        admission.RequireEncodedBytes(result.EncodedBytes);
        return result;
    }
}
