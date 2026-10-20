using KeyLoad.Core.Features.AnalyticsStore;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes AnalyticsStoreCommands operations against the caller's database context.</summary>
public static class AnalyticsStoreCommands
{
    internal const long AnalyticsMinimumExpectedRevision = 0;
    internal const int AnalyticsEmptyRevisionCount = 0;
    internal static void AuthorizeAnalyticsMutation(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, PartitionRef partition, Mutation mutation)
    {
        engine.Authorization.Require(principal, partition, mutation.Resource, Capability.AnalyticsWrite);
        var resource = engine.Resource(view, partition, mutation.Resource, ResourceKind.AnalyticsStore);
        AnalyticsStoreValidation.ValidateSchema(resource);
        AnalyticsStoreAuthorization.WriteFields(engine, principal, resource);
        if (mutation is UpsertAnalyticsEvents upsert)
        {
            engine.AuthorizeAnalyticsUpsert(view, principal, partition, upsert, resource.AnalyticsSchema!);
        }
        else if (mutation is DeleteAnalyticsEvents delete)
        {
            engine.AuthorizeAnalyticsDelete(view, principal, partition, delete);
        }
        else
        {
            throw Errors.Fail(ErrorCode.Validation, AnalyticsStoreValidation.Invalid);
        }
    }

    internal static void AuthorizeAnalyticsUpsert(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, PartitionRef partition, UpsertAnalyticsEvents upsert, AnalyticsStoreSchema schema)
    {
        engine.ValidateAnalyticsUpsert(upsert, schema);
        foreach (var value in upsert.Events)
        {
            var previous = AnalyticsStoreAuthorization.Previous(view, partition, upsert.Store, value.Id);
            if (previous is not null)
            {
                engine.Authorization.RequireWriteRow(principal, previous.Event.RowAccess ?? new());
            }

            engine.Authorization.RequireWriteRow(principal, value.RowAccess ?? previous?.Event.RowAccess ?? new());
            AnalyticsStoreLinks.Require(engine, view, principal, partition, value.Links);
        }
    }

    internal static void AuthorizeAnalyticsDelete(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, PartitionRef partition, DeleteAnalyticsEvents delete)
    {
        engine.ValidateAnalyticsDelete(delete);
        foreach (var id in delete.ExpectedRevisions.Keys)
        {
            var previous = AnalyticsStoreAuthorization.Previous(view, partition, delete.Store, id);
            if (previous is not null)
            {
                engine.Authorization.RequireWriteRow(principal, previous.Event.RowAccess ?? new());
            }
        }
    }

    internal static void ValidateAnalyticsUpsert(this DatabaseEngine engine, UpsertAnalyticsEvents mutation, AnalyticsStoreSchema schema)
    {
        if (mutation.Events.IsDefaultOrEmpty || mutation.ExpectedRevisions is null || mutation.Events.Length > engine.Limits.MaxBatchMutations || mutation.Events.Any(value => value is null) || mutation.Events.Select(value => value.Id).Distinct(StringComparer.Ordinal).Count() != mutation.Events.Length || !mutation.Events.Select(value => value.Id).Order(StringComparer.Ordinal).SequenceEqual(mutation.ExpectedRevisions.Keys.Order(StringComparer.Ordinal)) || mutation.ExpectedRevisions.Values.Any(value => value < AnalyticsMinimumExpectedRevision))
        {
            throw Errors.Fail(ErrorCode.Validation, AnalyticsStoreValidation.Invalid);
        }

        foreach (var value in mutation.Events)
        {
            AnalyticsStoreValidation.Event(value, schema, engine.Limits);
        }
    }

    internal static void ValidateAnalyticsDelete(this DatabaseEngine engine, DeleteAnalyticsEvents mutation)
    {
        if (mutation.ExpectedRevisions is null || mutation.ExpectedRevisions.Count == AnalyticsEmptyRevisionCount || mutation.ExpectedRevisions.Count > engine.Limits.MaxBatchMutations || mutation.ExpectedRevisions.Values.Any(value => value <= AnalyticsMinimumExpectedRevision))
        {
            throw Errors.Fail(ErrorCode.Validation, AnalyticsStoreValidation.Invalid);
        }

        foreach (var id in mutation.ExpectedRevisions.Keys)
        {
            JsonData.Identifier(id);
        }
    }
}
