using System.Collections.Immutable;
using KeyLoad.Core.Features.Authorization;
using KeyLoad.Core.Features.DocumentStorage;

namespace KeyLoad.Core;
/// <summary>Executes DocumentAuthorityIntersection operations against the caller's database context.</summary>
public static class DocumentAuthorityIntersection
{
    internal static DocumentProjection ProjectWithAuthorityIntersection(this DatabaseEngine engine, PrincipalRecord sourcePrincipal, ResourceDefinition sourceResource, PrincipalRecord targetPrincipal, ResourceDefinition targetResource, DocumentRecord record)
    {
        var target = engine.ProjectWithLineage(targetPrincipal, targetResource, record);
        var source = engine.ProjectWithLineage(sourcePrincipal, sourceResource, record with { Json = target.Value.Json });
        var omitted = target.CompleteOmittedPaths.Concat(source.CompleteOmittedPaths).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var targetVisible = FieldRedactionMetadata.Visible(engine.Authorization, targetPrincipal, targetResource, omitted);
        var sourceVisible = FieldRedactionMetadata.Visible(engine.Authorization, sourcePrincipal, sourceResource, omitted);
        var visible = targetVisible.Intersect(sourceVisible, StringComparer.Ordinal).ToImmutableArray();
        return new(new DocumentResult(record.Reference, record.Revision, source.Value.Json, target.Value.Redacted || source.Value.Redacted, visible), [.. omitted]);
    }
}
