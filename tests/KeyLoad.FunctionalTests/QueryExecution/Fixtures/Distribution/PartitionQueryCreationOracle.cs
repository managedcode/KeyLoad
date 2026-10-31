using KeyLoad.Core.ResourceExecution;

namespace KeyLoad.FunctionalTests.QueryExecution;

internal static class PartitionQueryCreationOracle
{
    private const string OriginalConfigurationRequired = "The original successful partition-query configuration outcome is required.";

    internal static ResourceLifetime Configure(TestDatabase fixture, ConfigureResourceRequest request)
    {
        var command = fixture.Database.CreateCommand(OperationKind.ConfigureResource, Guid.NewGuid(),
            PartitionQueryTestSupport.Principal, fixture.Database.EvaluationClock.GetUtcNow(), request);
        _ = fixture.SubmitIssued(command).Get<ResourceDefinition>();
        var outcome = OutcomeStoreOracle.ReadStored(fixture.Store, command);
        if (outcome is null || outcome.Result.IsFailed || outcome.PreparedCommand.CommandId != command.CommandId
            || outcome.PreparedCommand.UserId != command.UserId
            || outcome.PreparedCommand.Value!.Kind != OperationKind.ConfigureResource
            || !DatabaseCommandContentIdentity.SameContent(outcome.PreparedCommand, command)
            || !OrleansSerialization.Serialize(outcome.PreparedCommand.Value.Input, ValidationProfile.PublicInputElements).AsSpan().SequenceEqual(OrleansSerialization.Serialize(command.Value!.Input, ValidationProfile.PublicInputElements).AsSpan()))
        {
            throw new InvalidOperationException(OriginalConfigurationRequired);
        }
        return ResourceLifetimeIdentity.Create(command, request.TenantId, request.DatabaseId,
            request.Definition);
    }

    internal static EntityRef Reference(ResourceLifetime creation, PartitionRef partition, string id)
    {
        if (creation.TenantId != partition.TenantId || creation.DatabaseId != partition.DatabaseId
            || creation.TransactionDomainId != partition.TransactionDomainId)
        {
            throw new InvalidOperationException(OriginalConfigurationRequired);
        }
        return new(partition, creation.ResourceName, id) { Lifetime = new(creation.Incarnation.Value) };
    }
}
