using System.Text;
using System.Text.Json;

namespace KeyLoad.Client;

internal static class AggregateReplayJson
{
    internal static void ValidateState(string state, AggregateReplayWorkerLimits limits, string name)
        => ValidateJson(state, limits.MaximumStateBytes, limits.MaximumJsonDepth, name);

    internal static void ValidateJson(string json, int maximumBytes, int maximumDepth, string name)
    {
        if (json is null || Encoding.UTF8.GetByteCount(json) > maximumBytes)
        {
            throw new InvalidDataException(string.Concat(name, AggregateReplayMessages.ByteLimitSuffix));
        }
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = maximumDepth });
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(string.Concat(name, AggregateReplayMessages.JsonFormatSuffix), exception);
        }
    }
}
