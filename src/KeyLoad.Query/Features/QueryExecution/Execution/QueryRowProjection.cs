using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using KeyLoad.Core;
using KeyLoad.Storage;

namespace KeyLoad.Query.Features.QueryExecution;

/// <summary>Projects persisted authorized fields into the caller's exact selected row shape.</summary>
internal static class QueryRowProjection
{
    private const int SingleSelection = 1;
    private const int FirstSelectionIndex = 0;

    internal static QueryRow Project(DatabaseEngine database, PrincipalRecord principal, ResourceDefinition resource, DocumentRecord document,
        ImmutableArray<Selection> selections,
        IReadOnlyDictionary<string, string[]>? paths = null)
    {
        var safe = database.Project(principal, resource, document);
        if (selections.Length == SingleSelection && selections[FirstSelectionIndex].Path == SqlSyntax.Star)
        {
            return new(document.Reference.Id, document.Revision, safe.Json, safe.Redacted, safe.RedactedFields);
        }
        using var json = JsonDocument.Parse(safe.Json);
        var result = new JsonObject();
        foreach (var selection in selections)
        {
            var value = PredicateEvaluator.FieldValue(selection.Path, document, json.RootElement, paths);
            result[selection.Alias] = value is MissingValue ? null : JsonSerializer.SerializeToNode(value, JsonDefaults.Options);
        }
        return new(document.Reference.Id, document.Revision, result.ToJsonString(), safe.Redacted, safe.RedactedFields);
    }
}
