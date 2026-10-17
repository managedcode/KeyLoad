using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed record ResourceRetirementDropParentNativeState(DatabaseEngine Database,
    ResourceRetirementDropParentLookupV1 Lookup, ResourceRetirementRequestV1 Request,
    ResourceDefinition Definition, ResourceLifetimeV1 Lifetime, ReplicatedOperation Original,
    ResourceRetirementDropParentV1 Expected, OperationResult Result, byte[] OutcomeBytes);
