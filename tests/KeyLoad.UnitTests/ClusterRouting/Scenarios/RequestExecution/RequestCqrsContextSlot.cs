namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class RequestCqrsContextSlot
{
    private readonly string key;
    private readonly bool wasPresent;
    private readonly object? original;

    private RequestCqrsContextSlot(string key)
    {
        this.key = key;
        wasPresent = RequestContext.Keys.Contains(key, StringComparer.Ordinal);
        original = RequestContext.Get(key);
    }

    internal static RequestCqrsContextSlot Capture(string key) => new(key);

    internal void SetPrior(RequestCqrsPriorContextValue prior, object? value)
    {
        if (prior == RequestCqrsPriorContextValue.Absent)
        {
            RequestContext.Remove(key);
        }
        else
        {
            RequestContext.Set(key, prior == RequestCqrsPriorContextValue.PresentNull ? null! : value!);
        }
    }

    internal async Task AssertPriorAsync(RequestCqrsPriorContextValue prior, object? value)
    {
        var expectedPresence = prior != RequestCqrsPriorContextValue.Absent;
        await Assert.That(RequestContext.Keys.Contains(key, StringComparer.Ordinal)).IsEqualTo(expectedPresence);
        var actual = RequestContext.Get(key);
        if (prior == RequestCqrsPriorContextValue.PresentNull)
        {
            await Assert.That(actual).IsNull();
        }
        else if (prior == RequestCqrsPriorContextValue.Object)
        {
            await Assert.That(ReferenceEquals(actual, value)).IsTrue();
        }
    }

    internal async Task AssertOriginalAsync()
    {
        await Assert.That(RequestContext.Keys.Contains(key, StringComparer.Ordinal)).IsEqualTo(wasPresent);
        await Assert.That(ReferenceEquals(RequestContext.Get(key), original)).IsTrue();
    }

    internal void Restore()
    {
        if (wasPresent)
        {
            RequestContext.Set(key, original!);
        }
        else
        {
            RequestContext.Remove(key);
        }
    }
}
