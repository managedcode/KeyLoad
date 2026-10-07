using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace KeyLoad;

/// <summary>Identifies a document or graph endpoint within an atomic partition.</summary>
/// <param name="Partition">Identifies the atomic partition containing the resource.</param>
/// <param name="Collection">Identifies the document collection.</param>
/// <param name="Id">Identifies the entity, document, key, message, event, or operation.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.EntityRef)]
public sealed record EntityRef([property: Orleans.Id(0)] PartitionRef Partition, [property: Orleans.Id(1)] string Collection, [property: Orleans.Id(2)] string Id);

/// <summary>Identifies the kind of resource configured in a database.</summary>
public enum ResourceKind
{
    /// <summary>A document collection.</summary>
    Collection,
    /// <summary>An event stream set.</summary>
    StreamSet,
    /// <summary>A work queue.</summary>
    WorkQueue,
    /// <summary>A publish-subscribe topic.</summary>
    Topic,
    /// <summary>A graph resource.</summary>
    Graph,
    /// <summary>A time-series resource.</summary>
    TimeSeries,
    /// <summary>A canonical chunked binary object resource.</summary>
    BlobStore
}

/// <summary>Selects which source owns a resource’s canonical document state.</summary>
public enum DocumentAuthority
{
    /// <summary>Documents are the canonical state.</summary>
    Document,
    /// <summary>The event stream is the canonical state.</summary>
    EventStream
}

/// <summary>Defines a named index over document fields.</summary>
/// <param name="Name">Provides the resource or index name.</param>
/// <param name="Fields">Lists indexed field paths.</param>
/// <param name="Unique">Requires indexed values to be unique when true.</param>
/// <param name="IncludeNull">Includes explicit null values when true.</param>
/// <param name="IncludeMissing">Includes missing field values when true.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.IndexDefinition)]
public sealed record IndexDefinition([property: Orleans.Id(0)] string Name, [property: Orleans.Id(1)] ImmutableArray<string> Fields, [property: Orleans.Id(2)] bool Unique = false, [property: Orleans.Id(3)] bool IncludeNull = true, [property: Orleans.Id(4)] bool IncludeMissing = false);

/// <summary>Defines classification and grants for a sensitive field or header.</summary>
/// <param name="Path">Identifies a field path.</param>
/// <param name="Classification">Names the data classification.</param>
/// <param name="RawReadGrant">Names the grant required to read raw values.</param>
/// <param name="RawUseGrant">Names the grant required to use raw values.</param>
/// <param name="WriteGrant">Names the grant required to write values.</param>
/// <param name="RequiredForProcessing">Requires this field for processing when true.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.SensitiveFieldPolicy)]
public sealed record SensitiveFieldPolicy([property: Orleans.Id(0)] string Path, [property: Orleans.Id(1)] string Classification, [property: Orleans.Id(2)] string RawReadGrant = SensitiveFieldPolicy.DefaultRawReadGrant,
    [property: Orleans.Id(3)] string RawUseGrant = SensitiveFieldPolicy.DefaultRawUseGrant, [property: Orleans.Id(4)] string WriteGrant = SensitiveFieldPolicy.DefaultWriteGrant, [property: Orleans.Id(5)] bool RequiredForProcessing = false)
{
    private const string DefaultRawReadGrant = "pii.read";
    private const string DefaultRawUseGrant = "pii.use";
    private const string DefaultWriteGrant = "pii.write";
}

/// <summary>Defines a database resource and its transaction and feature policies.</summary>
/// <param name="Name">Provides the resource or index name.</param>
/// <param name="Kind">Identifies the trusted operation kind.</param>
/// <param name="TransactionDomainId">Identifies the transaction domain.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.ResourceDefinition)]
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
    public DocumentAuthority Authority { get; init; }
    /// <summary>Gets the resource schema version.</summary>
    [Orleans.Id(9)]
    public long SchemaVersion { get; init; } = InitialSchemaVersion;
    /// <summary>Gets whether dispatch is paused.</summary>
    [Orleans.Id(10)]
    public bool Paused { get; init; }
    /// <summary>Gets optional version-one binary limits; null selects immutable blob defaults.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [Orleans.Id(11)]
    public BlobPolicy? BlobPolicy { get; init; }
    /// <summary>Gets an optional closed typed-row schema for a document-authority collection.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [Orleans.Id(12)]
    public RelationalSchema? RelationalSchema { get; init; }
    /// <summary>Gets immutable authoritative vector profiles for configured collection fields.</summary>
    [Orleans.Id(13)]
    public ImmutableArray<VectorFieldProfile> VectorProfiles { get; init; } = [];
}

/// <summary>Describes row ownership and project metadata used by row-level access policy.</summary>
/// <param name="OwnerId">Identifies the principal owner.</param>
/// <param name="ProjectId">Identifies the optional project.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.RowAccess)]
public sealed record RowAccess([property: Orleans.Id(0)] string? OwnerId = null, [property: Orleans.Id(1)] string? ProjectId = null);

