using KeyLoad.Core.Features.AnalyticsStore;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const long AnalyticsMinimumExpectedRevision = 0;
    private const int AnalyticsEmptyRevisionCount = 0;
    internal void AuthorizeAnalyticsMutation(IKeyValueView view, PrincipalRecord principal,
        PartitionRef partition, Mutation mutation)
    {
        Authorization.Require(principal, partition, mutation.Resource, Capability.AnalyticsWrite);
        var resource = Resource(view, partition, mutation.Resource, ResourceKind.AnalyticsStore);
        AnalyticsStoreValidation.ValidateSchema(resource);
        AnalyticsStoreAuthorization.WriteFields(this, principal, resource);
        if (mutation is UpsertAnalyticsEvents upsert)
        {
            AuthorizeAnalyticsUpsert(view, principal, partition, upsert, resource.AnalyticsSchema!);
        }
        else if (mutation is DeleteAnalyticsEvents delete)
        {
            AuthorizeAnalyticsDelete(view, principal, partition, delete);
        }
        else
        {
            throw Errors.Fail(ErrorCode.Validation, AnalyticsStoreValidation.Invalid);
        }
    }

    private void AuthorizeAnalyticsUpsert(IKeyValueView view, PrincipalRecord principal,
        PartitionRef partition, UpsertAnalyticsEvents upsert, AnalyticsStoreSchema schema)
    {
        ValidateAnalyticsUpsert(upsert, schema);
        foreach (var value in upsert.Events)
        {
            var previous = AnalyticsStoreAuthorization.Previous(view, partition, upsert.Store, value.Id);
            if (previous is not null)
            {
                Authorization.RequireWriteRow(principal, previous.Event.RowAccess ?? new());
            }

            Authorization.RequireWriteRow(principal, value.RowAccess ?? previous?.Event.RowAccess ?? new());
            AnalyticsStoreLinks.Require(this, view, principal, partition, value.Links);
        }
    }

    private void AuthorizeAnalyticsDelete(IKeyValueView view, PrincipalRecord principal,
        PartitionRef partition, DeleteAnalyticsEvents delete)
    {
        ValidateAnalyticsDelete(delete);
        foreach (var id in delete.ExpectedRevisions.Keys)
        {
            var previous = AnalyticsStoreAuthorization.Previous(view, partition, delete.Store, id);
            if (previous is not null)
            {
                Authorization.RequireWriteRow(principal, previous.Event.RowAccess ?? new());
            }
        }
    }

    private void ValidateAnalyticsUpsert(UpsertAnalyticsEvents mutation, AnalyticsStoreSchema schema)
    {
        if (mutation.Events.IsDefaultOrEmpty || mutation.ExpectedRevisions is null
            || mutation.Events.Length > Limits.MaxBatchMutations
            || mutation.Events.Any(value => value is null)
            || mutation.Events.Select(value => value.Id).Distinct(StringComparer.Ordinal).Count() != mutation.Events.Length
            || !mutation.Events.Select(value => value.Id).Order(StringComparer.Ordinal)
                .SequenceEqual(mutation.ExpectedRevisions.Keys.Order(StringComparer.Ordinal))
            || mutation.ExpectedRevisions.Values.Any(value => value < AnalyticsMinimumExpectedRevision))
        {
            throw Errors.Fail(ErrorCode.Validation, AnalyticsStoreValidation.Invalid);
        }

        foreach (var value in mutation.Events)
        {
            AnalyticsStoreValidation.Event(value, schema, Limits);
        }
    }

    private void ValidateAnalyticsDelete(DeleteAnalyticsEvents mutation)
    {
        if (mutation.ExpectedRevisions is null || mutation.ExpectedRevisions.Count == AnalyticsEmptyRevisionCount
            || mutation.ExpectedRevisions.Count > Limits.MaxBatchMutations
            || mutation.ExpectedRevisions.Values.Any(value => value <= AnalyticsMinimumExpectedRevision))
        {
            throw Errors.Fail(ErrorCode.Validation, AnalyticsStoreValidation.Invalid);
        }

        foreach (var id in mutation.ExpectedRevisions.Keys)
        {
            JsonData.Identifier(id);
        }
    }
}
