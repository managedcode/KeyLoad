using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.QueryExecution;
using KeyLoad.Server;
using static KeyLoad.IntegrationTests.DatabaseComposition.FunctionalGateUserPolicies;

namespace KeyLoad.IntegrationTests.DatabaseComposition;

/// <summary>Eight real persisted database identities and their independently owned SDK/official MCP sessions.</summary>
internal sealed class FunctionalGateUsers : IAsyncDisposable
{
    private const int UserCount = 8;
    private const int WriterCount = 4;
    private const int ReaderStart = 4;
    private const int AllowedCount = UserCount;
    private const int DeniedIndex = ReaderStart;
    private const long FirstRevision = 1;
    private const string AuthId = "auth-grant-probe";
    private const string AuthJson = "{\"gate\":\"persisted-auth\",\"value\":31}";
    private const string EpochFence = "The principal policy changed since this command was evaluated.";
    private readonly List<RequestCqrsClusterCallers> callers = [];
    private readonly List<McpPersistedIdentity> identities = [];
    private RequestCqrsClusterCallers? queueAdministrator;

    internal RequestCqrsClusterCallers QueueAdministrator => queueAdministrator
        ?? throw new InvalidOperationException("The persisted queue operator is not connected.");
    internal RequestCqrsClusterCallers Writer => callers[0];
    internal RequestCqrsClusterCallers Reader => callers[ReaderStart];
    internal IReadOnlyList<RequestCqrsClusterCallers> Callers => callers;
    internal IReadOnlyList<RequestCqrsClusterCallers> Writers => callers.Take(WriterCount).ToArray();
    internal IReadOnlyList<RequestCqrsClusterCallers> Readers => callers.Skip(ReaderStart).Take(WriterCount).ToArray();
    internal IReadOnlyList<PrincipalRecord> WriterPrincipals => identities.Take(WriterCount).Select(item => item.Principal).ToArray();
    internal IReadOnlyList<string> WriterPrincipalIds => identities.Take(WriterCount).Select(item => item.Principal.Id).ToArray();
    internal IReadOnlyList<string> ReaderPrincipalIds => identities.Skip(ReaderStart).Take(WriterCount).Select(item => item.Principal.Id).ToArray();

