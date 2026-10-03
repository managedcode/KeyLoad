using System.Text.Json;
using KeyLoad.Replication;

namespace KeyLoad.Orleans;

internal static class ReplicaReplayClassifier
{
    internal static ReplicaReplayPool Classify(ReplicaRpc method, ReadOnlySpan<byte> payload, ReplicaConfiguration configuration,
        int maximumControlPayloadBytes)
    {
        try
        {
            return method switch
            {
                ReplicaRpc.Forward or ReplicaRpc.Append or ReplicaRpc.ReadProbe
                    => Application(method, payload, configuration, maximumControlPayloadBytes),
                ReplicaRpc.ReadBarrier or ReplicaRpc.ControlReadBarrier => Read(method, payload),
                _ => Critical(method, payload, configuration)
            };
        }
        catch (JsonException)
        {
            throw Errors.Fail(ErrorCode.Validation, ReplicaTransportProtocol.InvalidPayload);
        }
    }

    private static ReplicaReplayPool Application(ReplicaRpc method, ReadOnlySpan<byte> payload, ReplicaConfiguration configuration,
        int maximumControlPayloadBytes)
    {
        var reader = new Utf8JsonReader(payload, new JsonReaderOptions { MaxDepth = JsonDefaults.Options.MaxDepth });
        ReplicaPayloadReader.Require(reader.Read());
        var pool = method is ReplicaRpc.Append or ReplicaRpc.ReadProbe
            ? ReplicaAppendClassification.Classify(ref reader, configuration, maximumControlPayloadBytes, method == ReplicaRpc.ReadProbe)
            : ReplicaOperationClassification.IsControl(ref reader, maximumControlPayloadBytes) ? ReplicaReplayPool.Critical : ReplicaReplayPool.Forward;
        ReplicaPayloadReader.Require(!reader.Read());
        return pool;
    }

    private static ReplicaReplayPool Read(ReplicaRpc method, ReadOnlySpan<byte> payload)
    {
        ReplicaPayloadReader.Require(payload.Length <= ReplicaTransportProtocol.MaximumMetadataBytes);
        var value = JsonSerializer.Deserialize<string>(payload, JsonDefaults.Options);
        ReplicaPayloadReader.Require(value is not null && value.Length == 0);
        return method == ReplicaRpc.ControlReadBarrier ? ReplicaReplayPool.Critical : ReplicaReplayPool.ReadBarrier;
    }

    private static ReplicaReplayPool Critical(ReplicaRpc method, ReadOnlySpan<byte> payload, ReplicaConfiguration configuration)
    {
        ReplicaCriticalClassification.Validate(method, payload, configuration);
        return ReplicaReplayPool.Critical;
    }
}
