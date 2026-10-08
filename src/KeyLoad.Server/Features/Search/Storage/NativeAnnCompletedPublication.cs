using KeyLoad.Core.Features.Search;
using KeyLoad.Query.Features.Search;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.Search;

internal static class NativeAnnCompletedPublication
{
    internal static NativeAnnManifest Save(NativeAnnGenerationOwner owner, NativeAnnRootLease root,
        List<NativeAnnGenerationSlot> slots, NativeAnnExecutionOptions options, IOptions<PackedAnnOptions> policy,
        IOptions<PackedAnnStorageOptions> storage, AnnMaintenanceRequest request, AnnSeed seed, PackedAnnIndex index,
        CommitProjectionBatchRequest? intent, AnnWorkBudget budget, long? checkpointAfter)
    {
        var leaf = NativeAnnProtocol.GenerationPrefix + Guid.NewGuid().ToString(NativeAnnProtocol.GuidFormat);
        var stageStorage = NativeAnnDiskAdmission.ForStage(root.Root, root.Receipt, storage, options);
        root.Enroll(leaf, options);
        var published = false;
        try
        {
            var manifest = NativeAnnCanonicalSource.Manifest(request, seed, leaf, policy.Value)
                with
            { CheckpointIntent = intent, ReplayAfter = checkpointAfter ?? seed.ProjectionCheckpoint ?? seed.Cut.OutboxTail };
            var saved = NativeAnnGenerationFiles.Save(root.Root, root.Receipt, manifest, index, stageStorage, options, budget);
            budget.Check();
            var key = new NativeAnnOwnedKey(request.Consumer, request.IndexGeneration);
            var pending = root.Receipt.Pending.SingleOrDefault(item => item.Key == key);
            root.Publish(key, saved.Pointer, options);
            published = true;
            if (pending is not null)
            { root.Remove(pending.Pointer.GenerationLeaf, options); }
            foreach (var slot in slots.Where(item => !item.Retired && NativeAnnSlotRetention.SameKey(item.Manifest, key)))
            { slot.Retire(); }
            slots.Add(new(owner, saved.Manifest, index));
            NativeAnnSlotRetention.RetireUnpinned(root, slots, options);
            return saved.Manifest;
        }
        catch (Exception primary)
        {
            if (!published)
            {
                try
                { root.Remove(leaf, options); }
                catch (Exception cleanup) { throw new AggregateException(primary, cleanup); }
            }
            throw;
        }
    }

}
