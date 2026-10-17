namespace KeyLoad.Client;

internal sealed class AggregateReplayInput(int maximumBytes, int maximumDepth)
{
    private long usedBytes;

    internal void AddState(string json, string name, AggregateReplayWorkerLimits limits)
    {
        Count(json);
        AggregateReplayJson.ValidateState(json, limits, name);
    }

    internal void AddJson(string json, string name)
    {
        Count(json);
        AggregateReplayJson.ValidateJson(json, maximumBytes, maximumDepth, name);
    }

    private void Count(string json)
    {
        if (json is null)
        {
            throw new InvalidDataException(AggregateReplayMessages.MissingInput);
        }
        usedBytes += System.Text.Encoding.UTF8.GetByteCount(json);
        if (usedBytes > maximumBytes)
        {
            throw new InvalidDataException(AggregateReplayMessages.InputLimitExceeded);
        }
    }
}
