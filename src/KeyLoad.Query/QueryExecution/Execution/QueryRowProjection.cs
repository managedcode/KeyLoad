using System.Buffers;
using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Core.DocumentStorage;

namespace KeyLoad.Query.QueryExecution;

/// <summary>Projects persisted authorized fields into the caller's exact selected row shape.</summary>
internal sealed class QueryRowProjection(DatabaseEngine database) : IDisposable
{
    private ArrayBufferWriter<byte>? buffer;
    private Utf8JsonWriter? writer;

    internal QueryRow Project(PrincipalRecord principal, ResourceDefinition resource, DocumentRecord document,
        ImmutableArray<Selection> selections,
        IReadOnlyDictionary<string, string[]>? paths = null)
    {
        var projection = database.ProjectWithLineage(principal, resource, document);
        return ProjectSelected(resource, document, selections, paths, projection);
    }

    internal QueryRow ProjectIntersection(PartitionQueryLifetimeMetadata metadata,
        PrincipalRecord principal, ResourceDefinition resource, DocumentRecord document,
        ImmutableArray<Selection> selections, IReadOnlyDictionary<string, string[]>? paths = null)
    {
        var projection = database.ProjectWithAuthorityIntersection(metadata.Principal, metadata.Definition,
            principal, resource, document);
        return ProjectSelected(resource, document, selections, paths, projection, metadata.Definition);
    }

    private QueryRow ProjectSelected(ResourceDefinition resource,
        DocumentRecord document, ImmutableArray<Selection> selections, IReadOnlyDictionary<string, string[]>? paths,
        DocumentProjection projection, ResourceDefinition? sourceResource = null)
    {
        var safe = projection.Value;
        if (selections.Length == 1 && selections[0].Path == SqlSyntax.Star)
        {
            return new(document.Reference.Id, document.Revision, safe.Json, safe.Redacted, safe.RedactedFields);
        }
        using var json = JsonDocument.Parse(safe.Json);
        var result = buffer ??= new();
        result.ResetWrittenCount();
        var output = writer ??= new(result);
        output.Reset(result);
        output.WriteStartObject();
        foreach (var selection in selections)
        {
            var value = PredicateEvaluator.FieldValue(selection.Path, document, json.RootElement, paths);
            output.WritePropertyName(selection.Alias);
            new JsonScalarValue(value).WriteTo(output);
        }
        output.WriteEndObject();
        output.Flush();
        var selected = QueryProjectionRedactions.Selected(database, resource, safe,
            projection.CompleteOmittedPaths, selections, additionalResource: sourceResource);
        return new(document.Reference.Id, document.Revision, Encoding.UTF8.GetString(result.WrittenSpan), selected.Redacted, selected.Fields);
    }

    public void Dispose() => writer?.Dispose();
}
