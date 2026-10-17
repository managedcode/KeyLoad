using System.Text.Json;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>Parses only actual bounded Docker identity fields; no environment or free diagnostic text is requested.</summary>
internal sealed record MongoNativeReadinessRegressionIdentity(string Id, string Name, string Image, bool Running,
    IReadOnlyDictionary<string, string[]> Networks, IReadOnlyDictionary<string, string> Labels)
{
    internal const string InspectFormat = "{\"id\":{{json .Id}},\"name\":{{json .Name}},\"image\":{{json .Config.Image}},"
        + "\"running\":{{json .State.Running}},\"networks\":{{json .NetworkSettings.Networks}},\"labels\":{{json .Config.Labels}}}";
    private const string IdField = "id", NameField = "name", ImageField = "image", RunningField = "running";
    private const string NetworksField = "networks", LabelsField = "labels", AliasesField = "Aliases";
    private const char NamePrefix = '/';
    private const string NativeAliasSuffix = ".dev.internal";
    private const int MinimumResourceAliases = 1, MaximumResourceAliases = 2, NativeAliasCount = 1;

    internal static MongoNativeReadinessRegressionIdentity Parse(string text)
    {
        using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = MongoNativeReadinessRegressionProtocol.JsonDepth });
        var root = document.RootElement;
        var id = root.GetProperty(IdField).GetString()!;
        var name = root.GetProperty(NameField).GetString()!;
        MongoNativeReadinessRegressionProtocol.Require(id.Length == MongoNativeReadinessRegressionProtocol.ContainerCharacters
            && id.All(static item => char.IsAsciiHexDigitLower(item)) && name.StartsWith(NamePrefix));
        name = name.TrimStart(NamePrefix);
        MongoNativeReadinessRegressionProtocol.Require(MongoNativeReadinessRegressionProtocol.Identifier(name));
        var networks = ReadNetworks(root.GetProperty(NetworksField));
        var labelValues = root.GetProperty(LabelsField);
        var labels = labelValues.ValueKind == JsonValueKind.Null ? new Dictionary<string, string>(StringComparer.Ordinal)
            : labelValues.EnumerateObject().ToDictionary(item => item.Name, item => item.Value.GetString()!, StringComparer.Ordinal);
        return new(id, name, root.GetProperty(ImageField).GetString()!, root.GetProperty(RunningField).GetBoolean(), networks, labels);
    }

    internal void RequireNode(string expectedName, string image, string alias)
    {
        MongoNativeReadinessRegressionProtocol.Require(Name == expectedName && Image == image && Running && Networks.Count > 0);
        foreach (var aliases in Networks.Values)
        {
            var selected = aliases.Count(value => value == alias);
            MongoNativeReadinessRegressionProtocol.Require(selected is >= MinimumResourceAliases and <= MaximumResourceAliases
                && aliases.Count(value => value == alias + NativeAliasSuffix) == NativeAliasCount
                && aliases.Where(value => value != alias).Distinct(StringComparer.Ordinal).Count() == aliases.Length - selected
                && aliases.All(value => value == alias || value == alias + NativeAliasSuffix || value == Name || value == Id
                    || value == Id[..MongoNativeReadinessRegressionProtocol.ShortContainerCharacters]));
        }
    }

    internal void RequireChild(string name, string image, string network, IReadOnlyDictionary<string, string> labels)
    {
        MongoNativeReadinessRegressionProtocol.Require(Name == name && Image == image
            && Networks.Keys.SequenceEqual([network], StringComparer.Ordinal)
            && labels.All(item => Labels.TryGetValue(item.Key, out var observed) && observed == item.Value));
    }

    private static Dictionary<string, string[]> ReadNetworks(JsonElement value)
    {
        var networks = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var item in value.EnumerateObject())
        {
            MongoNativeReadinessRegressionProtocol.Require(MongoNativeReadinessRegressionProtocol.Identifier(item.Name));
            var aliases = item.Value.GetProperty(AliasesField);
            var observed = aliases.ValueKind == JsonValueKind.Null ? [] : aliases.EnumerateArray().Select(alias => alias.GetString()!).ToArray();
            MongoNativeReadinessRegressionProtocol.Require(observed.All(alias => alias is not null
                && MongoNativeReadinessRegressionProtocol.Identifier(alias)) && networks.TryAdd(item.Name, observed));
        }
        return networks;
    }
}
