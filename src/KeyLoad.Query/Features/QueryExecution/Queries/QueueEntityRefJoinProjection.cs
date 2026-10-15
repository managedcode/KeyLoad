using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using KeyLoad.Core;
using KeyLoad.Storage;

namespace KeyLoad.Query.Features.QueryExecution;

/// <summary>Combines only separately authorized native projections from the same committed view.</summary>
internal static class QueueEntityRefJoinProjection
{
    private const char LeadingSeparator = '/';
    private const string SourceSeparator = ".";

    internal static QueryRow Project(DatabaseEngine database, PrincipalRecord principal,
        ResourceDefinition queue, ResourceDefinition target, SelectQuery query,
        DocumentRecord left, DocumentRecord right)
    {
        var leftSafe = database.Project(principal, queue, left);
        var rightSafe = database.Project(principal, target, right);
        using var leftJson = JsonDocument.Parse(leftSafe.Json);
        using var rightJson = JsonDocument.Parse(rightSafe.Json);
        var json = new JsonObject();
        foreach (var selection in query.Projection)
        {
            var root = selection.SourceAlias == query.Alias ? leftJson.RootElement : rightJson.RootElement;
            var value = JsonData.Scalar(root, selection.Path);
            json[selection.Alias] = value is MissingValue ? null : JsonSerializer.SerializeToNode(value, JsonDefaults.Options);
        }
        var leftFields = QueryProjectionRedactions.Selected(database, queue, leftSafe, query.Projection, query.Alias);
        var rightFields = QueryProjectionRedactions.Selected(database, target, rightSafe, query.Projection, query.InnerJoin?.Alias);
        var fields = leftFields.Select(field => Field(query.Alias!, field))
            .Concat(rightFields.Select(field => Field(query.InnerJoin!.Alias, field))).ToImmutableArray();
        var sources = ImmutableArray.Create(new QueryRowSource(query.Alias!, left.Reference.Id, left.Revision),
            new QueryRowSource(query.InnerJoin!.Alias, right.Reference.Id, right.Revision));
        return new(left.Reference.Id, left.Revision, json.ToJsonString(), !fields.IsEmpty, fields, sources);
    }

    private static string Field(string alias, string field) => alias + SourceSeparator + field.TrimStart(LeadingSeparator);
}
