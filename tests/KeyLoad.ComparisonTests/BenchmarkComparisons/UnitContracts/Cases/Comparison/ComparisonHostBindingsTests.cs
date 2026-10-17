using KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>Rejects invalid native session cohorts before any measured request.</summary>
internal sealed class ComparisonHostBindingsTests
{
    [Test]
    [Arguments("missing")]
    [Arguments("duplicate")]
    [Arguments("extra")]
    [Arguments("malformed")]
    [Arguments("non-http")]
    [Arguments("userinfo")]
    [Arguments("query")]
    [Arguments("fragment")]
    public Task NativeSessionEndpointCohortRejectsInvalidIdentityBeforeClients(string corruption)
    {
        var settings = SessionHostStartupSupport.ValidSettings();
        var key = SessionHostStartupSupport.Endpoints + "1";
        switch (corruption)
        {
            case "missing":
                settings.Remove(key);
                break;
            case "duplicate":
                settings[key] = settings[SessionHostStartupSupport.Endpoints + "0"];
                break;
            case "extra":
                settings[SessionHostStartupSupport.Endpoints + "3"] = "http://127.0.0.1:4";
                break;
            case "malformed":
                settings[key] = "invalid-uri";
                break;
            case "non-http":
                settings[key] = "ftp://127.0.0.1:2";
                break;
            case "userinfo":
                settings[key] = "http://" + SessionHostStartupSupport.Canary + "@127.0.0.1:2";
                break;
            case "query":
                settings[key] += "/?token=" + SessionHostStartupSupport.Canary;
                break;
            case "fragment":
                settings[key] += "/#" + SessionHostStartupSupport.Canary;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(corruption));
        }
        return SessionHostStartupSupport.AssertRefusedAsync(settings, SessionHostStartupSupport.Invalid);
    }

    [Test]
    [Arguments("missing-user")]
    [Arguments("missing-password")]
    [Arguments("invalid-user")]
    [Arguments("missing-connection")]
    public Task NativeSessionAuthenticatedTargetRejectsInvalidCredentialsBeforeClients(string corruption)
    {
        var settings = SessionHostStartupSupport.ValidSettings();
        settings[SessionHostStartupSupport.Target] = "RabbitMQ";
        settings[SessionHostStartupSupport.User] = "benchmark";
        settings[SessionHostStartupSupport.Password] = SessionHostStartupSupport.Canary;
        settings[SessionHostStartupSupport.Connection] = "amqp://127.0.0.1:1/";
        switch (corruption)
        {
            case "missing-user":
                settings.Remove(SessionHostStartupSupport.User);
                break;
            case "missing-password":
                settings.Remove(SessionHostStartupSupport.Password);
                break;
            case "invalid-user":
                settings[SessionHostStartupSupport.User] += "\n" + SessionHostStartupSupport.Canary;
                break;
            case "missing-connection":
                settings.Remove(SessionHostStartupSupport.Connection);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(corruption));
        }
        return SessionHostStartupSupport.AssertRefusedAsync(settings, SessionHostStartupSupport.Invalid);
    }
}
