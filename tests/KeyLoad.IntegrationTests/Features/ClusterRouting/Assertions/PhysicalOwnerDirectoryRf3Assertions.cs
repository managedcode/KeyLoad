using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.IntegrationTests.Features.StorageRecovery;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PhysicalOwnerDirectoryRf3Assertions
{
    private const string PhysicalB = "membership-physical-b";
    private const string IncarnationB = "membership-incarnation-b";
    private const string DatabaseDirectory = "database";
    private const string DirectorySpace = "physical-owner-directory";
    private const string DirectoryFormat = "v1";
    private const int Version = 1;
    private const long FirstRevision = 1;
    private const int MaximumRecords = 4096;
    private const string NativeOriginPrefix = "http://";
    private const string NativeOriginSuffix = ":8080";
    private const string EndpointRoot = "/";
    private const string IdentityFormat = "D";

    internal static async Task<PhysicalOwnerDirectoryV1> ExpectedAsync(DistributedApplication app,
        NodeEpochRf3Profile profile, CancellationToken token)
    {
        var model = app.Services.GetRequiredService<DistributedApplicationModel>();
        var physical = Guid.ParseExact(await ParameterAsync(model, PhysicalB, token).ConfigureAwait(false), IdentityFormat);
        var incarnation = Guid.ParseExact(await ParameterAsync(model, IncarnationB, token).ConfigureAwait(false), IdentityFormat);
        var first = Entry(profile.PhysicalShardId, profile.Incarnation, TwoRf3MembershipProtocol.Nodes[..TwoRf3MembershipProtocol.MembersPerGroup]);
        var second = Entry(physical, incarnation, TwoRf3MembershipProtocol.Nodes[TwoRf3MembershipProtocol.MembersPerGroup..]);
        return new(Version, FirstRevision, first.Owner, [first, second]);
    }

    private static RegisteredPhysicalOwnerV1 Entry(Guid physical, Guid incarnation, string[] nodes)
        => new(new(physical, incarnation, [.. nodes.Select(node => NativeOriginPrefix + node + NativeOriginSuffix)], FirstRevision),
            [.. nodes.Select(node => NativeOriginPrefix + node + NativeOriginSuffix + EndpointRoot)]);

    private static async Task<string> ParameterAsync(DistributedApplicationModel model, string name, CancellationToken token)
        => await model.Resources.OfType<ParameterResource>().Single(value => value.Name == name).GetValueAsync(token)
            .ConfigureAwait(false) ?? throw new InvalidOperationException(TwoRf3MembershipProtocol.MissingState);

    internal static async Task VerifyReopenedAsync(string root, PhysicalOwnerDirectoryV1 expected)
    {
        var key = KeyCodec.Encode(DirectorySpace, DirectoryFormat);
        var expectedBytes = NativeSerialization.Serialize(expected);
        foreach (var node in TwoRf3MembershipProtocol.Nodes)
        {
            var path = Path.Combine(root, node, DatabaseDirectory);
            var failures = new List<Exception>();
            await KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                var first = Read(path, key);
                var second = Read(path, key);
                await Assert.That(second.Position).IsEqualTo(first.Position);
                await Assert.That(second.Image.SequenceEqual(first.Image)).IsTrue();
                var control = Array.IndexOf(TwoRf3MembershipProtocol.Nodes, node) < TwoRf3MembershipProtocol.MembersPerGroup;
                if (control)
                { await Assert.That(first.Directory is not null && first.Directory.AsSpan().SequenceEqual(expectedBytes)).IsTrue(); }
                else
                { await Assert.That(first.Directory).IsNull(); }
            }, failures).ConfigureAwait(false);
            KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
        }
    }

    private static (long Position, string[] Image, byte[]? Directory) Read(string path, byte[] key)
    {
        var failures = new List<Exception>();
        ZoneTreeStore? store = null;
        (long Position, string[] Image, byte[]? Directory)? result = null;
        KeyLoad.Server.ServerFailureObserver.Observe(() =>
        {
            store = new ZoneTreeStore(new(path), IntegrationExecutionOptions.StorageExecution(),
                IntegrationExecutionOptions.PointCacheExecution());
            result = store.Read(view =>
            {
                var page = view.Scan([], MaximumRecords);
                if (page.HasMore)
                { throw new InvalidOperationException("The registration store exceeded its full image bound."); }
                return (store.Position, page.Records.Select(row => Convert.ToHexString(row.Key.Span) + ":" +
                    Convert.ToHexString(row.Value.Span)).ToArray(), view.ReadOwnedValue(key));
            });
        }, failures);
        if (store is { } owned)
        { KeyLoad.Server.ServerFailureObserver.Observe(owned.Dispose, failures); }
        KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
        return result ?? throw new InvalidOperationException(TwoRf3MembershipProtocol.MissingState);
    }
}
