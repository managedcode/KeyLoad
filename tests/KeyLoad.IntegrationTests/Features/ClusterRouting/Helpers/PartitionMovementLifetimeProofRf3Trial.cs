using KeyLoad.Client;
using KeyLoad.Clustering;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Owns original public tasks and their one genuine six-resource Aspire application through all diagnostic holds.</summary>
internal sealed class PartitionMovementLifetimeProofRf3Trial(List<Exception> failures) : IAsyncDisposable
{
    private readonly List<PartitionMovementLifetimeProofRf3Original> originals = [];
    private readonly List<Guid> arms = [];
    private TwoRf3MembershipWave? wave;
    private PartitionMovementPublicParentRf3Seed? seed;
    private Task? disposal;

    internal static async Task RunAsync(CancellationToken token)
    {
        using var deadline = new CancellationTokenSource(RequestCqrsRf3Protocol.ParentDeadline, TimeProvider.System);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token);
        var failures = new List<Exception>();
        try
        {
            await using var trial = new PartitionMovementLifetimeProofRf3Trial(failures);
            try
            {
                await ServerFailureObserver.ObserveAsync(() => trial.ExecuteAsync(caller.Token), failures);
            }
            finally
            {
                await ServerFailureObserver.ObserveAsync(caller.CancelAsync, failures);
            }
        }
        catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
        catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private async Task ExecuteAsync(CancellationToken token)
    {
        wave = await NativeCanonicalModelCutWaveStartup.StartAsync(PartitionMovementPublicParentRf3Administrator.PrincipalId, token);
        seed = await PartitionMovementPublicParentRf3Seed.CreateAsync(wave, token);
        var terminal = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.MovePartitionAsync(seed.FirstRequest, token));
        await PartitionMovementPublicParentRf3Scenario.RequireTerminalAsync(seed, seed.FirstRequest,
            seed.OriginalPlacement, seed.Directory.ControlOwner, terminal, token);
        await seed.VerifyAsync(token);
        var fixture = new NativeCommandLifetimeProofRf3Fixture(wave, seed);
        var donor = await ProbedAsync(fixture, terminal, official: false, donor: null, token);
        var mcp = await ProbedAsync(fixture, terminal, official: true, donor, token);
        await PartitionMovementLifetimeProofRf3Cold.ExecuteAsync(wave, seed, terminal, [donor, mcp], originals, token);
        foreach (var arm in arms)
        {
            await wave.QueryControls.RetireArmAsync(arm, token);
        }
    }

    private async Task<PartitionMovementLifetimeProofRf3Original> ProbedAsync(NativeCommandLifetimeProofRf3Fixture fixture,
        PartitionMoveResult terminal, bool official, PartitionMovementLifetimeProofRf3Original? donor, CancellationToken token)
    {
        var input = PartitionMovementLifetimeProofRf3Calls.Create(seed!, terminal, official, cold: false);
        var arm = fixture.Arm(input.Command, donor?.ArmId);
        arms.Add(arm);
        var original = PartitionMovementLifetimeProofRf3Calls.Start(seed!, input, official, arm, token);
        originals.Add(original);
        await fixture.DriveAsync(original, donor, token);
        await PartitionMovementLifetimeProofRf3Calls.CompleteAsync(seed!, terminal, original, token);
        await fixture.JoinProducerAsync(original, token);
        return original;
    }

    public ValueTask DisposeAsync() => new(disposal ??= DisposeCoreAsync());

    private async Task DisposeCoreAsync()
    {
        var cleanup = new List<Exception>();
        if (wave is { } resources && (failures.Count != 0 || originals.Any(value => !value.Producer.IsCompleted)))
        {
            resources.RetainRoots();
            await resources.DisposeApplicationAsync(cleanup);
        }
        foreach (var original in originals)
        {
            await ServerFailureObserver.ObserveAsync(() => original.Producer, cleanup);
            if (original.Producer.IsCompletedSuccessfully && original.Receipt is null)
            {
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    _ = await original.Result();
                }, cleanup);
            }
        }
        if (seed is { } calls)
        {
            await ServerFailureObserver.ObserveAsync(() => calls.DisposeAsync().AsTask(), cleanup);
        }
        if (wave is { } owned)
        {
            if (failures.Count != 0 || cleanup.Count != 0)
            {
                owned.RetainRoots();
            }
            await ServerFailureObserver.ObserveAsync(() => owned.DisposeAsync().AsTask(), cleanup);
        }
        failures.AddRange(cleanup);
    }
}
