using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace KeyLoad;

/// <summary>Defines a database resource and its transaction and feature policies.</summary>
/// <param name="Name">Provides the resource or index name.</param>
/// <param name="Kind">Identifies the trusted operation kind.</param>
/// <param name="TransactionDomainId">Identifies the transaction domain.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ContractAliases.ResourceDefinition)]
public sealed record ResourceDefinition([property: Orleans.Id(0)] string Name, [property: Orleans.Id(1)] ResourceKind Kind, [property: Orleans.Id(2)] string TransactionDomainId)
{
    private const int InitialSchemaVersion = 1;

    /// <summary>Gets the indexes maintained for the resource.</summary>
    [Orleans.Id(3)]
    public ImmutableArray<IndexDefinition> Indexes { get; init; } = [];
    /// <summary>Gets the field policies for the resource.</summary>
    [Orleans.Id(4)]
    public ImmutableArray<SensitiveFieldPolicy> FieldPolicies { get; init; } = [];
    /// <summary>Gets the header policies for the resource.</summary>
    [Orleans.Id(5)]
    public ImmutableArray<SensitiveFieldPolicy> HeaderPolicies { get; init; } = [];
    /// <summary>Gets the queue limits for the resource.</summary>
    [Orleans.Id(6)]
    public QueuePolicy QueuePolicy { get; init; } = new();
    /// <summary>Gets the event retention policy for the resource.</summary>
    [Orleans.Id(7)]
    public EventRetentionPolicy EventRetention { get; init; } = new();
    /// <summary>Gets the canonical state authority.</summary>
    [Orleans.Id(8)]
    public DocumentAuthority Authority
    {
        get; init;
    }
    /// <summary>Gets the resource schema version.</summary>
    [Orleans.Id(9)]
    public long SchemaVersion { get; init; } = InitialSchemaVersion;
    /// <summary>Gets whether dispatch is paused.</summary>
    [Orleans.Id(10)]
    public bool Paused
    {
        get; init;
    }
    /// <summary>Gets an optional closed typed-row schema for a document-authority collection.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [Orleans.Id(12)]
    public RelationalSchema? RelationalSchema
    {
        get; init;
    }
    /// <summary>Gets immutable authoritative vector profiles for configured collection fields.</summary>
    [Orleans.Id(13)]
    public ImmutableArray<VectorFieldProfile> VectorProfiles { get; init; } = [];
    /// <summary>Gets explicit retained target-inbox sublimits; null disables target processing.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [Orleans.Id(14)]
    public InboxPolicy? InboxPolicy
    {
        get; init;
    }
    /// <summary>Gets the closed user analytics dimension and metric schema.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [Orleans.Id(15)]
    public AnalyticsStoreSchema? AnalyticsSchema
    {
        get; init;
    }
}
