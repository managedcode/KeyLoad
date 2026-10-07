using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using KeyLoad.Core;

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
        var redacted = leftSafe.Redacted || rightSafe.Redacted;
        var fields = leftSafe.RedactedFields.Select(field => FieldPath(leftAlias, field))
            .Concat(rightSafe.RedactedFields.Select(field => FieldPath(rightAlias, field))).ToImmutableArray();
        var sources = ImmutableArray.Create(new QueryRowSource(leftAlias, left.Reference.Id, left.Revision),
            new QueryRowSource(rightAlias, right.Reference.Id, right.Revision));
        return new(left.Reference.Id, left.Revision, json.ToJsonString(), redacted, fields, sources);
    }

    private static string FieldPath(string alias, string path) => alias + SourceSeparator + path.TrimStart(LeadingPathSeparator);
}
