using System.Text.Json.Serialization;

namespace KeyLoad.UnitTests.QueryExecution;

internal sealed record QueryAdapterOrder(decimal Number, string Status)
{
    [JsonPropertyName(QueryAdapterTestTokens.OddName)]
    public string? Secret
    {
        get; init;
    }

    [JsonPropertyName(QueryAdapterTestTokens.OptionalNumber)]
    public decimal? Optional
    {
        get; init;
    }
}
