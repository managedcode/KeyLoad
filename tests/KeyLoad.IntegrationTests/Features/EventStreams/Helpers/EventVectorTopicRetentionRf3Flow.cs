using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.GraphTraversal;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class EventVectorTopicRetentionRf3Flow
{
    internal static async Task RunAsync(TwoRf3MembershipWave wave, CancellationToken token)
    {
        await PhysicalOwnerRegistrationRf3Observation.WaitAsync(wave.Application, token);
        var directory = await PhysicalOwnerDirectoryRf3Assertions.ExpectedAsync(wave.Application, wave.Profile, token);
        var credential = await PartitionMovementPublicParentRf3Administrator.PersistAsync(wave, token);
        var seeds = new[]
        {
            await EventVectorTopicRetentionRf3Seed.CreateAsync(wave, credential, TwoRf3MembershipProtocol.Node1, token),
            await EventVectorTopicRetentionRf3Seed.CreateAsync(wave, credential, TwoRf3MembershipProtocol.Node4, token)
        };
        var open = new EventFeedControlRequest
        {
            Version = 1,
            CommandId = Guid.NewGuid(),
            MapId = Guid.NewGuid(),
            ControlPartition = seeds[0].Source.Partition,
            Action = EventFeedControlAction.Open,
            Scope = new()
            {
                Domain = seeds[0].Source.Partition.TransactionDomainId,
                Resource = EventVectorTopicRetentionRf3Seed.Topic,
                Kind = EventSourceKind.Topic
            },
            Start = EventFeedStartPolicy.FromBeginning,
            Limit = 3
        };
        var opened = await EventFeedFirstOpenRf3Flow.RoutesAsync(wave, credential, open, token);
        await EventFeedFirstOpenRf3Assertions.OpenAsync(open, opened);
        foreach (var seed in seeds)
        {
            _ = await EventVectorTopicRetentionRf3Routes.RefuseAsync(wave, credential, seed, token);
            _ = await EventVectorTopicRetentionRf3Assertions.SourceAsync(wave, credential, seed, false, false, token);
        }
        var before = await NativeRemoteGraphRf3Cuts.CaptureAsync(wave, directory, token);
        await wave.RestartJoinedAsync(token);
        foreach (var seed in seeds)
        {
            _ = await EventVectorTopicRetentionRf3Routes.RefuseAsync(wave, credential, seed, token);
            _ = await EventVectorTopicRetentionRf3Assertions.SourceAsync(wave, credential, seed, false, false, token);
        }
        var after = await NativeRemoteGraphRf3Cuts.CaptureAsync(wave, directory, token);
        await EventVectorTopicRetentionRf3ColdAssertions.RequireAsync(before, after, seeds, token);
        await wave.RestartJoinedAsync(token);
        await ReleaseAsync(wave, credential, open, opened, token);
        await HealthyAsync(wave, credential, directory, seeds, token);
    }

    private static async Task ReleaseAsync(TwoRf3MembershipWave wave, string credential,
        EventFeedControlRequest open, EventFeedControlResult opened, CancellationToken token)
    {
        var request = open with
        {
            CommandId = Guid.NewGuid(),
            Action = EventFeedControlAction.Release,
            ExpectedRevision = opened.Revision,
            ExpectedCoverageGeneration = opened.CoverageGeneration,
            Cursor = opened.Cursor
        };
        var released = await EventFeedSettlementRf3Flow.RoutesAsync(wave, credential, request, token);
        await EventFeedSettlementRf3Assertions.ResultAsync(request, released, opened.Revision, released: true);
    }

    private static async Task HealthyAsync(TwoRf3MembershipWave wave, string credential, PhysicalOwnerDirectoryV1 directory,
        EventVectorTopicRetentionRf3Seed[] seeds, CancellationToken token)
    {
        var successful = new List<(EventVectorTopicRetentionRf3Seed Seed, CommandRequest Request, CommitReceipt Receipt)>();
        foreach (var seed in seeds)
        {
            var purge = seed.Refused with { CommandId = Guid.NewGuid() };
            var receipt = await EventVectorTopicRetentionRf3Routes.CommitAsync(wave, credential, seed, purge, token);
            await SqlRf3Protocol.EqualAsync(new MutationReceipt("purgeTopic", EventVectorTopicRetentionRf3Seed.Topic, "2", 2), receipt.Mutations.Single());
            successful.Add((seed, purge, receipt));
            _ = await EventVectorTopicRetentionRf3Assertions.SourceAsync(wave, credential, seed, true, false, token);
            var next = new CommandRequest(Guid.NewGuid(), seed.Source.Partition,
                [new PublishTopic(EventVectorTopicRetentionRf3Seed.Topic, [new("fourth", "Created", "{\"n\":4}")])]);
            successful.Add((seed, next, await EventVectorTopicRetentionRf3Routes.CommitAsync(wave, credential, seed, next, token)));
            await SqlRf3Protocol.EqualAsync(seed.Receipt, await EventVectorTopicRetentionRf3Routes.CommitAsync(
                wave, credential, seed, seed.Publication, token));
        }
        var before = await NativeRemoteGraphRf3Cuts.CaptureAsync(wave, directory, token);
        await wave.RestartJoinedAsync(token);
        foreach (var value in successful)
        {
            await SqlRf3Protocol.EqualAsync(value.Receipt, await EventVectorTopicRetentionRf3Routes.CommitAsync(
            wave, credential, value.Seed, value.Request, token));
        }
        foreach (var seed in seeds)
        {
            await SqlRf3Protocol.EqualAsync(seed.Receipt, await EventVectorTopicRetentionRf3Routes.CommitAsync(
                wave, credential, seed, seed.Publication, token));
            _ = await EventVectorTopicRetentionRf3Assertions.SourceAsync(wave, credential, seed, true, true, token);
        }
        var after = await NativeRemoteGraphRf3Cuts.CaptureAsync(wave, directory, token);
        await EventVectorTopicRetentionRf3ColdAssertions.RequireAsync(before, after, seeds, token,
            successful.Select(value => (value.Seed, value.Request, value.Receipt)).ToArray());
    }
}
