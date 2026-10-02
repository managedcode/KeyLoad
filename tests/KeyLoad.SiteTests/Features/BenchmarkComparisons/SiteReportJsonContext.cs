using System.Text.Json;
using System.Text.Json.Serialization;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

[JsonSerializable(typeof(SiteReport))]
internal sealed partial class SiteReportJsonContext : JsonSerializerContext
{
    internal static SiteReportJsonContext Create() => new(new JsonSerializerOptions(SiteTokens.JsonOptions));
}
