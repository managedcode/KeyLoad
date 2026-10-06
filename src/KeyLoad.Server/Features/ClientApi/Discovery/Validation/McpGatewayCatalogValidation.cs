using System.Collections.Immutable;
using ManagedCode.MCPGateway;

namespace KeyLoad.Server;

/// <summary>Validates only trusted static catalog metadata before constructing the graph.</summary>
internal static class McpGatewayCatalogValidation
{
    internal const int MaximumOperations = 256;
    internal const int MaximumMetadataBytes = 4 * 1024 * 1024;
    internal const int MaximumDescriptorTextLength = 4096;
    internal const int MaximumOperationNameLength = 64;
    internal const int MaximumAliasLength = 128;
    internal const int MaximumAliasesPerOperation = 4;
    internal const int NativeMaximumResults = 4;
    internal const int DefaultSearchResults = 3;
    internal const string MetadataBoundFailure = "The canonical MCP tool catalog exceeds its configured bound.";
    private const string AdminPrefix = "admin";
    private const string OperationPrefix = "keyload_";
    private const string ChangesPrefix = "changes";
    private const string DocumentsPrefix = "documents";
    private const string EventsPrefix = "events";
    private const string GraphPrefix = "graph";
    private const string MessagesPrefix = "messages";
    private const string ProjectionsPrefix = "projections";
    private const string QueryPrefix = "query";
    private const string ResourcesPrefix = "resources";
    private const string SearchPrefix = "search";
    private const string SeriesPrefix = "series";
    private const string StreamsPrefix = "streams";
    private const string AdministrationCategory = "administration";
    private const string ChangeFeedCategory = "change-feed";
    private const string DocumentsCategory = "documents";
    private const string EventsCategory = "events";
    private const string GraphCategory = "graph";
    private const string MessagingCategory = "messaging";
    private const string ProjectionsCategory = "projections";
    private const string SqlCategory = "sql";
    private const string ResourcesCategory = "resources";
    private const string SearchCategory = "search";
    private const string TimeSeriesCategory = "time-series";
    private const string EventStreamsCategory = "event-streams";

    internal static ImmutableArray<McpGatewayCatalogEntry> CreateEntries(
        IReadOnlyList<McpOperationDescriptor> operations)
    {
        const int OperationsCountEmptyCount = 0;

        ArgumentNullException.ThrowIfNull(operations);
        if (operations.Count is OperationsCountEmptyCount or > MaximumOperations)
        {
            throw new InvalidOperationException(MetadataBoundFailure);
        }

        var entries = ImmutableArray.CreateBuilder<McpGatewayCatalogEntry>(operations.Count);
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var operation in operations)
        {
            if (!names.Add(operation.Name) || operation.Name.Length > MaximumOperationNameLength
                || operation.Description.Length > MaximumDescriptorTextLength
                || operation.InputSchema.ValueKind != System.Text.Json.JsonValueKind.Object
                || operation.OutputSchema.ValueKind != System.Text.Json.JsonValueKind.Object)
            {
                throw new InvalidOperationException(MetadataBoundFailure);
            }

            entries.Add(new McpGatewayCatalogEntry(operation, CreateHints(operation)));
        }

        var result = entries.MoveToImmutable();
        McpGatewayMetadataSizer.Validate(result, MaximumMetadataBytes);
        return result;
    }

    private static McpGatewayToolSearchHints CreateHints(McpOperationDescriptor operation)
    {
        const char UnderscoreCharacter = '_';
        const char SpaceCharacter = ' ';
        const char SlashCharacter = '/';
        const int ValueLengthEmptyCount = 0;

        var category = ResolveCategory(operation.Name);
        var aliases = new[]
        {
            operation.Name.Replace(UnderscoreCharacter, SpaceCharacter),
            operation.Route.Trim(SlashCharacter).Replace(SlashCharacter, SpaceCharacter),
            category
        }.Where(static value => value.Length is > ValueLengthEmptyCount and <= MaximumAliasLength)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaximumAliasesPerOperation)
            .ToArray();

        return new McpGatewayToolSearchHints(
            Aliases: aliases,
            Keywords: [],
            Categories: [category],
            Tags: [],
            DataSources: [],
            UsageExamples: [],
            ReadOnly: operation.ReadOnly,
            Idempotent: operation.Idempotent,
            Destructive: operation.Destructive,
            OpenWorld: false,
            EnabledByDefault: true);
    }

    private static string ResolveCategory(string name)
    {
        const char UnderscoreCharacter = '_';
        const int SeparatorValidationBoundary = 0;

        name = name.StartsWith(OperationPrefix, StringComparison.Ordinal) ? name[OperationPrefix.Length..] : name;
        var separator = name.IndexOf(UnderscoreCharacter, StringComparison.Ordinal);
        var category = separator > SeparatorValidationBoundary ? name[..separator] : name;
        return category switch
        {
            AdminPrefix => AdministrationCategory,
            ChangesPrefix => ChangeFeedCategory,
            DocumentsPrefix => DocumentsCategory,
            EventsPrefix => EventsCategory,
            GraphPrefix => GraphCategory,
            MessagesPrefix => MessagingCategory,
            ProjectionsPrefix => ProjectionsCategory,
            QueryPrefix => SqlCategory,
            ResourcesPrefix => ResourcesCategory,
            SearchPrefix => SearchCategory,
            SeriesPrefix => TimeSeriesCategory,
            StreamsPrefix => EventStreamsCategory,
            _ => category
        };
    }
}