    internal static async Task<FunctionalGateUsers> CreateAsync(FunctionalGateFixture fixture,
        RequestCqrsClusterCallers administrator, PartitionRef partition, CancellationToken token)
    {
        var owner = new FunctionalGateUsers();
        try
        {
            for (var index = 0; index < UserCount; index++)
            {
                var identity = await McpPersistedIdentity.CreateAsync(administrator.Sdk, partition,
                    "gate_docs_0", Capability.None, token);
                var capabilities = index < WriterCount ? WriteCapabilities : ReadCapabilities;
                var expected = identity.Principal with
                {
                    PolicyEpoch = checked(identity.Principal.PolicyEpoch + 1),
                    FieldGrants = [.. identity.Principal.FieldGrants.Concat(FunctionalGateAnalyticsAccess.Grants).Distinct(StringComparer.Ordinal)],
                    Grants = Grants(partition.DatabaseId, capabilities, index < WriterCount
                        ? identity.Principal.Id : owner.identities[index - ReaderStart].Principal.Id)
                };
                var persisted = await McpCallerAssertions.SdkSuccessAsync(
                    await administrator.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), expected, token));
                await SqlClusterProtocol.EqualAsync(expected, persisted);
                owner.identities.Add(identity with
                {
                    Principal = persisted
                });
                owner.callers.Add(await RequestCqrsClusterCallers.ConnectAsync(fixture.App,
                    fixture.NodeNames[index % fixture.NodeNames.Length], identity.Secret, token));
            }
            owner.queueAdministrator = await FunctionalGateQueueAdministrator.CreateAsync(fixture, administrator, partition, token);
            await Assert.That(owner.identities.Select(item => item.Principal.Id).Distinct(StringComparer.Ordinal).Count())
                .IsEqualTo(UserCount);
            return owner;
        }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            await ServerFailureObserver.ObserveAsync(() => owner.DisposeAsync().AsTask(), failures);
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    /// <summary>Verifies all eight identities, denied writes, fresh policy changes and original-command fencing.</summary>
    internal async Task VerifyAsync(FunctionalGateContext context, CancellationToken token)
    {
        var anchor = await context.SdkAsync(context.Administrator.Sdk.GetAsync(context.Anchor, token));
        await Assert.That(anchor.Document is not null).IsTrue();
        for (var index = 0; index < AllowedCount; index++)
        {
            await FunctionalGateUserAssertions.EqualDocumentAsync(context, callers[index], context.Anchor, anchor, token);
        }
        var probe = context.Bind(new(context.Partition, context.MutableDocumentCollection, AuthId));
        var absent = new DocumentReadResult(null);
        await FunctionalGateUserAssertions.EqualDocumentAsync(context, context.Administrator, probe, absent, token);
        var command = new CommandRequest(Guid.NewGuid(), context.Partition,
            [new PutDocument(probe.Collection, probe.Id, AuthJson, ExpectedRevision: 0)]);
        for (var index = ReaderStart; index < UserCount; index++)
        {
            await FunctionalGateUserAssertions.RefusedWriteAsync(callers[index], command, null, token);
            await FunctionalGateUserAssertions.EqualDocumentAsync(context, context.Administrator, probe, absent, token);
            await FunctionalGateUserAssertions.EqualDocumentAsync(context, context.Administrator, context.Anchor, anchor, token);
        }
        var foreign = context.Anchor with
        {
            Partition = context.Partition with
            {
                DatabaseId = context.Partition.DatabaseId + "-foreign"
            }
        };
        foreach (var caller in callers)
        {
            await FunctionalGateUserAssertions.RefusedReadAsync(caller, foreign, token);
        }

        await GrantAndRevokeAsync(context, anchor, probe, absent, command, foreign, token);
    }

    private async Task GrantAndRevokeAsync(FunctionalGateContext context, DocumentReadResult anchor,
        EntityRef probe, DocumentReadResult absent, CommandRequest command, EntityRef foreign, CancellationToken token)
    {
        // Grant only the actual read-only principal in the current database, on its original open connections.
        await SetPolicyAsync(context, DeniedIndex, WriteCapabilities, token);
        await FunctionalGateUserAssertions.EqualDocumentAsync(context, callers[DeniedIndex], context.Anchor, anchor, token);
        // A capability refusal can precede commit. Establish an actual retained apply-time failure.
        var retainedFailure = new CommandRequest(Guid.NewGuid(), context.Partition,
            [new PutDocument(probe.Collection, probe.Id, AuthJson, ExpectedRevision: FirstRevision)]);
        await FunctionalGateUserAssertions.RevisionConflictAsync(callers[DeniedIndex], retainedFailure, token);
        await FunctionalGateUserAssertions.EqualDocumentAsync(context, context.Administrator, probe, absent, token);
        var admitted = command with
        {
            CommandId = Guid.NewGuid()
        };
        var receipt = await context.SdkAsync(callers[DeniedIndex].Sdk.CommitAsync(admitted, token));
        await SqlClusterProtocol.EqualAsync(receipt, await context.McpAsync<CommitReceipt>(callers[DeniedIndex],
            McpCallerTools.DocumentsCommit, admitted, token));
        await Assert.That(receipt.CommandId).IsEqualTo(admitted.CommandId);
        await SqlClusterProtocol.EqualAsync(new MutationReceipt[] { new("putDocument", probe.Collection, probe.Id, FirstRevision) },
            receipt.Mutations.ToArray());
        var present = new DocumentReadResult(new(probe, FirstRevision, AuthJson, false, []));
        await FunctionalGateUserAssertions.EqualDocumentAsync(context, callers[DeniedIndex], probe, present, token);
        await FunctionalGateUserAssertions.EqualDocumentAsync(context, context.Administrator, probe, present, token);

        // This precondition would now allow a write; the original retained failure must still win.
        await FunctionalGateUserAssertions.RevisionConflictAsync(callers[DeniedIndex], retainedFailure, token);
        await SetPolicyAsync(context, DeniedIndex, WriteCapabilities, token);
        await FunctionalGateUserAssertions.RefusedWriteAsync(callers[DeniedIndex], retainedFailure, EpochFence, token);
        await FunctionalGateUserAssertions.RefusedWriteAsync(callers[DeniedIndex], admitted, EpochFence, token);
        await FunctionalGateUserAssertions.EqualDocumentAsync(context, context.Administrator, probe, present, token);

        await SetPolicyAsync(context, DeniedIndex, Capability.None, token);
        await FunctionalGateUserAssertions.RefusedReadAsync(callers[DeniedIndex], probe, token);
        await FunctionalGateUserAssertions.RefusedWriteAsync(callers[DeniedIndex], admitted, null, token);
        var refusedAfterRevoke = new CommandRequest(Guid.NewGuid(), context.Partition,
            [new PutDocument(probe.Collection, probe.Id, "{\"forbidden\":true}", ExpectedRevision: FirstRevision)]);
        await FunctionalGateUserAssertions.RefusedWriteAsync(callers[DeniedIndex], refusedAfterRevoke, null, token);
        await FunctionalGateUserAssertions.EqualDocumentAsync(context, context.Administrator, probe, present, token);
        await FunctionalGateUserAssertions.EqualDocumentAsync(context, context.Administrator, context.Anchor, anchor, token);

        // Fresh read-only repair succeeds; both previously refused IDs remain denied and the value stays exact.
        await SetPolicyAsync(context, DeniedIndex, ReadCapabilities, token);
        await FunctionalGateUserAssertions.EqualDocumentAsync(context, callers[DeniedIndex], probe, present, token);
        await FunctionalGateUserAssertions.RefusedWriteAsync(callers[DeniedIndex], command, null, token);
        await FunctionalGateUserAssertions.RefusedWriteAsync(callers[DeniedIndex], refusedAfterRevoke, null, token);
        await FunctionalGateUserAssertions.RefusedReadAsync(callers[DeniedIndex], foreign, token);
        await FunctionalGateUserAssertions.EqualDocumentAsync(context, context.Administrator, probe, present, token);
        // Remove the dedicated authentication row before model workers query these collections.
        var cleanup = new CommandRequest(Guid.NewGuid(), context.Partition,
            [new DeleteDocument(probe.Collection, probe.Id, ExpectedRevision: FirstRevision)]);
        var cleanupReceipt = await context.SdkAsync(context.Administrator.Sdk.CommitAsync(cleanup, token));
        await SqlClusterProtocol.EqualAsync(cleanupReceipt, await context.McpAsync<CommitReceipt>(context.Administrator,
            McpCallerTools.DocumentsCommit, cleanup, token));
        await SqlClusterProtocol.EqualAsync(new MutationReceipt[] { new("deleteDocument", probe.Collection, probe.Id, FirstRevision + 1) },
            cleanupReceipt.Mutations.ToArray());
        await FunctionalGateUserAssertions.EqualDocumentAsync(context, callers[DeniedIndex], probe, absent, token);
        await FunctionalGateUserAssertions.EqualDocumentAsync(context, context.Administrator, context.Anchor, anchor, token);
    }

    private async Task SetPolicyAsync(FunctionalGateContext context, int index, Capability capabilities, CancellationToken token)
    {
        var expected = identities[index].Principal with
        {
            PolicyEpoch = checked(identities[index].Principal.PolicyEpoch + 1),
            Grants = Grants(context.Partition.DatabaseId, capabilities, identities[index % WriterCount].Principal.Id)
        };
        var actual = await context.SdkAsync(context.Administrator.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), expected, token));
        await SqlClusterProtocol.EqualAsync(expected, actual);
        identities[index] = identities[index] with
        {
            Principal = actual
        };
    }

    public async ValueTask DisposeAsync()
    {
        var failures = new List<Exception>();
        if (queueAdministrator is not null)
        {
            try
            {
                await queueAdministrator.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception failure) when (KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(failure))
            {
                failures.Add(failure);
            }
            catch (Exception failure) when (!KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(failure))
            {
                failures.Add(failure);
            }
            queueAdministrator = null;
        }
        foreach (var caller in callers.AsEnumerable().Reverse())
        {
            await ServerFailureObserver.ObserveAsync(() => caller.DisposeAsync().AsTask(), failures);
        }
        callers.Clear();
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
