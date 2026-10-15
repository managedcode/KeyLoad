using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using KeyLoad.Core;
using KeyLoad.Query.Features.QueryExecution;

namespace KeyLoad.Query;

/// <summary>Projects a bounded join pair through each resource's persisted field policy.</summary>
internal static class RelationalInnerJoinProjection
{
    private const int FirstElementIndex = 0;
    private const string SourceSeparator = ".";
    private const char LeadingPathSeparator = '/';

    internal static QueryRow Project(DatabaseEngine database, PrincipalRecord principal, ResourceDefinition leftResource,
        ResourceDefinition rightResource, string leftAlias, string rightAlias, DocumentRecord left, DocumentRecord right,
        ImmutableArray<Selection> selections)
    {
        var leftSafe = database.Project(principal, leftResource, left);
        var rightSafe = database.Project(principal, rightResource, right);
        using var leftJson = JsonDocument.Parse(leftSafe.Json);
        using var rightJson = JsonDocument.Parse(rightSafe.Json);
        var json = new JsonObject();
        foreach (var selection in selections)
        {
            var root = selection.SourceAlias == leftAlias ? leftJson.RootElement : rightJson.RootElement;
            if (root.TryGetProperty(JsonData.PathSegments(selection.Path)[FirstElementIndex], out var value))
            {
                json[selection.Alias] = JsonSerializer.SerializeToNode(value, JsonDefaults.Options);
            }
            else
            {
                json[selection.Alias] = null;
            }
        }
        var leftFields = QueryProjectionRedactions.Selected(database, leftResource, leftSafe, selections, leftAlias);
        var rightFields = QueryProjectionRedactions.Selected(database, rightResource, rightSafe, selections, rightAlias);
        var fields = leftFields.Select(field => FieldPath(leftAlias, field))
            .Concat(rightFields.Select(field => FieldPath(rightAlias, field))).ToImmutableArray();
        var redacted = !fields.IsEmpty;
        var sources = ImmutableArray.Create(new QueryRowSource(leftAlias, left.Reference.Id, left.Revision),
            new QueryRowSource(rightAlias, right.Reference.Id, right.Revision));
        return new(left.Reference.Id, left.Revision, json.ToJsonString(), redacted, fields, sources);
    }

    private static string FieldPath(string alias, string path) => alias + SourceSeparator + path.TrimStart(LeadingPathSeparator);
}
