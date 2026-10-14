using KeyLoad.Server.Features.BlobStorage;

namespace KeyLoad.UnitTests;

internal sealed class NativeBlobWireFileCases
{
    [Test]
    public Task RealConcurrentPublicationKeepsTheFirstCompleteImmutableWinner()
        => NativeBlobWireFileFixture.RunOwnedAsync(async fixture =>
        {
            var name = NativeBlobWireNames.Claim(NativeBlobWireIdentity.BeginUpload);
            byte[][] candidates = [[1, 2, 3], [4, 5, 6]];
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var original = candidates.Select(bytes => Task.Run(async () =>
            {
                await start.Task;
                try
                {
                    fixture.Publish(name, bytes);
                    return true;
                }
                catch (IOException) { return false; }
                catch (InvalidOperationException failure) when (failure.Message == NativeBlobWireProbeProtocol.InvalidEvidence)
                {
                    return false;
                }
            })).ToArray();
            start.SetResult();
            var result = await Task.WhenAll(original);
            await Assert.That(result.Count(value => value)).IsEqualTo(1);
            var winner = fixture.Files.Read(name);
            await Assert.That(winner.AsSpan().SequenceEqual(candidates[Array.FindIndex(result, value => value)])).IsTrue();
            Assert.ThrowsExactly<InvalidOperationException>(() => fixture.Publish(name, [9]));
            await Assert.That(fixture.Files.Read(name).AsSpan().SequenceEqual(winner)).IsTrue();
        });

    [Test]
    public Task ExactOriginalRecordBoundaryRefusesBeforeAnyNewFile()
        => NativeBlobWireFileFixture.RunOwnedAsync(async fixture =>
        {
            var name = NativeBlobWireNames.Claim(NativeBlobWireIdentity.BeginUpload);
            var bytes = new byte[8192];
            fixture.Publish(name, bytes);
            var files = Directory.GetFiles(fixture.Root).Order(StringComparer.Ordinal).ToArray();
            var failedName = NativeBlobWireNames.Claim(NativeBlobWireIdentity.WritePart);
            Assert.ThrowsExactly<InvalidOperationException>(() => fixture.Publish(failedName, new byte[8193]));
            await Assert.That(fixture.Files.Exists(failedName)).IsFalse();
            await Assert.That(files.SequenceEqual(Directory.GetFiles(fixture.Root).Order(StringComparer.Ordinal))).IsTrue();
            await Assert.That(fixture.Files.Read(name).AsSpan().SequenceEqual(bytes)).IsTrue();
        });

    [Test]
    public Task AllActualTemporaryBytesChargeTheOriginalAggregateCeiling()
        => NativeBlobWireFileFixture.RunOwnedAsync(async fixture =>
        {
            var names = new List<string>();
            for (var index = 0; index < 128; index++)
            {
                var name = NativeBlobWireFileFixture.Temporary();
                fixture.Publish(name, new byte[8192]);
                names.Add(name);
            }
            await Assert.That(Directory.GetFiles(fixture.Root).Sum(path => new FileInfo(path).Length)).IsEqualTo(1048576L);
            Assert.ThrowsExactly<InvalidOperationException>(() => fixture.Publish(NativeBlobWireFileFixture.Temporary(), [1]));
            await Assert.That(Directory.GetFiles(fixture.Root).Length).IsEqualTo(129);
            foreach (var name in names)
            {
                await Assert.That(fixture.Files.Read(name).Length).IsEqualTo(8192);
            }
        });

    [Test]
    public Task AllActualZeroLengthTemporaryFilesChargeTheOriginalFileCeiling()
        => NativeBlobWireFileFixture.RunOwnedAsync(async fixture =>
        {
            for (var index = 0; index < 399; index++)
            {
                fixture.Publish(NativeBlobWireFileFixture.Temporary(), []);
            }
            await Assert.That(Directory.GetFiles(fixture.Root).Length).IsEqualTo(400);
            Assert.ThrowsExactly<InvalidOperationException>(() => fixture.Publish(NativeBlobWireFileFixture.Temporary(), [1]));
            await Assert.That(Directory.GetFiles(fixture.Root).Length).IsEqualTo(400);
            await Assert.That(Directory.GetFiles(fixture.Root).Sum(path => new FileInfo(path).Length)).IsEqualTo(0L);
        });
}
