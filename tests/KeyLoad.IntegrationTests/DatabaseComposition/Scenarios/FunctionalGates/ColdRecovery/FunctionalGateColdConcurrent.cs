using System.Text.Json;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

/// <summary>Runs exactly eight original identities and joins their public callers before native restart or policy mutation.</summary>
internal static class FunctionalGateColdConcurrent
{
    private const int PrimaryIdentities = 3;
    private const int NoFailures = 0;
    private const long NoPublicOperations = 0;
    private const string MissingProof = "The original concurrent cold workload proof is absent.";
    private const string WitnessKind = "OriginalEightUserColdConcurrentWave";

    internal static async Task<FunctionalGateColdWorkload[]> RunAsync(FunctionalGateFixture fixture,
        FunctionalGateContext original, FunctionalGateColdState state, bool seed, CancellationToken token)
    {
        await FunctionalGateColdWorkloadProvision.InventoryAsync(state);
        McpPersistedIdentity[] identities = [state.Writer, state.Reader, state.QueueOperator, .. state.AdditionalIdentities];
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        var start = new FunctionalGateColdConcurrentStart();
        var results = await RunWaveAsync(fixture, original, state, identities, seed, start, cancellation);
        await Assert.That(start.Arrivals).IsEqualTo(FunctionalGateColdWorkloadProvision.TotalWorkloadIdentities);
        await Assert.That(results.Length).IsEqualTo(FunctionalGateColdWorkloadProvision.TotalWorkloadIdentities);
        foreach (var result in results)
        {
            await Assert.That(result.SdkOperations).IsGreaterThan(NoPublicOperations);
            await Assert.That(result.McpOperations).IsGreaterThan(NoPublicOperations);
        }
        await Console.Out.WriteLineAsync(JsonSerializer.Serialize(new
        {
            Kind = WitnessKind,
            Seed = seed,
            TopologyNodes = fixture.NodeNames.Length,
            ReadyCallers = start.Arrivals,
            CompletedCallers = results.Length,
            AllOwnedCallersJoined = true,
            SdkOperations = results.Select(result => result.SdkOperations).ToArray(),
            McpOperations = results.Select(result => result.McpOperations).ToArray()
        }).AsMemory(), token);
        return results.Skip(PrimaryIdentities).Select(result => result.Proof
            ?? throw new InvalidOperationException(MissingProof)).ToArray();
    }

    /// <summary>Borrows cancellation until every started caller has joined, including a partial start failure.</summary>
    private static async Task<Result[]> RunWaveAsync(FunctionalGateFixture fixture, FunctionalGateContext original,
        FunctionalGateColdState state, McpPersistedIdentity[] identities, bool seed,
        FunctionalGateColdConcurrentStart start, CancellationTokenSource cancellation)
    {
        const int FirstIdentityIndex = 0;
        var tasks = new List<Task<Result>>(identities.Length);
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(() =>
        {
            for (var index = FirstIdentityIndex; index < identities.Length; index++)
            {
                tasks.Add(OneAsync(fixture, original, state, identities[index], index, seed, start, cancellation));
            }
        }, failures);
        if (failures.Count > NoFailures)
        {
            await ServerFailureObserver.ObserveAsync(cancellation.CancelAsync, failures);
        }
        var joined = Task.WhenAll(tasks);
        await ServerFailureObserver.ObserveAsync(() => joined, failures);
        var results = new List<Result>(tasks.Count);
        foreach (var task in tasks)
        {
            if (task.IsCompletedSuccessfully)
            {
                var result = await task;
                results.Add(result);
                failures.AddRange(result.Failures);
            }
        }
        ServerFailureObserver.ThrowIfAny(failures);
        return results.ToArray();
    }

    private static async Task<Result> OneAsync(FunctionalGateFixture fixture, FunctionalGateContext original,
        FunctionalGateColdState state, McpPersistedIdentity identity, int index, bool seed,
        FunctionalGateColdConcurrentStart start, CancellationTokenSource cancellation)
    {
        var failures = new List<Exception>();
        FunctionalGateColdWorkload? proof = null;
        FunctionalGateContext? context = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            proof = await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, fixture.NodeNames[^1], identity.Secret,
                async caller =>
                {
                    context = new(original.Administrator, caller, caller, original.Partition, original.Anchor, original.ResourceCount)
                    {
                        ReferenceBindings = original.ReferenceBindings,
                        QueueAdministrator = caller,
                        WriterPrincipalId = identity.Principal.Id,
                        ReaderPrincipalId = identity.Principal.Id,
                        MutableDocumentCollection = original.MutableDocumentCollection,
                        ReplicaCount = original.ReplicaCount
                    };
                    try
                    {
                        await start.EnterAsync(cancellation.Token);
                        return await ExerciseAsync(context, state, identity, index, seed, cancellation.Token);
                    }
                    catch (Exception primary)
                    {
                        var originalFailures = new List<Exception> { primary };
                        await ServerFailureObserver.ObserveAsync(cancellation.CancelAsync, originalFailures);
                        ServerFailureObserver.ThrowIfAny(originalFailures);
                        throw;
                    }
                }, cancellation.Token);
        }, failures);
        if (failures.Count > NoFailures)
        {
            await ServerFailureObserver.ObserveAsync(cancellation.CancelAsync, failures);
        }
        return new(proof, context?.SdkOperations ?? NoPublicOperations,
            context?.McpOperations ?? NoPublicOperations, failures.ToArray());
    }

    private static async Task<FunctionalGateColdWorkload?> ExerciseAsync(FunctionalGateContext context,
        FunctionalGateColdState state, McpPersistedIdentity identity, int index, bool seed, CancellationToken token)
    {
        if (index < PrimaryIdentities)
        {
            await FunctionalGateColdConcurrentPrimary.RunAsync(context, state, index, token);
            return null;
        }
        var ordinal = index - PrimaryIdentities;
        if (seed)
        {
            return await FunctionalGateColdWorkloads.SeedOneAsync(context, state, identity,
                FunctionalGateColdWorkloadProvision.Resource(ordinal), token);
        }
        var proof = state.Workloads[ordinal];
        await Assert.That(proof.Identity.Principal.Id).IsEqualTo(identity.Principal.Id);
        await FunctionalGateColdWorkloads.ReplayAsync(context, proof, token);
        foreach (var model in state.Models)
        {
            await FunctionalGateColdReadAssertions.VerifyAsync(context, model, token);
        }
        await FunctionalGateColdRelationalClosure.VerifyAsync(context, state, token);
        return proof;
    }

    private sealed record Result(FunctionalGateColdWorkload? Proof, long SdkOperations,
        long McpOperations, Exception[] Failures);
}
