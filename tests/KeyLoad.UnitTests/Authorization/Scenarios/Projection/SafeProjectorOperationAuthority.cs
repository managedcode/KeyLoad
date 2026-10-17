using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.Query;
using KeyLoad.Server;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.Authorization;

internal static class SafeProjectorOperationAuthority
{
    private const string Point = "point";
    private const string FullScan = "bounded-full-scan";

    internal static async Task<SafeProjectorOperationCommand> SetAsync(SafeProjectorOperationDatabase owner,
        long epoch, ImmutableArray<string> grants)
    {
        var id = Guid.NewGuid();
        var expected = SafeProjectorOperationSeed.Principal(owner, epoch, grants);
        var excluded = new[]
        {
            KeySpace.Principal(SafeProjectorOperationProtocol.Reader),
            KeySpace.GlobalOutcome(SafeProjectorOperationProtocol.Root, id),
            KeySpace.ClockBytes,
            KeySpace.AppliedBytes
        };
        var before = SafeProjectorOperationImages.Without(owner, excluded);
        var command = SafeProjectorOperationSeed.Issue(owner, OperationKind.ConfigurePrincipal, id,
            new ConfigurePrincipalRequest(expected));
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => SafeProjectorOperationSeed.EqualAsync(
            command.Result.Get<PrincipalRecord>(), expected), failures);
        await ServerFailureObserver.ObserveAsync(() => SafeProjectorOperationSeed.EqualAsync(
            owner.Store.Read(view => view.GetRecord<PrincipalRecord>(
                KeySpace.Principal(SafeProjectorOperationProtocol.Reader))), expected), failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await Assert.That(SafeProjectorOperationImages.Without(owner, excluded).SequenceEqual(before, StringComparer.Ordinal)).IsTrue();
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return command;
    }

    internal static async Task UsageAsync(SafeProjectorOperationDatabase owner, bool allowed, string expectedJson,
        long revision, bool raw, CancellationToken token)
    {
        var queries = new QueryEngine(owner.Database, UnitExecutionOptions.QueryExecution());
        foreach (var order in new[] { false, true })
        {
            await SafeProjectorOperationImages.ObserveAsync(owner, async () =>
            {
                var request = new QueryRequest(owner.Source.Partition, order ? SafeProjectorOperationRequests.OrderSql
                    : SafeProjectorOperationRequests.PredicateSql, AllowFullScan: true);
                if (!allowed)
                {
                    await DeniedAsync(Assert.ThrowsExactly<KeyLoadException>(() => queries.Execute(
                        SafeProjectorOperationProtocol.Reader, request, cancellationToken: token)));
                    return;
                }
                var cut = owner.Store.Position;
                await PageAsync(queries.Execute(SafeProjectorOperationProtocol.Reader, request, cancellationToken: token),
                    expectedJson, revision, raw, cut, order ? Point : FullScan);
            });
            await AstUsageAsync(owner, queries, order, allowed, expectedJson, revision, raw, token);
        }
    }

    private static async Task AstUsageAsync(SafeProjectorOperationDatabase owner, QueryEngine queries, bool order,
        bool allowed, string expectedJson, long revision, bool raw, CancellationToken token)
        => await SafeProjectorOperationImages.ObserveAsync(owner, async () =>
        {
            var request = SafeProjectorOperationRequests.Usage(owner.Source.Partition, order);
            if (!allowed)
            {
                await DeniedAsync(Assert.ThrowsExactly<KeyLoadException>(() => queries.ExecuteAst(
                    SafeProjectorOperationProtocol.Reader, request, cancellationToken: token)));
                return;
            }
            var cut = owner.Store.Position;
            await PageAsync(queries.ExecuteAst(SafeProjectorOperationProtocol.Reader, request, cancellationToken: token),
                expectedJson, revision, raw, cut, order ? Point : FullScan);
        });

    private static async Task PageAsync(QueryPage actual, string json, long revision, bool raw, long cut, string access)
    {
        var paths = raw ? ImmutableArray<string>.Empty : SafeProjectorOperationProtocol.RedactedPaths;
        await SafeProjectorOperationSeed.EqualAsync(actual, new QueryPage(
            [new(SafeProjectorOperationProtocol.First, revision, json, !raw, paths)], null, cut, access));
    }

    private static async Task DeniedAsync(KeyLoadException actual)
    {
        await Assert.That(actual.Code).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(StringComparer.Ordinal.Equals(actual.Message, SafeProjectorOperationProtocol.UseDenied)).IsTrue();
        await Assert.That(actual.Message?.Contains(SafeProjectorOperationProtocol.Canary, StringComparison.Ordinal) is true).IsFalse();
    }
}
