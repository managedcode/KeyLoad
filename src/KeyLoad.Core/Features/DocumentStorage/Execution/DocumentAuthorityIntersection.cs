using System.Collections.Immutable;
using KeyLoad.Core.Features.Authorization;
using KeyLoad.Core.Features.DocumentStorage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal DocumentProjection ProjectWithAuthorityIntersection(PrincipalRecord sourcePrincipal,
        ResourceDefinition sourceResource, PrincipalRecord targetPrincipal, ResourceDefinition targetResource,
        DocumentRecord record)
    {
        var target = ProjectWithLineage(targetPrincipal, targetResource, record);
        var source = ProjectWithLineage(sourcePrincipal, sourceResource, record with
        {
            Json = target.Value.Json
        });
        var omitted = target.CompleteOmittedPaths.Concat(source.CompleteOmittedPaths)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var targetVisible = FieldRedactionMetadata.Visible(Authorization, targetPrincipal, targetResource, omitted);
        var sourceVisible = FieldRedactionMetadata.Visible(Authorization, sourcePrincipal, sourceResource, omitted);
        var visible = targetVisible.Intersect(sourceVisible, StringComparer.Ordinal).ToImmutableArray();
        return new(new DocumentResult(record.Reference, record.Revision, source.Value.Json,
            target.Value.Redacted || source.Value.Redacted, visible), [.. omitted]);
    }
}
