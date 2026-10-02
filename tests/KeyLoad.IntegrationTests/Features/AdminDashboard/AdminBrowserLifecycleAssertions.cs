using System.Text.Json;

namespace KeyLoad.IntegrationTests.Features.AdminDashboard;

internal static class AdminBrowserLifecycleAssertions
{
    private const string EnableNetwork = "Network.enable";
    private const string RefreshBurst = "(()=>{for(let index=0;index<25;index++)document.getElementById('refresh').click();return true;})()";
    private const string ObservationText = "document.getElementById('captured-at').textContent";
    private const string ObservationReady = "document.getElementById('status-message').textContent==='Live observations updated.'";
    private const string ObservationChanged = "document.getElementById('captured-at').textContent!==VALUE";
    private const string GetTarget = "Target.getTargetInfo";
    private const string TargetInfo = "targetInfo";
    private const string TargetId = "targetId";
    private const string CreateTarget = "Target.createTarget";
    private const string ActivateTarget = "Target.activateTarget";
    private const string CloseTarget = "Target.closeTarget";
    private const string BlankPage = "about:blank";
    private const string Hidden = "document.hidden===true";
    private const string Visible = "document.hidden===false";
    private const string EmulateMedia = "Emulation.setEmulatedMedia";
    private const string ReducedMotion = "prefers-reduced-motion";
    private const string Reduce = "reduce";
    private const string MotionDisabled = "matchMedia('(prefers-reduced-motion: reduce)').matches&&getComputedStyle(document.getElementById('refresh')).transitionDuration==='0s'&&getComputedStyle(document.getElementById('refresh')).animationName==='none'";
    private static readonly TimeSpan PollSuspensionObservation = TimeSpan.FromSeconds(11);

    internal static async Task BeginNetworkAsync(AdminBrowserCdp browser, CancellationToken cancellationToken)
        => await browser.CommandAsync(EnableNetwork, new { }, cancellationToken);

    internal static async Task VerifyRefreshAndMotionAsync(AdminBrowserCdp browser, CancellationToken cancellationToken)
    {
        await browser.EvaluateAsync(RefreshBurst, cancellationToken);
        await browser.WaitAsync(ObservationReady, cancellationToken);
        await Assert.That(browser.Network.MaximumConcurrent).IsEqualTo(1);
        await browser.CommandAsync(EmulateMedia, new { features = new[] { new { name = ReducedMotion, value = Reduce } } }, cancellationToken);
        await Assert.That((await browser.EvaluateAsync(MotionDisabled, cancellationToken)).GetBoolean()).IsTrue();
    }

    internal static async Task VerifyBackgroundSuspensionAsync(AdminBrowserCdp browser, CancellationToken cancellationToken)
    {
        var original = await browser.CommandAsync(GetTarget, new { }, cancellationToken);
        var originalId = original.GetProperty(TargetInfo).GetProperty(TargetId).GetString();
        var background = await browser.CommandAsync(CreateTarget, new { url = BlankPage, background = false }, cancellationToken);
        var foregroundId = background.GetProperty(TargetId).GetString();
        try
        {
            await browser.CommandAsync(ActivateTarget, new { targetId = foregroundId }, cancellationToken);
            await browser.WaitAsync(Hidden, cancellationToken);
            await browser.WaitForNetworkIdleAsync(cancellationToken);
            var before = (await browser.EvaluateAsync(ObservationText, cancellationToken)).GetString();
            await Task.Delay(PollSuspensionObservation, cancellationToken);
            var after = (await browser.EvaluateAsync(ObservationText, cancellationToken)).GetString();
            await Assert.That(after).IsEqualTo(before);
            await browser.CommandAsync(ActivateTarget, new { targetId = originalId }, cancellationToken);
            await browser.WaitAsync(Visible, cancellationToken);
            await browser.WaitAsync(ObservationChanged.Replace("VALUE", JsonSerializer.Serialize(before), StringComparison.Ordinal), cancellationToken);
        }
        finally { await browser.CommandAsync(CloseTarget, new { targetId = foregroundId }, cancellationToken); }
    }
}
