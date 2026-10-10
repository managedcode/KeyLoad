using KeyLoad.Orleans;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal static class RequestCqrsCohortObservationAssertions
{
    private const int RequiredMajority = 2;
    private const int RemoteCount = 2;
    private const int ReadyCohort = 3;

    internal static async Task RequireAsync(RequestCqrsCohortScenario scenario,
        ReplicaCohortAdmissionCode code, int evaluated, int fresh, int missing, int expired, int ready)
    {
        var firstRequests = scenario.RemoteOne.Requests;
        var secondRequests = scenario.RemoteTwo.Requests;
        var actual = scenario.Client.ObserveCompatibleCohort();
        await Assert.That(actual.Code).IsEqualTo(code);
        await Assert.That(actual.RequiredMajority).IsEqualTo(RequiredMajority);
        await Assert.That(actual.EvaluatedRemote).IsEqualTo(evaluated);
        await Assert.That(actual.FreshRemote).IsEqualTo(fresh);
        await Assert.That(actual.MissingRemote).IsEqualTo(missing);
        await Assert.That(actual.ExpiredRemote).IsEqualTo(expired);
        await Assert.That(actual.Ready).IsEqualTo(ready);
        await Assert.That(actual.Compatible).IsEqualTo(code == ReplicaCohortAdmissionCode.Compatible);
        await Assert.That(scenario.RemoteOne.Requests).IsEqualTo(firstRequests);
        await Assert.That(scenario.RemoteTwo.Requests).IsEqualTo(secondRequests);
    }

    internal static async Task RefuseResolutionAsync(RequestCqrsCohortScenario scenario,
        string voter, string detail, CancellationToken token)
    {
        var original = await Assert.ThrowsExactlyAsync<KeyLoadException>(() =>
            scenario.Client.ResolveAsync(voter, true, token));
        await RequireOriginalFailureAsync(original, detail);
    }

    internal static async Task RefuseAdmissionAsync(RequestCqrsCohortScenario scenario,
        string detail, CancellationToken token)
    {
        var original = await Assert.ThrowsExactlyAsync<KeyLoadException>(() =>
            scenario.Client.EnsureCompatibleCohortAsync(token));
        await RequireOriginalFailureAsync(original, detail);
        await Assert.That(scenario.Client.HasCompatibleCohort).IsFalse();
    }

    private static async Task RequireOriginalFailureAsync(KeyLoadException? original, string detail)
    {
        var failure = original ?? throw new InvalidOperationException("The original native cohort refusal is absent.");
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.OwnershipLost);
        await Assert.That(failure.Message).IsEqualTo(detail);
    }

    internal static async Task ResolveAsync(RequestCqrsCohortScenario scenario,
        ReplicaSiloDiscovery original, CancellationToken token)
    {
        var before = original.VoterId == RequestCqrsCohortScenario.FirstRemote
            ? scenario.RemoteOne.Requests : scenario.RemoteTwo.Requests;
        var address = await scenario.Client.ResolveAsync(original.VoterId, true, token);
        await Assert.That(address).IsEqualTo(SiloAddress.FromParsableString(original.SiloAddress));
        var after = original.VoterId == RequestCqrsCohortScenario.FirstRemote
            ? scenario.RemoteOne.Requests : scenario.RemoteTwo.Requests;
        await Assert.That(after).IsEqualTo(before + 1);
        await Assert.That(scenario.Options.ClusterId).IsEqualTo(original.ClusterId);
        await Assert.That(scenario.Configuration.Incarnation).IsEqualTo(original.Incarnation);
        await Assert.That(scenario.Configuration.VoterIds.Contains(original.VoterId)).IsTrue();
    }

    internal static async Task RepairAsync(RequestCqrsCohortScenario scenario,
        ReplicaSiloDiscovery first, ReplicaSiloDiscovery second, CancellationToken token)
    {
        scenario.PublishRemoteOneRecord(first);
        scenario.PublishRemoteTwoRecord(second);
        await ResolveAsync(scenario, first, token);
        await ResolveAsync(scenario, second, token);
        await scenario.Client.EnsureCompatibleCohortAsync(token);
        await RequireAsync(scenario, ReplicaCohortAdmissionCode.Compatible,
            RemoteCount, RemoteCount, 0, 0, ReadyCohort);
        await Assert.That(scenario.Client.HasCompatibleCohort).IsTrue();
    }
}
