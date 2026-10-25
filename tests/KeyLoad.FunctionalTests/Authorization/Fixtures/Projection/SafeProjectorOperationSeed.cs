namespace KeyLoad.FunctionalTests.Authorization;

internal static class SafeProjectorOperationSeed
{
    internal static async Task<SafeProjectorOperationState> CreateAsync(SafeProjectorOperationDatabase owner)
    {
        var resource = Resource(owner);
        var configured = Issue(owner, OperationKind.ConfigureResource, Guid.NewGuid(),
            new ConfigureResourceRequest(owner.Source.Partition.TenantId, owner.Source.Partition.DatabaseId, resource));
        await EqualAsync(configured.Result.Get<ResourceDefinition>(), resource);
        var reader = Principal(owner, SafeProjectorOperationProtocol.InitialPolicyEpoch, []);
        var principal = Issue(owner, OperationKind.ConfigurePrincipal, Guid.NewGuid(), new ConfigurePrincipalRequest(reader));
        await EqualAsync(principal.Result.Get<PrincipalRecord>(), reader);
        var id = Guid.NewGuid();
        var request = new CommandRequest(id, owner.Source.Partition,
        [
            new PutDocument(SafeProjectorOperationProtocol.Collection, SafeProjectorOperationProtocol.First,
                SafeProjectorOperationProtocol.InitialJson, ExpectedRevision: SafeProjectorOperationProtocol.AbsentRevision),
            new PutDocument(SafeProjectorOperationProtocol.Collection, SafeProjectorOperationProtocol.Second,
                SafeProjectorOperationProtocol.SecondJson, ExpectedRevision: SafeProjectorOperationProtocol.AbsentRevision)
        ]);
        var seed = Issue(owner, OperationKind.Batch, id, request);
        await EqualAsync(seed.Result.Get<CommitReceipt>(), new CommitReceipt(id, seed.ExpectedToken, [
            new(SafeProjectorOperationProtocol.PutKind, SafeProjectorOperationProtocol.Collection,
                SafeProjectorOperationProtocol.First, SafeProjectorOperationProtocol.InitialRevision),
            new(SafeProjectorOperationProtocol.PutKind, SafeProjectorOperationProtocol.Collection,
                SafeProjectorOperationProtocol.Second, SafeProjectorOperationProtocol.InitialRevision)
        ], DurabilityProfile.ProcessDurable, 1));
        return new(resource, reader, configured, principal, seed);
    }

    internal static SafeProjectorOperationCommand Issue<T>(SafeProjectorOperationDatabase owner, OperationKind kind,
        Guid id, T request, string principal = SafeProjectorOperationProtocol.Root, long creatorEpoch = SafeProjectorOperationProtocol.RootPolicyEpoch)
    {
        var operation = owner.Database.CreateCommand(kind, id, principal,
            owner.Database.EvaluationClock.GetUtcNow(), OrleansSerialization.Serialize(request));
        var expected = owner.NextToken();
        var result = owner.Submit(operation);
        var image = SafeProjectorOperationHistoryImages.Capture(owner, operation);
        return new(operation, owner.ReadOutcome(operation), result, expected, creatorEpoch, image);
    }

    internal static PrincipalRecord Principal(SafeProjectorOperationDatabase owner, long epoch,
        System.Collections.Immutable.ImmutableArray<string> grants) => new(SafeProjectorOperationProtocol.Reader,
        owner.Source.Partition.TenantId,
        [new(owner.Source.Partition.DatabaseId, SafeProjectorOperationProtocol.Collection,
            Capability.DocumentsRead | Capability.DocumentsWrite | Capability.Query | Capability.ChangesRead)], grants)
        {
            PolicyEpoch = epoch
        };

    private static ResourceDefinition Resource(SafeProjectorOperationDatabase owner) => new(
        SafeProjectorOperationProtocol.Collection, ResourceKind.Collection, owner.Source.Partition.TransactionDomainId)
    {
        Indexes = [new(SafeProjectorOperationProtocol.Index, [SafeProjectorOperationProtocol.StatusPath])],
        FieldPolicies =
        [
            Policy(SafeProjectorOperationProtocol.NestedPath),
            Policy(SafeProjectorOperationProtocol.LiteralPath),
            Policy(SafeProjectorOperationProtocol.ArrayPath)
        ]
    };

    private static SensitiveFieldPolicy Policy(string path) => new(path, SafeProjectorOperationProtocol.Classification,
        RawReadGrant: SafeProjectorOperationProtocol.ReadGrant, RawUseGrant: SafeProjectorOperationProtocol.UseGrant,
        WriteGrant: SafeProjectorOperationProtocol.WriteGrant);

    internal static async Task EqualAsync<T>(T actual, T expected)
    {
        await Assert.That(StringComparer.Ordinal.Equals(
            System.Text.Json.JsonSerializer.Serialize(actual, JsonDefaults.Options),
            System.Text.Json.JsonSerializer.Serialize(expected, JsonDefaults.Options))).IsTrue();
        if (actual is CommitReceipt receipt && expected is CommitReceipt literal)
        {
            await Assert.That(StringComparer.Ordinal.Equals(
                System.Text.Json.JsonSerializer.Serialize(receipt.Mutations.Select(row => row.CompositionReferences), JsonDefaults.Options),
                System.Text.Json.JsonSerializer.Serialize(literal.Mutations.Select(row => row.CompositionReferences), JsonDefaults.Options))).IsTrue();
        }
    }
}
