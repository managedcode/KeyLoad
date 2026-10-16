using System.Collections.Immutable;

namespace KeyLoad.UnitTests.Features.Search;

internal static class AnnLifetimeNativeDerived
{
    internal static SensitiveFieldPolicy InputPolicy(bool source)
        => new(AnnLifetimeNativeProtocol.InputField, AnnLifetimeNativeProtocol.Classification,
            AnnLifetimeNativeProtocol.SecretRead, source ? AnnLifetimeNativeProtocol.SourceInputUse : AnnLifetimeNativeProtocol.TargetInputUse,
            AnnLifetimeNativeProtocol.SecretWrite, SchemaReadGrant: source
                ? AnnLifetimeNativeProtocol.SourceInputSchema : AnnLifetimeNativeProtocol.TargetInputSchema);

    internal static void Configure(TestDatabase database, bool source, bool cross)
    {
        if (cross)
        {
            _ = database.Configure(AnnLifetimeNativeProtocol.InputCollection, ResourceKind.Collection, fields: [InputPolicy(source)]);
        }
        var principal = database.Store.Read(view => database.Database.Principal(view, AnnLifetimeNativeProtocol.Reader,
            database.Database.EvaluationClock.GetUtcNow()));
        var collection = cross ? AnnLifetimeNativeProtocol.InputCollection : AnnSeedTestSupport.Collection;
        var capabilities = source && !cross ? Capability.VectorSearch | Capability.DocumentsRead : Capability.DocumentsRead;
        var grants = cross ? principal.Grants.Add(new(database.Partition.DatabaseId, collection, capabilities))
            : ImmutableArray.Create(new ScopeGrant(database.Partition.DatabaseId, collection, source ? capabilities
                : Capability.VectorSearch | Capability.Query | Capability.DocumentsRead));
        _ = database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(principal with
        {
            Grants = grants,
            FieldGrants = source ? principal.FieldGrants : principal.FieldGrants.Add(AnnLifetimeNativeProtocol.TargetInputSchema)
                .Add(AnnLifetimeNativeProtocol.TargetInputUse),
            PolicyEpoch = principal.PolicyEpoch + AnnLifetimeNativeProtocol.NextEpoch
        })).Get<PrincipalRecord>();
    }

    internal static CommitReceipt Apply(TestDatabase target, bool cross)
    {
        _ = target.Configure(AnnLifetimeNativeProtocol.StreamSet, ResourceKind.StreamSet);
        var collection = cross ? AnnLifetimeNativeProtocol.InputCollection : AnnSeedTestSupport.Collection;
        _ = target.Commit(new PutDocument(collection, AnnLifetimeNativeProtocol.InputId, AnnLifetimeNativeProtocol.InputBody,
            Access: new(AnnLifetimeNativeProtocol.SourceOwner, TeamId: AnnLifetimeNativeProtocol.TargetTeam)),
            new AppendEvents(AnnLifetimeNativeProtocol.StreamSet, AnnLifetimeNativeProtocol.StreamId,
                [new(AnnLifetimeNativeProtocol.EventId, AnnLifetimeNativeProtocol.EventType, AnnLifetimeNativeProtocol.InputBody)],
                ExpectedStreamRevision.NoStream));
        var stream = new StreamRef(target.Partition, AnnLifetimeNativeProtocol.StreamSet, AnnLifetimeNativeProtocol.StreamId,
            AnnLifetimeNativeProtocol.Revision);
        var request = new ApplyVectorProjection(stream, AnnLifetimeNativeProtocol.Revision, AnnLifetimeNativeProtocol.EventId,
            new(target.Partition, collection, AnnLifetimeNativeProtocol.InputId), AnnLifetimeNativeProtocol.Revision,
            AnnLifetimeNativeProtocol.InputField, AnnLifetimeNativeProtocol.Reducer, AnnLifetimeNativeProtocol.ReducerVersion,
            AnnLifetimeNativeProtocol.Revision, new(AnnSeedTestSupport.Collection, AnnSeedTestSupport.Id(AnnLifetimeNativeProtocol.BothOwner),
                AnnSeedTestSupport.Field, [1, 0, 0], AnnSeedTestSupport.Space(), AnnLifetimeNativeProtocol.Revision));
        return target.Commit(request);
    }

    internal static void Repair(TestDatabase database, bool source, bool rawUse)
    {
        var principal = database.Store.Read(view => database.Database.Principal(view, AnnLifetimeNativeProtocol.Reader,
            database.Database.EvaluationClock.GetUtcNow()));
        var use = source ? AnnLifetimeNativeProtocol.SourceInputUse : AnnLifetimeNativeProtocol.TargetInputUse;
        var schema = source ? AnnLifetimeNativeProtocol.SourceInputSchema : AnnLifetimeNativeProtocol.TargetInputSchema;
        var fields = principal.FieldGrants.Where(value => value != use && value != schema).ToList();
        fields.Add(schema);
        if (rawUse)
        {
            fields.Add(use);
        }
        _ = database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(principal with
        {
            FieldGrants = [.. fields],
            PolicyEpoch = principal.PolicyEpoch + AnnLifetimeNativeProtocol.NextEpoch
        })).Get<PrincipalRecord>();
    }
}
