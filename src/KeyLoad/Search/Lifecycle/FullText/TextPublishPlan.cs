namespace KeyLoad.Server.Features.Search;

internal sealed record TextPublishPlan(TextGenerationSlot? Previous, bool ReplaceCurrent);
