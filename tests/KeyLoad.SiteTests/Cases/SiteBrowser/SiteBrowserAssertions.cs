using System.Text.Json;

namespace KeyLoad.SiteTests.BenchmarkComparisons;

internal static class SiteBrowserAssertions
{
    public static Task SelectValue(SiteBrowserCdpClient cdp, string selector, string value,
        CancellationToken cancellationToken)
    {
        var expression = "(()=>{const e=document.querySelector(" + JsonSerializer.Serialize(selector) +
                         ");if(!e)return false;e.value=" + JsonSerializer.Serialize(value) +
                         ";e.dispatchEvent(new Event('change',{bubbles:true}));return true;})()";
        return EvaluateSuccess(cdp, expression, cancellationToken);
    }

    internal static async Task EvaluateSuccess(SiteBrowserCdpClient cdp, string expression,
        CancellationToken cancellationToken)
    {
        var result = await cdp.EvaluateAsync(expression, false, cancellationToken);
        await Assert.That(result.GetBoolean()).IsTrue();
    }
}
