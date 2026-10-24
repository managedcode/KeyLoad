namespace KeyLoad.BenchmarkScenarios.BenchmarkComparisons;

internal sealed class SerializationBenchmarkState<T>
{
    internal SerializationBenchmarkState(T input)
    {
        Input = input;
        RuntimeBytes = OrleansSerialization.Serialize(input);
        JsonBytes = JsonDefaults.Serialize(input);
        RequireContent(RuntimeDecode());
        RequireContent(JsonDecode());
    }

    internal T Input
    {
        get;
    }
    internal byte[] RuntimeBytes
    {
        get;
    }
    internal byte[] JsonBytes
    {
        get;
    }

    internal byte[] RuntimeEncode() => OrleansSerialization.Serialize(Input);
    internal T RuntimeDecode() => OrleansSerialization.Deserialize<T>(RuntimeBytes);
    internal byte[] JsonEncode() => JsonDefaults.Serialize(Input);
    internal T JsonDecode() => JsonDefaults.Deserialize<T>(JsonBytes);

    private void RequireContent(T decoded)
    {
        const string RequireContentFailureMessage = "The typed execution/JSON benchmark corpus changed during roundtrip.";

        if (!JsonDefaults.Serialize(decoded).AsSpan().SequenceEqual(JsonBytes))
        {
            throw new InvalidOperationException(RequireContentFailureMessage);
        }
    }
}
