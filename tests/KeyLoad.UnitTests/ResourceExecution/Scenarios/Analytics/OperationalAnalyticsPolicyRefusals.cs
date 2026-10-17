using KeyLoad.Clustering;
using KeyLoad.Server;
using Microsoft.Extensions.Options;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class OperationalAnalyticsPolicyRefusals
{
    private const string ExpectedInvalidPolicy = "The private operational analytics policy is invalid.";

    internal static async Task RunAsync(OperationalAnalyticsNativeOwner owner, CancellationToken token)
    {
        var before = OperationalAnalyticsPolicyAssertions.Capture(owner);
        var count = 0;
        foreach (var policy in OperationalAnalyticsPolicyInputs.Counts()
            .Concat(OperationalAnalyticsPolicyInputs.Times()).Append(OperationalAnalyticsPolicyInputs.DiskRelation()))
        {
            token.ThrowIfCancellationRequested();
            await RefuseOneAsync(owner, policy);
            await OperationalAnalyticsPolicyAssertions.UnchangedAsync(owner, before);
            count++;
        }
        await Assert.That(count).IsEqualTo(OperationalAnalyticsPolicyInputs.ExpectedRefusals);
    }

    private static async Task RefuseOneAsync(OperationalAnalyticsNativeOwner owner, OperationalAnalyticsOptions policy)
    {
        Exception? observed = null;
        var constructionReturned = false;
        var failures = new List<Exception>();
        try
        {
            await using var unexpected = new OperationalAnalyticsOwner(owner.NodeDirectory,
                Options.Create(policy), TimeProvider.System);
            constructionReturned = true;
            await RequireRefusalAsync(null, failures);
        }
        catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure))
        {
            if (constructionReturned)
            {
                failures.Add(failure);
            }
            else
            {
                observed = failure;
            }
        }
        catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure))
        {
            if (constructionReturned)
            {
                failures.Add(failure);
            }
            else
            {
                observed = failure;
            }
        }
        if (!constructionReturned)
        {
            await RequireRefusalAsync(observed, failures);
        }
        if (failures.Count != 0)
        {
            owner.RetainEvidence();
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task RequireRefusalAsync(Exception? observed, List<Exception> failures)
    {
        if (observed is not null && observed.GetType() != typeof(OptionsValidationException))
        {
            failures.Add(observed);
        }
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(observed);
            await Assert.That(observed.GetType()).IsEqualTo(typeof(OptionsValidationException));
            var refused = (OptionsValidationException)observed;
            await Assert.That(refused.OptionsType).IsEqualTo(typeof(OperationalAnalyticsOptions));
            await Assert.That(refused.OptionsName).IsEqualTo(string.Empty);
            await Assert.That(refused.Failures).IsEquivalentTo(new[] { ExpectedInvalidPolicy }, CollectionOrdering.Matching);
        }, failures);
    }
}
