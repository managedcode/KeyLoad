using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace KeyLoad;

/// <summary>Identifies a document or graph endpoint within an atomic partition.</summary>
/// <param name="Partition">Identifies the atomic partition containing the resource.</param>
/// <param name="Collection">Identifies the document collection.</param>
/// <param name="Id">Identifies the entity, document, key, message, event, or operation.</param>
public sealed record EntityRef(PartitionRef Partition, string Collection, string Id);

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
public sealed record IndexDefinition(string Name, ImmutableArray<string> Fields, bool Unique = false, bool IncludeNull = true, bool IncludeMissing = false);

/// <summary>Defines classification and grants for a sensitive field or header.</summary>
/// <param name="Path">Identifies a field path.</param>
/// <param name="Classification">Names the data classification.</param>
/// <param name="RawReadGrant">Names the grant required to read raw values.</param>
/// <param name="RawUseGrant">Names the grant required to use raw values.</param>
/// <param name="WriteGrant">Names the grant required to write values.</param>
/// <param name="RequiredForProcessing">Requires this field for processing when true.</param>
public sealed record SensitiveFieldPolicy(string Path, string Classification, string RawReadGrant = "pii.read",
    string RawUseGrant = "pii.use", string WriteGrant = "pii.write", bool RequiredForProcessing = false);

/// <summary>Defines a database resource and its transaction and feature policies.</summary>
/// <param name="Name">Provides the resource or index name.</param>
/// <param name="Kind">Identifies the trusted operation kind.</param>
/// <param name="TransactionDomainId">Identifies the transaction domain.</param>
public sealed record ResourceDefinition(string Name, ResourceKind Kind, string TransactionDomainId)
{
    /// <summary>Gets the indexes maintained for the resource.</summary>
    public ImmutableArray<IndexDefinition> Indexes { get; init; } = [];
    /// <summary>Gets the field policies for the resource.</summary>
    public ImmutableArray<SensitiveFieldPolicy> FieldPolicies { get; init; } = [];
    /// <summary>Gets the header policies for the resource.</summary>
    public ImmutableArray<SensitiveFieldPolicy> HeaderPolicies { get; init; } = [];
    /// <summary>Gets the queue limits for the resource.</summary>
    public QueuePolicy QueuePolicy { get; init; } = new();
    /// <summary>Gets the event retention policy for the resource.</summary>
    public EventRetentionPolicy EventRetention { get; init; } = new();
    /// <summary>Gets the canonical state authority.</summary>
    public DocumentAuthority Authority { get; init; }
    /// <summary>Gets the resource schema version.</summary>
    public long SchemaVersion { get; init; } = 1;
    /// <summary>Gets whether dispatch is paused.</summary>
    public bool Paused { get; init; }
    /// <summary>Gets optional version-one binary limits; null selects immutable blob defaults.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BlobPolicy? BlobPolicy { get; init; }
}

/// <summary>Describes row ownership and project metadata used by row-level access policy.</summary>
/// <param name="OwnerId">Identifies the principal owner.</param>
/// <param name="ProjectId">Identifies the optional project.</param>
public sealed record RowAccess(string? OwnerId = null, string? ProjectId = null);

/// <summary>Represents a persisted document revision and its access metadata.</summary>
/// <param name="Reference">Identifies the document.</param>
/// <param name="Revision">Identifies the document, stream, or edge revision.</param>
/// <param name="Json">Contains the serialized operation result.</param>
/// <param name="Access">Provides optional row access metadata.</param>
/// <param name="UpdatedAt">Records the update time.</param>
/// <param name="Deleted">Marks a tombstone when true.</param>
public sealed record DocumentRecord(EntityRef Reference, long Revision, string Json, RowAccess Access,
    DateTimeOffset UpdatedAt, bool Deleted = false);

/// <summary>Returns a document revision with redaction metadata for the caller.</summary>
/// <param name="Reference">Identifies the document.</param>
/// <param name="Revision">Identifies the document, stream, or edge revision.</param>
/// <param name="Json">Contains the serialized operation result.</param>
/// <param name="Redacted">Indicates whether the document was redacted.</param>
/// <param name="RedactedFields">Lists fields removed from the result.</param>
public sealed record DocumentResult(EntityRef Reference, long Revision, string Json, bool Redacted, ImmutableArray<string> RedactedFields);

/// <summary>Creates or replaces a document in a collection.</summary>
/// <param name="Collection">Identifies the document collection.</param>
/// <param name="Id">Identifies the entity, document, key, message, event, or operation.</param>
/// <param name="Json">Contains the serialized operation result.</param>
/// <param name="ExpectedRevision">Sets the optional revision precondition.</param>
/// <param name="Access">Provides optional row access metadata.</param>
/// <param name="ExplicitReplacement">Allows an explicit replacement when true.</param>
public sealed record PutDocument(string Collection, string Id, string Json, long? ExpectedRevision = null,
    RowAccess? Access = null, bool ExplicitReplacement = false) : Mutation(Collection);

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
public sealed record FieldPatch(string Path, PatchKind Kind, string? ValueJson = null);

/// <summary>Applies field patches to a document at an expected revision.</summary>
/// <param name="Collection">Identifies the document collection.</param>
/// <param name="Id">Identifies the entity, document, key, message, event, or operation.</param>
/// <param name="Patches">Lists field changes to apply.</param>
/// <param name="ExpectedRevision">Sets the optional revision precondition.</param>
public sealed record PatchDocument(string Collection, string Id, ImmutableArray<FieldPatch> Patches, long ExpectedRevision) : Mutation(Collection);

/// <summary>Deletes a document, optionally conditional on its expected revision.</summary>
/// <param name="Collection">Identifies the document collection.</param>
/// <param name="Id">Identifies the entity, document, key, message, event, or operation.</param>
/// <param name="ExpectedRevision">Sets the optional revision precondition.</param>
public sealed record DeleteDocument(string Collection, string Id, long? ExpectedRevision = null) : Mutation(Collection);

/// <summary>Requests configuration of a resource for a tenant and database.</summary>
/// <param name="TenantId">Identifies the owning tenant.</param>
/// <param name="DatabaseId">Identifies the database.</param>
/// <param name="Definition">Provides the resource configuration.</param>
public sealed record ConfigureResourceRequest(string TenantId, string DatabaseId, ResourceDefinition Definition);
