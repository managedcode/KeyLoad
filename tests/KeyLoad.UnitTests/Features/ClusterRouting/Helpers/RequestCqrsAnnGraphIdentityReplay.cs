namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class RequestCqrsAnnGraphIdentityReplay
{
    internal static async Task RequireAsync(RequestCqrsClusterFixture fixture,
        AnnMaintenanceRequest original, AnnMaintenanceResult result, CancellationToken token)
    {
        var profile = original.MaintenanceProfile ?? throw new InvalidOperationException();
        await Assert.That(ReferenceEquals(original.Space, profile.Space)).IsTrue();
        var independent = original with
        {
            MaintenanceProfile = profile with { Space = profile.Space with { } }
        };
        await Assert.That(independent.Space == independent.MaintenanceProfile!.Space).IsTrue();
        await Assert.That(ReferenceEquals(independent.Space, independent.MaintenanceProfile.Space)).IsFalse();
        await Assert.That(NativeSerialization.Serialize(original).AsSpan()
            .SequenceEqual(NativeSerialization.Serialize(independent))).IsFalse();
        await RequestCqrsAnnOriginalReplay.RequireAsync(fixture, independent, result, token);
        await RequestCqrsAnnOriginalReplay.RequireAsync(fixture, original, result, token);
    }
}
