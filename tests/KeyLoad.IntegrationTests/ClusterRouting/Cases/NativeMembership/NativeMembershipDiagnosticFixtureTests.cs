namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed class NativeMembershipDiagnosticFixtureTests
{
    [Test]
    public async Task ExactSixPrivateOwnersAreAccepted()
    {
        if (OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(RequestCqrsProbeFixtureProtocol.PrivatePermissionsUnsupported);
        }
        await NativeMembershipDiagnosticFileScope.WithAsync(async scope =>
        {
            if (OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException(RequestCqrsProbeFixtureProtocol.PrivatePermissionsUnsupported);
            }
            var admitted = scope.Validate();
            await Assert.That(admitted.Count).IsEqualTo(TwoRf3MembershipProtocol.NodeCount);
            foreach (var node in TwoRf3MembershipProtocol.Nodes)
            {
                await Assert.That(admitted[node]).IsEqualTo(scope.Node(node));
                await Assert.That(Directory.GetFileSystemEntries(scope.Node(node)).Length).IsEqualTo(1);
                await Assert.That(File.GetUnixFileMode(scope.Node(node))).IsEqualTo(RequestCqrsProbeFileValidation.PrivateDirectoryMode);
                await Assert.That(File.GetUnixFileMode(scope.Owner(node))).IsEqualTo(RequestCqrsProbeFileValidation.PrivateFileMode);
            }
        });
    }

    [Test]
    public async Task DuplicateSessionDoesNotRewriteOwners()
    {
        await NativeMembershipDiagnosticFileScope.WithAsync(async scope =>
        {
            var original = await File.ReadAllBytesAsync(scope.Owner(TwoRf3MembershipProtocol.Node6));
            var failure = Assert.ThrowsExactly<IOException>(() => NativeMembershipDiagnosticFixture.Create(
                scope.DataRoot, Guid.ParseExact(scope.Fixture.SessionId, "N")));
            await Assert.That(failure.Message).IsEqualTo(RequestCqrsProbeFixtureProtocol.OwnershipConflict);
            await Assert.That((await File.ReadAllBytesAsync(scope.Owner(TwoRf3MembershipProtocol.Node6))).SequenceEqual(original)).IsTrue();
            await Assert.That(scope.Validate().Count).IsEqualTo(TwoRf3MembershipProtocol.NodeCount);
        });
    }

    [Test]
    public async Task ForeignSessionAndVoterAreRejected()
    {
        await NativeMembershipDiagnosticFileScope.WithAsync(async scope =>
        {
            var path = scope.Owner(TwoRf3MembershipProtocol.Node6);
            var session = Guid.NewGuid().ToString("N");
            await File.WriteAllBytesAsync(path, RequestCqrsProbeJsonWriter.Owner(session, "http://node6:8080"));
            await scope.RequireRejectedAsync();
            await File.WriteAllBytesAsync(path, RequestCqrsProbeJsonWriter.Owner(scope.Fixture.SessionId, "http://foreign:8080"));
            await scope.RequireRejectedAsync();
        });
    }

    [Test]
    public async Task MalformedAndOversizedOwnerFilesAreRejected()
    {
        await NativeMembershipDiagnosticFileScope.WithAsync(async scope =>
        {
            var path = scope.Owner(TwoRf3MembershipProtocol.Node4);
            await File.WriteAllBytesAsync(path, "{} {}"u8.ToArray());
            await scope.RequireRejectedAsync();
            await File.WriteAllBytesAsync(path, new byte[RequestCqrsProbeFixtureProtocol.MaximumRecordBytes + 1]);
            await scope.RequireRejectedAsync();
        });
    }

    [Test]
    public async Task ExtraAndMissingNodesAreRejected()
    {
        if (OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(RequestCqrsProbeFixtureProtocol.PrivatePermissionsUnsupported);
        }
        await NativeMembershipDiagnosticFileScope.WithAsync(async scope =>
        {
            if (OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException(RequestCqrsProbeFixtureProtocol.PrivatePermissionsUnsupported);
            }
            var extra = Path.Combine(scope.Fixture.Root, "node7");
            Directory.CreateDirectory(extra, RequestCqrsProbeFileValidation.PrivateDirectoryMode);
            await scope.RequireRejectedAsync();
            Directory.Delete(extra);
            Directory.Delete(scope.Node(TwoRf3MembershipProtocol.Node5), recursive: true);
            await scope.RequireRejectedAsync();
        });
    }

    [Test]
    public async Task OwnerSymlinkIsRejected()
    {
        await NativeMembershipDiagnosticFileScope.WithAsync(async scope =>
        {
            var owner = scope.Owner(TwoRf3MembershipProtocol.Node6);
            File.Delete(owner);
            File.CreateSymbolicLink(owner, scope.Owner(TwoRf3MembershipProtocol.Node1));
            await scope.RequireRejectedAsync();
            await Assert.That(File.Exists(scope.Owner(TwoRf3MembershipProtocol.Node1))).IsTrue();
        });
    }

    [Test]
    public async Task NonPrivateModesAreRejected()
    {
        if (OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(RequestCqrsProbeFixtureProtocol.PrivatePermissionsUnsupported);
        }
        await NativeMembershipDiagnosticFileScope.WithAsync(async scope =>
        {
            if (OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException(RequestCqrsProbeFixtureProtocol.PrivatePermissionsUnsupported);
            }
            var node = scope.Node(TwoRf3MembershipProtocol.Node4);
            File.SetUnixFileMode(node, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead);
            await scope.RequireRejectedAsync();
            File.SetUnixFileMode(node, RequestCqrsProbeFileValidation.PrivateDirectoryMode);
            File.SetUnixFileMode(scope.Owner(TwoRf3MembershipProtocol.Node4), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
            await scope.RequireRejectedAsync();
        });
    }

    [Test]
    public async Task CleanupBeforeActualOwnerJoinRefusesWithoutDeletingFiles()
    {
        await NativeMembershipDiagnosticFileScope.WithAsync(async scope =>
        {
            var wave = new TwoRf3MembershipWave(null);
            var original = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var node in TwoRf3MembershipProtocol.Nodes)
            {
                original.Add(node, await File.ReadAllBytesAsync(scope.Owner(node)));
            }
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => scope.Fixture.DisposeAfterOwnersJoinedAsync(wave));
            scope.Fixture.StopAdmission();
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => scope.Fixture.DisposeAfterOwnersJoinedAsync(wave));
            foreach (var (node, bytes) in original)
            {
                await Assert.That((await File.ReadAllBytesAsync(scope.Owner(node))).SequenceEqual(bytes)).IsTrue();
            }
        });
    }
}
