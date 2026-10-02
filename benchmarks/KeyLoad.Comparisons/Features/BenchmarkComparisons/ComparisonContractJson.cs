using System.Collections.Immutable;
using System.Text.Json;

namespace KeyLoad.Comparisons;

internal static class ComparisonContractJson
{
    internal static void Configure(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Converters.Add(JsonDefaults.Options.GetConverter(typeof(ImmutableArray<float>)));
        options.Converters.Add(JsonDefaults.Options.GetConverter(typeof(ImmutableArray<FoundDocument>)));
        options.Converters.Add(JsonDefaults.Options.GetConverter(typeof(ImmutableArray<string>)));
        options.Converters.Add(JsonDefaults.Options.GetConverter(typeof(ImmutableArray<OperationSample>)));
        options.Converters.Add(JsonDefaults.Options.GetConverter(typeof(ImmutableArray<TargetProfile>)));
        options.Converters.Add(JsonDefaults.Options.GetConverter(typeof(ImmutableArray<ComparisonCase>)));
    }
}