/// <summary>Represents a persisted document revision and its access metadata.</summary>
/// <param name="Reference">Identifies the document.</param>
/// <param name="Revision">Identifies the document, stream, or edge revision.</param>
/// <param name="Json">Contains the serialized operation result.</param>
/// <param name="Access">Provides optional row access metadata.</param>
/// <param name="UpdatedAt">Records the update time.</param>
/// <param name="Deleted">Marks a tombstone when true.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.DocumentRecord)]
public sealed record DocumentRecord([property: Orleans.Id(0)] EntityRef Reference, [property: Orleans.Id(1)] long Revision, [property: Orleans.Id(2)] string Json, [property: Orleans.Id(3)] RowAccess Access,
    [property: Orleans.Id(4)] DateTimeOffset UpdatedAt, [property: Orleans.Id(5)] bool Deleted = false);

/// <summary>Returns a document revision with redaction metadata for the caller.</summary>
/// <param name="Reference">Identifies the document.</param>
/// <param name="Revision">Identifies the document, stream, or edge revision.</param>
/// <param name="Json">Contains the serialized operation result.</param>
/// <param name="Redacted">Indicates whether the document was redacted.</param>
/// <param name="RedactedFields">Lists fields removed from the result.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.DocumentResult)]
public sealed record DocumentResult([property: Orleans.Id(0)] EntityRef Reference, [property: Orleans.Id(1)] long Revision, [property: Orleans.Id(2)] string Json, [property: Orleans.Id(3)] bool Redacted, [property: Orleans.Id(4)] ImmutableArray<string> RedactedFields);

/// <summary>Creates or replaces a document in a collection.</summary>
/// <param name="Collection">Identifies the document collection.</param>
/// <param name="Id">Identifies the entity, document, key, message, event, or operation.</param>
/// <param name="Json">Contains the serialized operation result.</param>
/// <param name="ExpectedRevision">Sets the optional revision precondition.</param>
/// <param name="Access">Provides optional row access metadata.</param>
/// <param name="ExplicitReplacement">Allows an explicit replacement when true.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.PutDocument)]
public sealed record PutDocument([property: Orleans.Id(0)] string Collection, [property: Orleans.Id(1)] string Id, [property: Orleans.Id(2)] string Json, [property: Orleans.Id(3)] long? ExpectedRevision = null,
    [property: Orleans.Id(4)] RowAccess? Access = null, [property: Orleans.Id(5)] bool ExplicitReplacement = false) : Mutation(Collection);

/// <summary>Selects whether a field patch sets or removes a value.</summary>
public enum PatchKind
{
    /// <summary>Set the field to the supplied JSON value.</summary>
    Set,
    /// <summary>Remove the field from the document.</summary>
    Remove
}

/// <summary>Describes a set or removal operation for one document field path.</summary>
/// <param name="Path">Identifies a field path.</param>
/// <param name="Kind">Identifies the trusted operation kind.</param>
/// <param name="ValueJson">Contains the JSON value for a set operation.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.FieldPatch)]
public sealed record FieldPatch([property: Orleans.Id(0)] string Path, [property: Orleans.Id(1)] PatchKind Kind, [property: Orleans.Id(2)] string? ValueJson = null);

/// <summary>Applies field patches to a document at an expected revision.</summary>
/// <param name="Collection">Identifies the document collection.</param>
/// <param name="Id">Identifies the entity, document, key, message, event, or operation.</param>
/// <param name="Patches">Lists field changes to apply.</param>
/// <param name="ExpectedRevision">Sets the optional revision precondition.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.PatchDocument)]
public sealed record PatchDocument([property: Orleans.Id(0)] string Collection, [property: Orleans.Id(1)] string Id, [property: Orleans.Id(2)] ImmutableArray<FieldPatch> Patches, [property: Orleans.Id(3)] long ExpectedRevision) : Mutation(Collection);

/// <summary>Deletes a document, optionally conditional on its expected revision.</summary>
/// <param name="Collection">Identifies the document collection.</param>
/// <param name="Id">Identifies the entity, document, key, message, event, or operation.</param>
/// <param name="ExpectedRevision">Sets the optional revision precondition.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.DeleteDocument)]
public sealed record DeleteDocument([property: Orleans.Id(0)] string Collection, [property: Orleans.Id(1)] string Id, [property: Orleans.Id(2)] long? ExpectedRevision = null) : Mutation(Collection);

/// <summary>Requests configuration of a resource for a tenant and database.</summary>
/// <param name="TenantId">Identifies the owning tenant.</param>
/// <param name="DatabaseId">Identifies the database.</param>
/// <param name="Definition">Provides the resource configuration.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.ConfigureResourceRequest)]
public sealed record ConfigureResourceRequest([property: Orleans.Id(0)] string TenantId, [property: Orleans.Id(1)] string DatabaseId, [property: Orleans.Id(2)] ResourceDefinition Definition)
{
    /// <summary>Gets the existing resource version required for a policy-only replacement.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [Orleans.Id(3)]
    public long? ExpectedSchemaVersion { get; init; }
}
