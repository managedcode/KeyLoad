using System.Collections.Immutable;

namespace KeyLoad.IntegrationTests.Features.Search;

internal sealed record AdaptiveFilteredPublicRf3Cohort(string Name, ImmutableArray<string>? Allowed, int[] Eligible);
