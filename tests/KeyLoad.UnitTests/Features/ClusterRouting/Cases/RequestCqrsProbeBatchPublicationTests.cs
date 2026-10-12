using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class RequestCqrsProbeBatchPublicationTests
{
    [Test]
    public async Task StagesActualPrivateFilesThenPublishesWitnessBeforeMarkerWithoutIntermediateRead()
    {
        using var directory = RequestCqrsProbeRecordFileFixture.Create();
        using var native = new RequestCqrsNativeModelWitnessCase();
        var witness = native.Create();
        var owner = RequestCqrsProbeCodecInput.Owner(witness.SessionId, witness.Voter);
        directory.CreateRegular(RequestCqrsProbeProtocol.OwnerFile, owner);
        var name = RequestCqrsNativeModelInventory.Name(witness.ArmId, witness.RequestId, witness.Phase);
        var marker = RequestCqrsNativeModelWitnessCase.Marker(witness);
        var markerName = RequestCqrsProbeFiles.MarkerName(marker);
        var bytes = RequestCqrsNativeModelInventory.Write(witness);
        var markerBytes = UnitRequestProbeOptions.Json.WriteMarker(marker);
        var reads = 0;
        RequestCqrsProbeSnapshot Inspect()
        {
            reads++;
            if (File.Exists(Path.Combine(directory.Root, name)) || File.Exists(Path.Combine(directory.Root, markerName)))
            { throw new InvalidOperationException("Inventory read during committed publication."); }
            var paths = Directory.GetFiles(directory.Root);
            var total = paths.Sum(path => (long)RequestCqrsProbeFiles.ReadRecord(path, UnitRequestProbeOptions.Execution).Length);
            if (!RequestCqrsProbeFiles.ReadRecord(Path.Combine(directory.Root, RequestCqrsProbeProtocol.OwnerFile), UnitRequestProbeOptions.Execution).SequenceEqual(owner))
            { throw new InvalidOperationException("Original owner changed."); }
            return new([], [], [], paths.Length, total);
        }
        RequestCqrsProbeBatchPublication.Write(directory.Root, [(name, bytes), (markerName, markerBytes)], UnitRequestProbeOptions.Execution, Inspect);
        await Assert.That(reads).IsEqualTo(2);
        await Assert.That(RequestCqrsProbeFiles.ReadRecord(Path.Combine(directory.Root, name), UnitRequestProbeOptions.Execution).SequenceEqual(bytes)).IsTrue();
        await Assert.That(RequestCqrsProbeFiles.ReadRecord(Path.Combine(directory.Root, markerName), UnitRequestProbeOptions.Execution).SequenceEqual(markerBytes)).IsTrue();
        RequestCqrsNativeModelInventory.RequireMarkers([RequestCqrsNativeModelInventory.Read(bytes)], [UnitRequestProbeOptions.Json.ReadMarker(markerBytes)]);
        await Assert.That(Directory.GetFiles(directory.Root).Length).IsEqualTo(3);
    }
}
