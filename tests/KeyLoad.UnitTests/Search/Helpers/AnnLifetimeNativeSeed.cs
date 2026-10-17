namespace KeyLoad.UnitTests.Features.Search;

internal static class AnnLifetimeNativeSeed
{
    internal static void Configure(TestDatabase database, bool source, bool derived = false, bool cross = false)
    {
        var vector = new SensitiveFieldPolicy(AnnSeedTestSupport.Field, AnnLifetimeNativeProtocol.Classification, AnnLifetimeNativeProtocol.VectorRead,
            source ? AnnLifetimeNativeProtocol.SourceUse : AnnLifetimeNativeProtocol.TargetUse,
            AnnLifetimeNativeProtocol.VectorWrite, SchemaReadGrant: source
                ? AnnLifetimeNativeProtocol.SourceVectorSchema : AnnLifetimeNativeProtocol.TargetVectorSchema);
        var secret = new SensitiveFieldPolicy(source ? AnnLifetimeNativeProtocol.SourceSecret : AnnLifetimeNativeProtocol.TargetSecret,
            "secret", AnnLifetimeNativeProtocol.SecretRead, AnnLifetimeNativeProtocol.SecretUse,
            AnnLifetimeNativeProtocol.SecretWrite, SchemaReadGrant: source ? AnnLifetimeNativeProtocol.SourceSecretSchema : null);
        var resource = new ResourceDefinition(AnnSeedTestSupport.Collection, ResourceKind.Collection, database.Partition.TransactionDomainId)
        {
            SchemaVersion = source ? AnnLifetimeNativeProtocol.SourceSchema : AnnLifetimeNativeProtocol.TargetSchema,
            FieldPolicies = derived && !cross ? [vector, secret, AnnLifetimeNativeDerived.InputPolicy(source)] : [vector, secret]
        };
        _ = database.Submit(OperationKind.ConfigureResource,
            new ConfigureResourceRequest(database.Partition.TenantId, database.Partition.DatabaseId, resource)).Get<ResourceDefinition>();
        Principal(database, source, source ? AnnLifetimeNativeProtocol.SourceEpoch : AnnLifetimeNativeProtocol.TargetEpoch);
        if (derived)
        {
            AnnLifetimeNativeDerived.Configure(database, source, cross);
        }
    }

    internal static void Principal(TestDatabase database, bool source, long epoch, bool schema = true, bool use = true,
        bool administrator = false)
    {
        var fields = new List<string>();
        if (schema)
        {
            fields.Add(source ? AnnLifetimeNativeProtocol.SourceVectorSchema : AnnLifetimeNativeProtocol.TargetVectorSchema);
        }
        if (use)
        {
            fields.Add(source ? AnnLifetimeNativeProtocol.SourceUse : AnnLifetimeNativeProtocol.TargetUse);
        }
        var principal = new PrincipalRecord(AnnLifetimeNativeProtocol.Reader,
            administrator ? AnnLifetimeNativeProtocol.SourceAdminTenant : database.Partition.TenantId,
            [new(database.Partition.DatabaseId, AnnSeedTestSupport.Collection, source ? Capability.VectorSearch
                : Capability.VectorSearch | Capability.Query | Capability.DocumentsRead)], [.. fields])
        {
            PolicyEpoch = epoch,
            ClusterAdministrator = administrator,
            RestrictRows = true,
            OwnerId = source ? AnnLifetimeNativeProtocol.SourceOwner : AnnLifetimeNativeProtocol.TargetOwner,
            Projects = source ? [AnnLifetimeNativeProtocol.SourceProject] : [],
            Teams = source ? [] : [AnnLifetimeNativeProtocol.TargetTeam]
        };
        _ = database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(principal)).Get<PrincipalRecord>();
    }

    internal static CommitReceipt Rows(TestDatabase target)
    {
        RowAccess[] access = [new(AnnLifetimeNativeProtocol.SourceOwner), new(AnnLifetimeNativeProtocol.TargetOwner),
            new(AnnLifetimeNativeProtocol.SourceOwner, TeamId: AnnLifetimeNativeProtocol.TargetTeam),
            new(AnnLifetimeNativeProtocol.TargetOwner, AnnLifetimeNativeProtocol.SourceProject),
            new(AnnLifetimeNativeProtocol.OtherOwner, AnnLifetimeNativeProtocol.SourceProject, AnnLifetimeNativeProtocol.TargetTeam),
            new(AnnLifetimeNativeProtocol.OtherOwner)];
        var mutations = new List<Mutation>();
        for (var index = AnnLifetimeNativeProtocol.First; index < access.Length; index++)
        {
            var id = AnnSeedTestSupport.Id(index);
            mutations.Add(new PutDocument(AnnSeedTestSupport.Collection, id, AnnLifetimeNativeProtocol.Body, Access: access[index]));
            mutations.Add(new PutVector(AnnSeedTestSupport.Collection, id, AnnSeedTestSupport.Field,
                [(float)index + 0.25f, 1f, -0.5f], AnnSeedTestSupport.Space(), AnnLifetimeNativeProtocol.Revision));
        }
        return target.Commit([.. mutations]);
    }
}
