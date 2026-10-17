namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed record MovementCursorRf3Lives(LiveQuerySnapshot Source, LiveQuerySnapshot Target, LiveQuerySnapshot Official);
