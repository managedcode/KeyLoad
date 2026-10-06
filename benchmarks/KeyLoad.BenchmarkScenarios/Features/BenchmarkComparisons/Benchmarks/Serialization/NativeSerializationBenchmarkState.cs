namespace KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons;

internal sealed class NativeSerializationBenchmarkState<T>
{
    internal NativeSerializationBenchmarkState(T input)
    {
        Input = input;
        NativeBytes = NativeSerialization.Serialize(input);
        JsonBytes = JsonDefaults.Serialize(input);
        RequireContent(NativeDecode());
        RequireContent(JsonDecode());
    }

    internal T Input { get; }
    internal byte[] NativeBytes { get; }
    internal byte[] JsonBytes { get; }

    internal byte[] NativeEncode() => NativeSerialization.Serialize(Input);
    internal T NativeDecode() => NativeSerialization.Deserialize<T>(NativeBytes);
    internal byte[] JsonEncode() => JsonDefaults.Serialize(Input);
    internal T JsonDecode() => JsonDefaults.Deserialize<T>(JsonBytes);

    private void RequireContent(T decoded)
    {
        const string RequireContentFailureMessage = "The typed native/JSON benchmark corpus changed during roundtrip.";

        if (!JsonDefaults.Serialize(decoded).AsSpan().SequenceEqual(JsonBytes))
        {
            throw new InvalidOperationException(RequireContentFailureMessage);
        }
    }
}
