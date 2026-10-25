using KeyLoad.Core.ResourceExecution;
using KeyLoad.Query;
using P = KeyLoad.FunctionalTests.Authorization.SafeProjectorOperationProtocol;

namespace KeyLoad.FunctionalTests.Authorization;

internal static class ResourceLifetimeLedgerData
{
    internal const string Other = "lifetime-ledger-other";
    internal const string FullScan = "bounded-full-scan";
    internal const long RepairEpoch = 2;
    internal const int NoEntries = 0;
    internal const int OneEntry = 1;
    internal const int TwoEntries = 2;
    internal const int OneByte = 1;

    internal static AstQueryRequest Request(SafeProjectorOperationDatabase owner, string name)
        => new(owner.Source.Partition, new(name, null, [new(P.Star, P.Star)], null,
            [new(P.IdPath, false)], P.BothRows), AllowFullScan: true);

    internal static QueryPage Expected(SafeProjectorOperationDatabase owner)
        => new([
            new(P.First, P.InitialRevision, P.InitialSafe, true, P.RedactedPaths),
            new(P.Second, P.InitialRevision, P.SecondJson, true, P.RedactedPaths)
        ], null, owner.Store.Position, FullScan);

    internal static ResourceLifetime Lifetime(SafeProjectorOperationDatabase owner,
        SafeProjectorOperationCommand command, ResourceDefinition resource)
        => ResourceLifetimeIdentity.Create(command.Operation, owner.Source.Partition.TenantId,
            owner.Source.Partition.DatabaseId, resource);

    internal static SafeProjectorOperationCommand OtherResource(SafeProjectorOperationDatabase owner,
        ResourceDefinition original)
    {
        var resource = original with
        {
            Name = Other
        };
        return SafeProjectorOperationSeed.Issue(owner, OperationKind.ConfigureResource, Guid.NewGuid(),
            new ConfigureResourceRequest(owner.Source.Partition.TenantId, owner.Source.Partition.DatabaseId, resource));
    }

    internal static SafeProjectorOperationCommand Repair(SafeProjectorOperationDatabase owner,
        PrincipalRecord original)
    {
        var reader = original with
        {
            PolicyEpoch = RepairEpoch,
            Grants = [.. original.Grants, new(owner.Source.Partition.DatabaseId, Other,
                Capability.Query | Capability.DocumentsRead)]
        };
        return SafeProjectorOperationSeed.Issue(owner, OperationKind.ConfigurePrincipal, Guid.NewGuid(),
            new ConfigurePrincipalRequest(reader));
    }
}
