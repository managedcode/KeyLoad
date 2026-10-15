using KeyLoad.AppHost.Features.ClusterReplication;
using KeyLoad.AppHost.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class ProtectedMovementLocalProfileTests
{
    [Test]
    public async Task ExplicitProtectedMovementRefusesForeignProfilesWithoutEffectsThenEnrollsSameSixOwners()
    {
        await RequestCqrsProbeAppHostFileFixture.WithFixtureAsync(async fixture =>
        {
            var before = ProtectedMovementProfileFixture.Capture(fixture);
            var builder = ProtectedMovementProfileFixture.Create(fixture);
            var local = LocalDevelopmentContainerImage.Read(builder)!;
            foreach (var setting in new[] { TwoRf3ProfileProtocol.ProtectedDocumentMovementSetting,
                TwoRf3ProfileProtocol.RemoteQuerySetting, ProtectedMovementProbeProfile.Setting })
            {
                var denied = ProtectedMovementProfileFixture.Create(fixture, setting,
                    ProtectedMovementProfileFixture.False);
                var error = Assert.ThrowsExactly<InvalidOperationException>(() =>
                    TwoRf3QueryProbe.Create(denied, fixture.DataRoot, null, local));
                await Assert.That(error.Message).IsEqualTo(setting == TwoRf3ProfileProtocol.RemoteQuerySetting
                    ? ProtectedMovementProfileFixture.MissingProfile : ProtectedMovementProbeProfile.Invalid);
                await Assert.That(ProtectedMovementProfileFixture.Conserved(fixture, before)).IsTrue();
            }
            var absent = ProtectedMovementProfileFixture.Create(fixture, ProtectedMovementProbeProfile.Setting, null);
            var absentError = Assert.ThrowsExactly<InvalidOperationException>(() =>
                TwoRf3QueryProbe.Create(absent, fixture.DataRoot, null, local));
            await Assert.That(absentError.Message).IsEqualTo(ProtectedMovementProfileFixture.MissingProfile);
            var nested = ProtectedMovementProfileFixture.Create(fixture,
                ProtectedMovementProbeProfile.Setting + ProtectedMovementProfileFixture.Foreign,
                ProtectedMovementProfileFixture.True);
            var nestedError = Assert.ThrowsExactly<InvalidOperationException>(() =>
                TwoRf3QueryProbe.Create(nested, fixture.DataRoot, null, local));
            await Assert.That(nestedError.Message).IsEqualTo(ProtectedMovementProbeProfile.Invalid);
            var image = RuntimeContainerImage.Read(RequestCqrsProbeAppHostBuilder.CreateBuilder(fixture),
                RuntimeContainerImage.ServerConfiguration);
            Assert.ThrowsExactly<InvalidOperationException>(() =>
                RequestCqrsProbeProfile.ReadDiagnosticSix(builder, fixture.DataRoot, image, local));
            Assert.ThrowsExactly<InvalidOperationException>(() =>
                RequestCqrsProbeProfile.ReadDiagnosticSix(builder, fixture.DataRoot, null, null));
            var profile = TwoRf3QueryProbe.Create(builder, fixture.DataRoot, null, local)!;
            await Assert.That(profile.MemberCount).IsEqualTo(TwoRf3ProfileProtocol.TotalNodes);
            await Assert.That(profile.SessionId).IsEqualTo(fixture.SessionId);
            await Assert.That(ProtectedMovementProfileFixture.Conserved(fixture, before)).IsTrue();
        }, includeDestination: true).ConfigureAwait(false);
    }
}
