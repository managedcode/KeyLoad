using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.Client;
using KeyLoad.Comparisons;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>AC-ISO-003/004/005: untimed genuine process faults of each selected fixed native membership.</summary>
internal static class IsolatedKeyLoadFaultChecks
{
    internal static async Task VerifyAsync(DistributedApplication app, int nodeCount, string evidenceDirectory, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentOutOfRangeException.ThrowIfLessThan(nodeCount, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(nodeCount, 3);
        ArgumentException.ThrowIfNullOrWhiteSpace(evidenceDirectory);
        var evidence = IsolatedKeyLoadFaultChecksProvider.Read(nodeCount);
        IsolatedKeyLoadFaultChecksNative? native = null;
        string? failure = null;
        using var deadlineTimeout = new CancellationTokenSource(NativeExecutionPolicyFixture.Harness().Value.KeyLoadFaultChecksTimeout, TimeProvider.System);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token, deadlineTimeout.Token);
        try
        {
            await IsolatedKeyLoadFaultChecksExecution.RunAsync(async () =>
            {
                native = new(app, nodeCount);
                evidence.Native = native.Receipts;
                await RunAsync(app, nodeCount, native, evidence, deadline.Token);
            });
        }
        catch (ComparisonFailureException)
        {
            failure = deadline.IsCancellationRequested ? "deadlineOrCancellation" : evidence.Stage;
        }
        finally
        {
            var restored = native is null || await native.RestoreAllAsync();
            failure ??= restored ? null : "nativeCleanup";
            try
            {
                await IsolatedKeyLoadFaultChecksExecution.RunAsync(() => evidence.WriteAsync(evidenceDirectory, failure));
            }
            catch (ComparisonFailureException)
            {
                failure ??= "evidenceWrite";
            }
        }
        if (failure is not null)
        {
            throw new InvalidOperationException(IsolatedKeyLoadFaultChecksProtocol.Failure + ":" + failure);
        }
    }

    private static async Task RunAsync(DistributedApplication app, int nodes, IsolatedKeyLoadFaultChecksNative native,
        IsolatedKeyLoadFaultChecksEvidence evidence, CancellationToken token)
    {
        var admin = await app.Services.GetRequiredService<DistributedApplicationModel>().Resources.OfType<ParameterResource>()
            .Single(item => item.Name == IsolatedKeyLoadFaultChecksProtocol.Admin).GetValueAsync(token);
        IsolatedKeyLoadFaultChecksProtocol.Require(!string.IsNullOrWhiteSpace(admin));
        using var http = IsolatedKeyLoadPublicChecksProtocol.CreateHttp(app, 1);
        var sdk = new KeyLoadClient(http, admin!, ComparisonClientOptions.Execution());
        var seed = await IsolatedKeyLoadFaultChecksSeed.CreateAsync(sdk, nodes, token);
        await IsolatedKeyLoadFaultChecksAuthority.VerifyAsync(app, 1, admin!, seed, token);
        evidence.Baseline = await IsolatedKeyLoadFaultChecksMembership.WaitAsync(app, nodes, admin!, token);
        await IsolatedKeyLoadFaultChecksPhases.RunAsync(app, nodes, admin!, seed, native, evidence, token);
        evidence.Stage = "complete";
    }
}
