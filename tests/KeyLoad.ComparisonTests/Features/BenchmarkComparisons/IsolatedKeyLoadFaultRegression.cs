using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.Client;
using KeyLoad.Comparisons;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>AC-ISO-003/004/005: untimed genuine process faults of each selected fixed native membership.</summary>
internal static class IsolatedKeyLoadFaultRegression
{
    internal static async Task VerifyAsync(DistributedApplication app, int nodeCount, string evidenceDirectory, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentOutOfRangeException.ThrowIfLessThan(nodeCount, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(nodeCount, 3);
        ArgumentException.ThrowIfNullOrWhiteSpace(evidenceDirectory);
        var evidence = IsolatedKeyLoadFaultRegressionProvider.Read(nodeCount);
        IsolatedKeyLoadFaultRegressionNative? native = null;
        string? failure = null;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromMinutes(IsolatedKeyLoadFaultRegressionProtocol.OverallMinutes));
        try
        {
            await IsolatedKeyLoadFaultRegressionExecution.RunAsync(async () =>
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
                await IsolatedKeyLoadFaultRegressionExecution.RunAsync(() => evidence.WriteAsync(evidenceDirectory, failure));
            }
            catch (ComparisonFailureException)
            {
                failure ??= "evidenceWrite";
            }
        }
        if (failure is not null)
        {
            throw new InvalidOperationException(IsolatedKeyLoadFaultRegressionProtocol.Failure + ":" + failure);
        }
    }

    private static async Task RunAsync(DistributedApplication app, int nodes, IsolatedKeyLoadFaultRegressionNative native,
        IsolatedKeyLoadFaultRegressionEvidence evidence, CancellationToken token)
    {
        var admin = await app.Services.GetRequiredService<DistributedApplicationModel>().Resources.OfType<ParameterResource>()
            .Single(item => item.Name == IsolatedKeyLoadFaultRegressionProtocol.Admin).GetValueAsync(token);
        IsolatedKeyLoadFaultRegressionProtocol.Require(!string.IsNullOrWhiteSpace(admin));
        using var http = IsolatedKeyLoadPublicRegressionProtocol.CreateHttp(app, 1);
        var sdk = new KeyLoadClient(http, admin!);
        var seed = await IsolatedKeyLoadFaultRegressionSeed.CreateAsync(sdk, token);
        await IsolatedKeyLoadFaultRegressionAuthority.VerifyAsync(app, 1, admin!, seed, token);
        evidence.Baseline = await IsolatedKeyLoadFaultRegressionMembership.WaitAsync(app, nodes, admin!, seed.AckReceipt.Token.Position, token);
        await IsolatedKeyLoadFaultRegressionPhases.RunAsync(app, nodes, admin!, seed, native, evidence, token);
        evidence.Stage = "complete";
    }
}
