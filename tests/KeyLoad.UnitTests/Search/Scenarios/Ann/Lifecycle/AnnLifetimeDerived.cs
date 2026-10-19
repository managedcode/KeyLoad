using System.Collections.Immutable;

namespace KeyLoad.UnitTests.Features.Search;

internal static class AnnLifetimeDerived
{
    internal static SensitiveFieldPolicy InputPolicy(bool source)
        => new(AnnLifetimeProtocol.InputField, AnnLifetimeProtocol.Classification,
            AnnLifetimeProtocol.SecretRead, source ? AnnLifetimeProtocol.SourceInputUse : AnnLifetimeProtocol.TargetInputUse,
            AnnLifetimeProtocol.SecretWrite, SchemaReadGrant: source
                ? AnnLifetimeProtocol.SourceInputSchema : AnnLifetimeProtocol.TargetInputSchema);

    internal static void Configure(TestDatabase database, bool source, bool cross)
    {
        if (cross)
        {
            _ = database.Configure(AnnLifetimeProtocol.InputCollection, ResourceKind.Collection, fields: [InputPolicy(source)]);
        }
        var principal = database.Store.Read(view => database.Database.Principal(view, AnnLifetimeProtocol.Reader,
            database.Database.EvaluationClock.GetUtcNow()));
        var collection = cross ? AnnLifetimeProtocol.InputCollection : AnnSeedTestSupport.Collection;
        var capabilities = source && !cross ? Capability.VectorSearch | Capability.DocumentsRead : Capability.DocumentsRead;
        var grants = cross ? principal.Grants.Add(new(database.Partition.DatabaseId, collection, capabilities))
            : ImmutableArray.Create(new ScopeGrant(database.Partition.DatabaseId, collection, source ? capabilities
                : Capability.VectorSearch | Capability.Query | Capability.DocumentsRead));
        _ = database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(principal with
        {
            Grants = grants,
            FieldGrants = source ? principal.FieldGrants : principal.FieldGrants.Add(AnnLifetimeProtocol.TargetInputSchema)
                .Add(AnnLifetimeProtocol.TargetInputUse),
            PolicyEpoch = principal.PolicyEpoch + AnnLifetimeProtocol.NextEpoch
        })).Get<PrincipalRecord>();
    }

    internal static CommitReceipt Apply(TestDatabase target, bool cross)
    {
        _ = target.Configure(AnnLifetimeProtocol.StreamSet, ResourceKind.StreamSet);
        var collection = cross ? AnnLifetimeProtocol.InputCollection : AnnSeedTestSupport.Collection;
        _ = target.Commit(new PutDocument(collection, AnnLifetimeProtocol.InputId, AnnLifetimeProtocol.InputBody,
            Access: new(AnnLifetimeProtocol.SourceOwner, TeamId: AnnLifetimeProtocol.TargetTeam)),
            new AppendEvents(AnnLifetimeProtocol.StreamSet, AnnLifetimeProtocol.StreamId,
                [new(AnnLifetimeProtocol.EventId, AnnLifetimeProtocol.EventType, AnnLifetimeProtocol.InputBody)],
                ExpectedStreamRevision.NoStream));
        var stream = new StreamRef(target.Partition, AnnLifetimeProtocol.StreamSet, AnnLifetimeProtocol.StreamId,
            AnnLifetimeProtocol.Revision);
        var request = new ApplyVectorProjection(stream, AnnLifetimeProtocol.Revision, AnnLifetimeProtocol.EventId,
            new(target.Partition, collection, AnnLifetimeProtocol.InputId), AnnLifetimeProtocol.Revision,
            AnnLifetimeProtocol.InputField, AnnLifetimeProtocol.Reducer, AnnLifetimeProtocol.ReducerVersion,
            AnnLifetimeProtocol.Revision, new(AnnSeedTestSupport.Collection, AnnSeedTestSupport.Id(AnnLifetimeProtocol.BothOwner),
                AnnSeedTestSupport.Field, [1, 0, 0], AnnSeedTestSupport.Space(), AnnLifetimeProtocol.Revision));
        return target.Commit(request);
    }

    internal static void Repair(TestDatabase database, bool source, bool rawUse)
    {
        var principal = database.Store.Read(view => database.Database.Principal(view, AnnLifetimeProtocol.Reader,
            database.Database.EvaluationClock.GetUtcNow()));
        var use = source ? AnnLifetimeProtocol.SourceInputUse : AnnLifetimeProtocol.TargetInputUse;
        var schema = source ? AnnLifetimeProtocol.SourceInputSchema : AnnLifetimeProtocol.TargetInputSchema;
        var fields = principal.FieldGrants.Where(value => value != use && value != schema).ToList();
        fields.Add(schema);
        if (rawUse)
        {
            fields.Add(use);
        }
        _ = database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(principal with
        {
            FieldGrants = [.. fields],
            PolicyEpoch = principal.PolicyEpoch + AnnLifetimeProtocol.NextEpoch
        })).Get<PrincipalRecord>();
    }
}
