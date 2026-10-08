using System.Collections.Immutable;
using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private CommandRequest RequireControlledDocumentBatch(ReplicatedOperation original, PartitionRef partition)
    {
        if (original.Kind != OperationKind.Batch)
        { throw Errors.Fail(ErrorCode.UnsupportedCapability, UnsupportedOperationMessage); }
        var command = Payload<CommandRequest>(original);
        if (command.CommandId != original.Id || command.Partition != partition || command.Mutations.IsDefaultOrEmpty
            || command.Mutations.Length > Limits.MaxBatchMutations)
        { throw Errors.Fail(ErrorCode.Validation, PartitionMoveProtocol.Invalid); }
        foreach (var mutation in command.Mutations)
        {
            if (mutation is not (PutDocument or PatchDocument or DeleteDocument))
            { throw Errors.Fail(ErrorCode.UnsupportedCapability, UnsupportedMutationMessage); }
            ValidateMutationStructure(mutation);
        }
        return command;
    }

    private void RequireControlledDocumentPolicies(PrincipalRecord principal, CommandRequest command,
        ImmutableArray<ResourceDefinition> resources)
    {
        if (resources.IsDefault)
        { throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.Invalid); }
        foreach (var effect in command.Mutations)
        {
            Authorization.Require(principal, command.Partition, effect.Resource, Capability.DocumentsWrite);
            var resource = resources.SingleOrDefault(item => item.Name == effect.Resource)
                ?? throw Errors.Fail(ErrorCode.NotFound, DatabaseEngineResourceIsNotConfiguredDetail);
            if (resource.Kind != ResourceKind.Collection || resource.TransactionDomainId != command.Partition.TransactionDomainId)
            { throw Errors.Fail(ErrorCode.Validation, DatabaseEngineResourceHasADifferentKindDetail); }
            ReauthorizeEffectFields(principal, resource, effect);
        }
    }
}
