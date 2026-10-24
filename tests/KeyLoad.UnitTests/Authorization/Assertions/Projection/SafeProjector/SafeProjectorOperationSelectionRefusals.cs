using KeyLoad.Query;

namespace KeyLoad.UnitTests.Authorization;

internal static class SafeProjectorOperationSelectionRefusals
{
    internal static async Task VerifyAsync(SafeProjectorOperationDatabase owner, QueryEngine query, CancellationToken token)
    {
        foreach (var objectValue in new[] { false, true })
        {
            var source = objectValue ? SafeProjectorOperationSelectionCases.ObjectSource : SafeProjectorOperationSelectionCases.ArraySource;
            var path = objectValue ? SafeProjectorOperationSelectionCases.ObjectPath : SafeProjectorOperationSelectionCases.ArrayPath;
            await SafeProjectorOperationImages.ObserveAsync(owner, async () =>
            {
                var actual = Assert.ThrowsExactly<KeyLoadException>(() => query.Execute(SafeProjectorOperationProtocol.Reader,
                    SafeProjectorOperationSelectionCases.Sql(owner.Source.Partition, SafeProjectorOperationProtocol.Collection,
                        SafeProjectorOperationProtocol.First, source), cancellationToken: token));
                await ErrorAsync(actual, ErrorCode.UnsupportedCapability, SafeProjectorOperationSelectionCases.UnsupportedScalar);
            });
            await SafeProjectorOperationImages.ObserveAsync(owner, async () =>
            {
                var actual = Assert.ThrowsExactly<KeyLoadException>(() => query.ExecuteAst(SafeProjectorOperationProtocol.Reader,
                    SafeProjectorOperationSelectionCases.Ast(owner.Source.Partition, SafeProjectorOperationProtocol.Collection,
                        SafeProjectorOperationProtocol.First, path), cancellationToken: token));
                await ErrorAsync(actual, ErrorCode.UnsupportedCapability, SafeProjectorOperationSelectionCases.UnsupportedScalar);
            });
        }
        await CollisionAsync(owner, query, token);
    }

    private static async Task CollisionAsync(SafeProjectorOperationDatabase owner, QueryEngine query, CancellationToken token)
    {
        await SafeProjectorOperationImages.ObserveAsync(owner, async () =>
        {
            var actual = Assert.ThrowsExactly<KeyLoadException>(() => query.Execute(SafeProjectorOperationProtocol.Reader,
                SafeProjectorOperationSelectionCases.Sql(owner.Source.Partition, SafeProjectorOperationProtocol.Collection,
                    SafeProjectorOperationProtocol.First, SafeProjectorOperationSelectionCases.DuplicateSource), cancellationToken: token));
            await ErrorAsync(actual, ErrorCode.Validation, SafeProjectorOperationSelectionCases.DuplicateSqlDetail);
        });
        await SafeProjectorOperationImages.ObserveAsync(owner, async () =>
        {
            var request = SafeProjectorOperationSelectionCases.Ast(owner.Source.Partition, SafeProjectorOperationProtocol.Collection,
                SafeProjectorOperationProtocol.First, SafeProjectorOperationProtocol.LiteralPath);
            var actual = Assert.ThrowsExactly<KeyLoadException>(() => query.ExecuteAst(SafeProjectorOperationProtocol.Reader,
                request with
                {
                    Query = request.Query with
                    {
                        Projection = [new(SafeProjectorOperationProtocol.LiteralPath, SafeProjectorOperationProtocol.Exposed),
                            new(SafeProjectorOperationProtocol.NullPath, SafeProjectorOperationProtocol.Exposed)]
                    }
                }, cancellationToken: token));
            await ErrorAsync(actual, ErrorCode.Validation, SafeProjectorOperationSelectionCases.DuplicateAliases);
        });
    }

    private static async Task ErrorAsync(KeyLoadException actual, ErrorCode code, string detail)
    {
        await Assert.That(actual.Code).IsEqualTo(code);
        await Assert.That(StringComparer.Ordinal.Equals(actual.Message, detail)).IsTrue();
        await Assert.That(actual.Message?.Contains(SafeProjectorOperationProtocol.Canary, StringComparison.Ordinal) is true).IsFalse();
    }
}
