using KeyLoad.AppHost.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class RequestCqrsProbeAppHostPathTests
{
    private const string InvalidConfiguration = "RequestCqrsProbeConfigurationInvalid";
    private const UnixFileMode PrivateDirectory = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode PrivateFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    [Test]
    public async Task ExactPrivateOwnedThreeVoterTreeIsAccepted()
    {
        await RequestCqrsProbeAppHostFileFixture.WithFixtureAsync(async fixture =>
        {
            var nodes = RequestCqrsProbeProfilePaths.Validate(fixture.Root, fixture.SessionId, fixture.DataRoot);
            await Assert.That(nodes.Count).IsEqualTo(3);
            foreach (var voter in new[] { "node1", "node2", "node3" })
            {
                await Assert.That(nodes[voter]).IsEqualTo(fixture.Node(voter));
                await Assert.That(RequestCqrsProbeAppHostNativeFiles.GetMode(fixture.Node(voter))).IsEqualTo(PrivateDirectory);
                await Assert.That(RequestCqrsProbeAppHostNativeFiles.GetMode(fixture.Owner(voter))).IsEqualTo(PrivateFile);
            }
        });
    }

    [Test]
    public async Task DataRootOverlapAndUnexpectedEntriesAreRejected()
    {
        await RequestCqrsProbeAppHostFileFixture.WithFixtureAsync(async fixture =>
        {
            var nestedData = Path.Combine(fixture.Root, "data");
            Directory.CreateDirectory(nestedData);
            await AssertRejectedAsync(() => RequestCqrsProbeProfilePaths.Validate(fixture.Root, fixture.SessionId, nestedData));
            Directory.Delete(nestedData);
            await AssertRejectedAsync(() => RequestCqrsProbeProfilePaths.Validate(fixture.Root, fixture.SessionId, fixture.Parent));
            var extra = Path.Combine(fixture.Root, "extra");
            Directory.CreateDirectory(extra);
            RequestCqrsProbeAppHostNativeFiles.SetMode(extra, PrivateDirectory);
            await AssertRejectedAsync(() => RequestCqrsProbeProfilePaths.Validate(fixture.Root, fixture.SessionId, fixture.DataRoot));
            Directory.Delete(extra);
        });
    }

    [Test]
    public async Task OwnerLinksDirectoriesAndFifosFailClosed()
    {
        await RequestCqrsProbeAppHostFileFixture.WithFixtureAsync(async fixture =>
        {
            var owner = fixture.Owner("node3");
            File.Delete(owner);
            var outside = Path.Combine(fixture.Parent, "outside-owner.json");
            await File.WriteAllTextAsync(outside, "{}", CancellationToken.None);
            File.CreateSymbolicLink(owner, outside);
            await AssertRejectedAsync(() => RequestCqrsProbeProfilePaths.Validate(fixture.Root, fixture.SessionId, fixture.DataRoot));
            File.Delete(owner);
            Directory.CreateDirectory(owner);
            await AssertRejectedAsync(() => RequestCqrsProbeProfilePaths.Validate(fixture.Root, fixture.SessionId, fixture.DataRoot));
            Directory.Delete(owner);
            RequestCqrsProbeAppHostNativeFiles.CreateFifo(owner);
            await AssertRejectedAsync(() => RequestCqrsProbeProfilePaths.Validate(fixture.Root, fixture.SessionId, fixture.DataRoot));
            File.Delete(owner);
        });
    }

    [Test]
    public async Task WrongModeAndOwnerIdentityFailClosed()
    {
        await RequestCqrsProbeAppHostFileFixture.WithFixtureAsync(async fixture =>
        {
            var node = fixture.Node("node2");
            RequestCqrsProbeAppHostNativeFiles.SetMode(node, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            await AssertRejectedAsync(() => RequestCqrsProbeProfilePaths.Validate(fixture.Root, fixture.SessionId, fixture.DataRoot));
            RequestCqrsProbeAppHostNativeFiles.SetMode(node, PrivateDirectory);
            fixture.WriteOwner("node2", session: "00000000000000000000000000000001");
            await AssertRejectedAsync(() => RequestCqrsProbeProfilePaths.Validate(fixture.Root, fixture.SessionId, fixture.DataRoot));
            fixture.WriteOwner("node2", ownerVoter: "http://other:8080");
            await AssertRejectedAsync(() => RequestCqrsProbeProfilePaths.Validate(fixture.Root, fixture.SessionId, fixture.DataRoot));
        });
    }

    [Test]
    public async Task MissingMalformedAndWrongModeOwnerFilesFailClosed()
    {
        await RequestCqrsProbeAppHostFileFixture.WithFixtureAsync(async fixture =>
        {
            var owner = fixture.Owner("node1");
            File.Delete(owner);
            await AssertRejectedAsync(() => RequestCqrsProbeProfilePaths.Validate(fixture.Root, fixture.SessionId, fixture.DataRoot));
            fixture.WriteOwner("node1");
            RequestCqrsProbeAppHostNativeFiles.SetMode(owner, UnixFileMode.UserRead);
            await AssertRejectedAsync(() => RequestCqrsProbeProfilePaths.Validate(fixture.Root, fixture.SessionId, fixture.DataRoot));
            RequestCqrsProbeAppHostNativeFiles.SetMode(owner, PrivateFile);
            fixture.WriteOwner("node1");
            await File.WriteAllTextAsync(owner, "{} {} ", CancellationToken.None);
            RequestCqrsProbeAppHostNativeFiles.SetMode(owner, PrivateFile);
            await AssertRejectedAsync(() => RequestCqrsProbeProfilePaths.Validate(fixture.Root, fixture.SessionId, fixture.DataRoot));
        });
    }

    [Test]
    public async Task OwnerAtExactByteLimitIsAcceptedAndOneByteOverIsRejected()
    {
        await RequestCqrsProbeAppHostFileFixture.WithFixtureAsync(async fixture =>
        {
            fixture.WriteOwner("node1", bytes: 8_192);
            await Assert.That(RequestCqrsProbeProfilePaths.Validate(fixture.Root, fixture.SessionId, fixture.DataRoot).Count).IsEqualTo(3);
            fixture.WriteOwner("node1", bytes: 8_193);
            await AssertRejectedAsync(() => RequestCqrsProbeProfilePaths.Validate(fixture.Root, fixture.SessionId, fixture.DataRoot));
        });
    }

    private static async Task AssertRejectedAsync(Action action)
    {
        var error = Assert.ThrowsExactly<InvalidOperationException>(action);
        await Assert.That(error.Message).IsEqualTo(InvalidConfiguration);
    }
}
