using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class PartitionQueryCreationOracle
{
    private const string OriginalConfigurationRequired = "The original successful partition-query configuration outcome is required.";

    internal static ResourceLifetimeV1 Configure(TestDatabase fixture, ConfigureResourceRequest request)
    {
        var command = fixture.Database.CreateCommand(OperationKind.ConfigureResource, Guid.NewGuid(),
            PartitionQueryTestSupport.Principal, fixture.Database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(request));
        _ = fixture.SubmitIssued(command).Get<ResourceDefinition>();
        var outcome = OutcomeStoreOracle.ReadStored(fixture.Store, command);
        if (outcome is null || outcome.Result.IsFailed || outcome.PreparedCommand.CommandId != command.CommandId
            || outcome.PreparedCommand.UserId != command.UserId
            || outcome.PreparedCommand.Value!.Kind != OperationKind.ConfigureResource
            || outcome.PreparedCommand.Value.Fingerprint != command.Value!.Fingerprint
            || !outcome.PreparedCommand.Value.Input.Span.SequenceEqual(command.Value!.Input.Span))
        {
            throw new InvalidOperationException(OriginalConfigurationRequired);
        }
        return ResourceLifetimeIdentity.Create(command, request.TenantId, request.DatabaseId,
            request.Definition);
    }

    internal static EntityRef Reference(ResourceLifetimeV1 creation, PartitionRef partition, string id)
    {
        if (creation.TenantId != partition.TenantId || creation.DatabaseId != partition.DatabaseId
            || creation.TransactionDomainId != partition.TransactionDomainId)
        {
            throw new InvalidOperationException(OriginalConfigurationRequired);
        }
        return new(partition, creation.ResourceName, id) { Lifetime = new(creation.Incarnation.Value) };
    }
}
