using Microsoft.Extensions.Options;
using System.Collections.Immutable;
using ManagedCode.MCPGateway;

namespace KeyLoad.Server;

/// <summary>Validates only trusted static catalog metadata before constructing the graph.</summary>
internal static class McpGatewayCatalogValidation
{
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

    internal static ImmutableArray<McpGatewayCatalogEntry> CreateEntries(IReadOnlyList<McpOperationDescriptor> operations, IOptions<McpExecutionOptions> executionOptions)
    {
        const int OperationsCountEmptyCount = 0;

        ArgumentNullException.ThrowIfNull(operations);
        if (operations.Count == OperationsCountEmptyCount || operations.Count > executionOptions.Value.MaximumCatalogOperations)
        {
            throw new InvalidOperationException(MetadataBoundFailure);
        }

        var entries = ImmutableArray.CreateBuilder<McpGatewayCatalogEntry>(operations.Count);
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var operation in operations)
        {
            if (!names.Add(operation.Name) || operation.Name.Length > executionOptions.Value.MaximumOperationNameCharacters
                || operation.Description.Length > executionOptions.Value.MaximumDescriptorCharacters
                || operation.InputSchema.ValueKind != System.Text.Json.JsonValueKind.Object
                || operation.OutputSchema.ValueKind != System.Text.Json.JsonValueKind.Object)
            {
                throw new InvalidOperationException(MetadataBoundFailure);
            }

            entries.Add(new McpGatewayCatalogEntry(operation, CreateHints(operation: operation, executionOptions: executionOptions)));
        }

        var result = entries.MoveToImmutable();
        McpGatewayMetadataSizer.Validate(result, executionOptions);
        return result;
    }

    private static McpGatewayToolSearchHints CreateHints(McpOperationDescriptor operation, IOptions<McpExecutionOptions> executionOptions)
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
        }.Where(value => value.Length > ValueLengthEmptyCount && value.Length <= executionOptions.Value.MaximumAliasCharacters)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(executionOptions.Value.MaximumAliasesPerOperation)
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
