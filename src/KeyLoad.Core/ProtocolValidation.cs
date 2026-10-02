namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private static void ValidatePrincipalStructure(PrincipalRecord principal)
    {
        if (principal.Grants.IsDefault || principal.FieldGrants.IsDefault || principal.Projects.IsDefault
            || principal.Grants.Length > 256 || principal.FieldGrants.Length > 256 || principal.Projects.Length > 256
            || principal.Grants.Any(grant => grant is null || (grant.Capabilities & ~Capability.All) != 0))
        {
            throw Errors.Fail(ErrorCode.Validation, "The principal scope exceeds its budget or contains an invalid grant.");
        }

        foreach (var grant in principal.Grants)
        { JsonData.Identifier(grant.Database); JsonData.Identifier(grant.Resource); }
        foreach (var field in principal.FieldGrants)
        {
            JsonData.Identifier(field);
        }

        foreach (var project in principal.Projects)
        {
            JsonData.Identifier(project);
        }

        if (principal.OwnerId is { } owner)
        {
            JsonData.Identifier(owner);
        }
    }
    private static void ValidateMutationStructure(Mutation mutation)
    {
        if (mutation is null)
        {
            throw Errors.Fail(ErrorCode.Validation, MissingMutationEntryMessage);
        }

        var owner = mutation switch
        {
            PutDocument put => put.Collection,
            PatchDocument patch => patch.Collection,
            DeleteDocument delete => delete.Collection,
            AppendEvents append => append.StreamSet,
            PublishTopic topic => topic.Topic,
            EnqueueMessage message => message.Queue,
            UpsertEdge edge => edge.Graph,
            DeleteEdge edge => edge.Graph,
            AppendSamples samples => samples.SeriesSet,
            PutVector vector => vector.Collection,
            _ => throw Errors.Fail(ErrorCode.UnsupportedCapability, "The mutation is unsupported.")
        };
        if (mutation.Resource != owner)
        {
            throw Errors.Fail(ErrorCode.Validation, "The mutation resource and canonical owner differ.");
        }

        var invalid = mutation switch
        {
            PatchDocument patch => patch.Patches.IsDefault || patch.Patches.Any(item => item is null || item.Kind is not (PatchKind.Set or PatchKind.Remove)),
            AppendEvents append => append.Events.IsDefault || append.Events.Any(item => item is null)
                || append.ExpectedRevision.State is not (ExpectedStreamState.Any or ExpectedStreamState.Exact or ExpectedStreamState.NoStream),
            PublishTopic topic => topic.Events.IsDefault || topic.Events.Any(item => item is null),
            AppendSamples samples => samples.Samples.IsDefault || samples.Samples.Any(item => item is null),
            PutVector vector => vector.Values.IsDefault,
            _ => false
        };
        if (invalid)
        {
            throw Errors.Fail(ErrorCode.Validation, "A nested mutation entry or enum value is invalid.");
        }
    }
}
