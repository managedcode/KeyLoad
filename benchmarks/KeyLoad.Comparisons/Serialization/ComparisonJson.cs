using System.Text.Json;
using System.Text.Json.Serialization;

namespace KeyLoad.Comparisons;

/// <summary>Canonical execution comparison value serialization, with no standalone report producer.</summary>
public static class ComparisonJson
{
    /// <summary>Shared strict value and topology converters for execution correctness contracts.</summary>
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };
        ComparisonContractJson.Configure(options);
        return options;
    }
}
