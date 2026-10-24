namespace KeyLoad.UnitTests.Search;

internal static class AnnLifetimeSeed
{
    internal static void Configure(TestDatabase database, bool source, bool derived = false, bool cross = false)
    {
        var vector = new SensitiveFieldPolicy(AnnSeedTestSupport.Field, AnnLifetimeProtocol.Classification, AnnLifetimeProtocol.VectorRead,
            source ? AnnLifetimeProtocol.SourceUse : AnnLifetimeProtocol.TargetUse,
            AnnLifetimeProtocol.VectorWrite, SchemaReadGrant: source
                ? AnnLifetimeProtocol.SourceVectorSchema : AnnLifetimeProtocol.TargetVectorSchema);
        var secret = new SensitiveFieldPolicy(source ? AnnLifetimeProtocol.SourceSecret : AnnLifetimeProtocol.TargetSecret,
            "secret", AnnLifetimeProtocol.SecretRead, AnnLifetimeProtocol.SecretUse,
            AnnLifetimeProtocol.SecretWrite, SchemaReadGrant: source ? AnnLifetimeProtocol.SourceSecretSchema : null);
        var resource = new ResourceDefinition(AnnSeedTestSupport.Collection, ResourceKind.Collection, database.Partition.TransactionDomainId)
        {
            SchemaVersion = source ? AnnLifetimeProtocol.SourceSchema : AnnLifetimeProtocol.TargetSchema,
            FieldPolicies = derived && !cross ? [vector, secret, AnnLifetimeDerived.InputPolicy(source)] : [vector, secret]
        };
        _ = database.Submit(OperationKind.ConfigureResource,
            new ConfigureResourceRequest(database.Partition.TenantId, database.Partition.DatabaseId, resource)).Get<ResourceDefinition>();
        Principal(database, source, source ? AnnLifetimeProtocol.SourceEpoch : AnnLifetimeProtocol.TargetEpoch);
        if (derived)
        {
            AnnLifetimeDerived.Configure(database, source, cross);
        }
    }

    internal static void Principal(TestDatabase database, bool source, long epoch, bool schema = true, bool use = true,
        bool administrator = false)
    {
        var fields = new List<string>();
        if (schema)
        {
            fields.Add(source ? AnnLifetimeProtocol.SourceVectorSchema : AnnLifetimeProtocol.TargetVectorSchema);
        }
        if (use)
        {
            fields.Add(source ? AnnLifetimeProtocol.SourceUse : AnnLifetimeProtocol.TargetUse);
        }
        var principal = new PrincipalRecord(AnnLifetimeProtocol.Reader,
            administrator ? AnnLifetimeProtocol.SourceAdminTenant : database.Partition.TenantId,
            [new(database.Partition.DatabaseId, AnnSeedTestSupport.Collection, source ? Capability.VectorSearch
                : Capability.VectorSearch | Capability.Query | Capability.DocumentsRead)], [.. fields])
        {
            PolicyEpoch = epoch,
            ClusterAdministrator = administrator,
            RestrictRows = true,
            OwnerId = source ? AnnLifetimeProtocol.SourceOwner : AnnLifetimeProtocol.TargetOwner,
            Projects = source ? [AnnLifetimeProtocol.SourceProject] : [],
            Teams = source ? [] : [AnnLifetimeProtocol.TargetTeam]
        };
        _ = database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(principal)).Get<PrincipalRecord>();
    }

    internal static CommitReceipt Rows(TestDatabase target)
    {
        RowAccess[] access = [new(AnnLifetimeProtocol.SourceOwner), new(AnnLifetimeProtocol.TargetOwner),
            new(AnnLifetimeProtocol.SourceOwner, TeamId: AnnLifetimeProtocol.TargetTeam),
            new(AnnLifetimeProtocol.TargetOwner, AnnLifetimeProtocol.SourceProject),
            new(AnnLifetimeProtocol.OtherOwner, AnnLifetimeProtocol.SourceProject, AnnLifetimeProtocol.TargetTeam),
            new(AnnLifetimeProtocol.OtherOwner)];
        var mutations = new List<Mutation>();
        for (var index = AnnLifetimeProtocol.First; index < access.Length; index++)
        {
            var id = AnnSeedTestSupport.Id(index);
            mutations.Add(new PutDocument(AnnSeedTestSupport.Collection, id, AnnLifetimeProtocol.Body, Access: access[index]));
            mutations.Add(new PutVector(AnnSeedTestSupport.Collection, id, AnnSeedTestSupport.Field,
                [(float)index + 0.25f, 1f, -0.5f], AnnSeedTestSupport.Space(), AnnLifetimeProtocol.Revision));
        }
        return target.Commit([.. mutations]);
    }
}
