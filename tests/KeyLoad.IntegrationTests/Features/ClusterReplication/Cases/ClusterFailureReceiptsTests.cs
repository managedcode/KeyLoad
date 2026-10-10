using System.Globalization;
using TUnit.Assertions.Enums;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>AC-TEST-007: actual bounded diagnostic files survive subsequent and concurrent failures.</summary>
internal sealed class ClusterFailureReceiptsTests
{
    private const int MaximumReceipts = 32;
    private const int ExtraFailures = 3;
    private const int ConcurrentFailures = 64;
    private const int LinesPerReceipt = 2;
    private const string FirstLine = "first bounded failure";
    private const string SecondLine = "second bounded failure";
    private const string TailLine = "bounded tail";
    private const string FilePrefix = "rf3-failure-";
    private const string FileSuffix = ".log";

    [Test]
    public async Task AcTest007FirstReceiptAndLatestFailureAreBothPreserved()
    {
        using var scope = new ReceiptDirectory();
        var receipts = new ClusterFailureReceipts();
        receipts.Save(scope.Path, [FirstLine, TailLine]);
        receipts.Save(scope.Path, [SecondLine, TailLine]);

        await Assert.That(await File.ReadAllLinesAsync(ReceiptPath(scope.Path, 1), TestContext.Current!.Execution.CancellationToken))
            .IsEquivalentTo(new[] { FirstLine, TailLine }, CollectionOrdering.Matching);
        await Assert.That(await File.ReadAllLinesAsync(ReceiptPath(scope.Path, 2), TestContext.Current!.Execution.CancellationToken))
            .IsEquivalentTo(new[] { SecondLine, TailLine }, CollectionOrdering.Matching);
        await Assert.That(await File.ReadAllLinesAsync(System.IO.Path.Combine(scope.Path, ClusterFixtureProtocol.DiagnosticsFileName),
                TestContext.Current!.Execution.CancellationToken))
            .IsEquivalentTo(new[] { SecondLine, TailLine }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task AcTest007IndividualReceiptsKeepFirst32WhileLatestViewContinues()
    {
        using var scope = new ReceiptDirectory();
        var receipts = new ClusterFailureReceipts();
        for (var index = 1; index <= MaximumReceipts + ExtraFailures; index++)
        {
            receipts.Save(scope.Path, [index.ToString(CultureInfo.InvariantCulture)]);
        }

        await Assert.That(Directory.GetFiles(scope.Path)).Count().IsEqualTo(MaximumReceipts + 1);
        await Assert.That(await File.ReadAllLinesAsync(ReceiptPath(scope.Path, 1), TestContext.Current!.Execution.CancellationToken))
            .IsEquivalentTo(new[] { "1" }, CollectionOrdering.Matching);
        await Assert.That(await File.ReadAllLinesAsync(ReceiptPath(scope.Path, MaximumReceipts), TestContext.Current!.Execution.CancellationToken))
            .IsEquivalentTo(new[] { MaximumReceipts.ToString(CultureInfo.InvariantCulture) }, CollectionOrdering.Matching);
        await Assert.That(File.Exists(ReceiptPath(scope.Path, MaximumReceipts + 1))).IsFalse();
        await Assert.That(await File.ReadAllLinesAsync(System.IO.Path.Combine(scope.Path, ClusterFixtureProtocol.DiagnosticsFileName),
                TestContext.Current!.Execution.CancellationToken))
            .IsEquivalentTo(new[] { (MaximumReceipts + ExtraFailures).ToString(CultureInfo.InvariantCulture) }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task AcTest007ConcurrentFailureWritesStayWholeAndBounded()
    {
        using var scope = new ReceiptDirectory();
        var receipts = new ClusterFailureReceipts();
        Parallel.For(0, ConcurrentFailures, index =>
        {
            var line = index.ToString(CultureInfo.InvariantCulture);
            receipts.Save(scope.Path, [line, line]);
        });

        var files = Directory.GetFiles(scope.Path);
        await Assert.That(files).Count().IsEqualTo(MaximumReceipts + 1);
        foreach (var file in files)
        {
            var lines = await File.ReadAllLinesAsync(file, TestContext.Current!.Execution.CancellationToken);
            await Assert.That(lines).Count().IsEqualTo(LinesPerReceipt);
            await Assert.That(lines[0]).IsEqualTo(lines[1]);
        }
    }

    [Test]
    public async Task AcTest007SeparateNativeRunnerRootsRetainCompleteOriginalReceipts()
    {
        using var scope = new ReceiptDirectory();
        var normal = System.IO.Path.Combine(scope.Path, "normal");
        var scalar = System.IO.Path.Combine(scope.Path, "scalar");
        var first = new ClusterFailureReceipts();
        var second = new ClusterFailureReceipts();
        var originalOutput = first.RequireRunOutput(normal);
        var secondOutput = second.RequireRunOutput(scalar);
        Directory.CreateDirectory(originalOutput);
        Directory.CreateDirectory(secondOutput);
        first.Save(originalOutput, [FirstLine, TailLine]);
        second.Save(secondOutput, [SecondLine, TailLine]);
        await Assert.That(() => first.RequireRunOutput(scalar)).Throws<InvalidOperationException>();
        await ExactRunnerFilesAsync(originalOutput, FirstLine);
        await ExactRunnerFilesAsync(secondOutput, SecondLine);
        await Assert.That(first.RequireRunOutput(normal)).IsEqualTo(originalOutput);
        first.Save(originalOutput, [TailLine, FirstLine]);
        var token = TestContext.Current!.Execution.CancellationToken;
        await Assert.That(await File.ReadAllLinesAsync(ReceiptPath(originalOutput, 1), token))
            .IsEquivalentTo(new[] { FirstLine, TailLine }, CollectionOrdering.Matching);
        await Assert.That(await File.ReadAllLinesAsync(ReceiptPath(originalOutput, 2), token))
            .IsEquivalentTo(new[] { TailLine, FirstLine }, CollectionOrdering.Matching);
        await ExactRunnerFilesAsync(secondOutput, SecondLine);
    }

    private static async Task ExactRunnerFilesAsync(string output, string line)
    {
        var expected = System.Text.Encoding.UTF8.GetBytes(line + Environment.NewLine + TailLine + Environment.NewLine);
        var token = TestContext.Current!.Execution.CancellationToken;
        foreach (var path in new[] { ReceiptPath(output, 1),
            System.IO.Path.Combine(output, ClusterFixtureProtocol.DiagnosticsFileName) })
        { await Assert.That((await File.ReadAllBytesAsync(path, token)).SequenceEqual(expected)).IsTrue(); }
    }

    private static string ReceiptPath(string directory, int number)
        => System.IO.Path.Combine(directory, FilePrefix + number.ToString("D4", CultureInfo.InvariantCulture) + FileSuffix);

    private sealed class ReceiptDirectory : IDisposable
    {
        private const string DirectoryPrefix = "keyload-failure-receipts-";

        internal string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), DirectoryPrefix + Guid.NewGuid().ToString("N"));

        internal ReceiptDirectory() => Directory.CreateDirectory(Path);

        public void Dispose() => Directory.Delete(Path, true);
    }
}
