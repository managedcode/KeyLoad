namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>AC-ISO-006L: closed original numeric lines are algorithm inputs, not native execution evidence.</summary>
internal sealed class ComparisonReplayDiagnosticLogTests
{
    /// <summary>Exact original native prefix and consistent one/two/three-voter arithmetic define closed protected keys.</summary>
    /// <param name="voters">The actual configuration cardinality represented by the numeric input.</param>
    [Test]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public async Task ConsistentConfigurationAndQuotaHaveClosedKeys(int voters)
    {
        await Assert.That(ComparisonReplayDiagnosticLog.TryRead(ComparisonReplayDiagnosticLogData.Config(voters), out var config)).IsTrue();
        await Assert.That(config.Key).IsEqualTo(0);
        await Assert.That(config.Voters).IsEqualTo(voters);
        for (var sender = 0; sender < voters; sender++)
        {
            for (var pool = 0; pool < 4; pool++)
            {
                var original = ComparisonReplayDiagnosticLogData.Quota(sender, pool, voters);
                await Assert.That(ComparisonReplayDiagnosticLog.TryRead(original, out var quota)).IsTrue();
                await Assert.That(quota.Key).IsEqualTo(1 + sender * 4 + pool);
                await Assert.That(quota.Matches(config)).IsTrue();
            }
        }
    }

    /// <summary>Malformed/overflow/secret-bearing input cannot replace an existing protected quota.</summary>
    /// <param name="field">The exact original grammar token to replace.</param>
    /// <param name="replacement">A concrete malformed or inconsistent replacement.</param>
    [Test]
    [Arguments(ComparisonReplayDiagnosticLogData.Timestamp, ComparisonReplayDiagnosticLogData.InvalidDate)]
    [Arguments(ComparisonReplayDiagnosticLogData.Timestamp, ComparisonReplayDiagnosticLogData.InvalidPrecision)]
    [Arguments(ComparisonReplayDiagnosticLogData.Indent, ComparisonReplayDiagnosticLogData.ShortIndent)]
    [Arguments(ComparisonReplayDiagnosticLogData.Sender, ComparisonReplayDiagnosticLogData.BadSender)]
    [Arguments(ComparisonReplayDiagnosticLogData.Pool, ComparisonReplayDiagnosticLogData.BadPool)]
    [Arguments(ComparisonReplayDiagnosticLogData.Method, ComparisonReplayDiagnosticLogData.BadMethod)]
    [Arguments(ComparisonReplayDiagnosticLogData.Read, ComparisonReplayDiagnosticLogData.NegativeRead)]
    [Arguments(ComparisonReplayDiagnosticLogData.Read, ComparisonReplayDiagnosticLogData.WrongRead)]
    [Arguments(ComparisonReplayDiagnosticLogData.Critical, ComparisonReplayDiagnosticLogData.ExcessiveCritical)]
    [Arguments(ComparisonReplayDiagnosticLogData.Capacity, ComparisonReplayDiagnosticLogData.ZeroCapacity)]
    [Arguments(ComparisonReplayDiagnosticLogData.Capacity, ComparisonReplayDiagnosticLogData.WrongCapacity)]
    [Arguments(ComparisonReplayDiagnosticLogData.VoterMaximum, ComparisonReplayDiagnosticLogData.ZeroMaximum)]
    [Arguments(ComparisonReplayDiagnosticLogData.NodeMaximum, ComparisonReplayDiagnosticLogData.WrongMaximum)]
    [Arguments(ComparisonReplayDiagnosticLogData.NodeMaximum, ComparisonReplayDiagnosticLogData.TooManyVoters)]
    [Arguments(ComparisonReplayDiagnosticLogData.NodeMaximum, ComparisonReplayDiagnosticLogData.TooFewVoters)]
    [Arguments(ComparisonReplayDiagnosticLogData.Observed, ComparisonReplayDiagnosticLogData.NegativeObserved)]
    [Arguments(ComparisonReplayDiagnosticLogData.Oldest, ComparisonReplayDiagnosticLogData.OverflowOldest)]
    [Arguments(ComparisonReplayDiagnosticLogData.Suppressed, ComparisonReplayDiagnosticLogData.NegativeSuppressed)]
    [Arguments(ComparisonReplayDiagnosticLogData.Suppressed, ComparisonReplayDiagnosticLogData.OverflowSuppressed)]
    [Arguments(ComparisonReplayDiagnosticLogData.Suppressed, ComparisonReplayDiagnosticLogData.DuplicateSuppressed)]
    [Arguments(ComparisonReplayDiagnosticLogData.Suppressed, ComparisonReplayDiagnosticLogData.SecretSuffix)]
    [Arguments(ComparisonReplayDiagnosticLogData.Suppressed, ComparisonReplayDiagnosticLogData.UnknownSuffix)]
    [Arguments(ComparisonReplayDiagnosticLogData.Suppressed, ComparisonReplayDiagnosticLogData.LeadingZero)]
    public async Task MalformedQuotaCannotDisplaceProtectedEvidence(string field, string replacement)
    {
        var original = ComparisonReplayDiagnosticLogData.Quota(0, 2, 3);
        var malformed = original.Replace(field, replacement, StringComparison.Ordinal);
        await Assert.That(ComparisonReplayDiagnosticLog.TryRead(malformed, out _)).IsFalse();
        var buffer = new ComparisonResourceLogBuffer(2, 2048, 1024);
        buffer.Add(original);
        buffer.Add(malformed);
        buffer.Add(ComparisonReplayDiagnosticLogData.Ordinary);
        await Assert.That(buffer.Snapshot().SequenceEqual(new[] { original, ComparisonReplayDiagnosticLogData.Ordinary })).IsTrue();
    }

    /// <summary>Unknown prefix, embedded message and invalid configuration arithmetic remain ordinary.</summary>
    /// <param name="invalid">The complete invalid original line.</param>
    [Test]
    [Arguments(ComparisonReplayDiagnosticLogData.SecretPrefix)]
    [Arguments(ComparisonReplayDiagnosticLogData.BareMessage)]
    [Arguments(ComparisonReplayDiagnosticLogData.ZeroVoters)]
    [Arguments(ComparisonReplayDiagnosticLogData.ExcessVoters)]
    [Arguments(ComparisonReplayDiagnosticLogData.ZeroConfig)]
    [Arguments(ComparisonReplayDiagnosticLogData.WrongConfig)]
    [Arguments(ComparisonReplayDiagnosticLogData.OversizedConfig)]
    [Arguments(ComparisonReplayDiagnosticLogData.OverflowConfig)]
    public async Task InvalidOriginalConfigurationOrPrefixIsNotProtected(string invalid)
        => await Assert.That(ComparisonReplayDiagnosticLog.TryRead(invalid, out _)).IsFalse();

    /// <summary>Actual control characters and non-ASCII digits cannot claim protection through BCL numeric compatibility.</summary>
    /// <param name="configuration">Whether configuration or quota numeric bytes are corrupted.</param>
    /// <param name="corruption">The original token character corruption.</param>
    [Test]
    [Arguments(false, ComparisonReplayDiagnosticNumericToken.TrailingNull)]
    [Arguments(false, ComparisonReplayDiagnosticNumericToken.EmbeddedNull)]
    [Arguments(false, ComparisonReplayDiagnosticNumericToken.Tab)]
    [Arguments(false, ComparisonReplayDiagnosticNumericToken.NonAscii)]
    [Arguments(true, ComparisonReplayDiagnosticNumericToken.TrailingNull)]
    [Arguments(true, ComparisonReplayDiagnosticNumericToken.EmbeddedNull)]
    [Arguments(true, ComparisonReplayDiagnosticNumericToken.Tab)]
    [Arguments(true, ComparisonReplayDiagnosticNumericToken.NonAscii)]
    public async Task NonAsciiNumericTokenCannotDisplaceOriginalEvidence(bool configuration, ComparisonReplayDiagnosticNumericToken corruption)
    {
        var config = ComparisonReplayDiagnosticLogData.Config(3);
        var quota = ComparisonReplayDiagnosticLogData.Quota(0, 2, 3, suppressed: 17);
        var token = configuration ? ComparisonReplayDiagnosticLogData.NodeValue : ComparisonReplayDiagnosticLogData.SuppressedValue;
        var changed = corruption switch
        {
            ComparisonReplayDiagnosticNumericToken.TrailingNull => token + ComparisonReplayDiagnosticLogData.Null,
            ComparisonReplayDiagnosticNumericToken.EmbeddedNull => token.Insert(1, ComparisonReplayDiagnosticLogData.Null),
            ComparisonReplayDiagnosticNumericToken.Tab => token + ComparisonReplayDiagnosticLogData.Tab,
            _ => configuration ? ComparisonReplayDiagnosticLogData.NonAsciiNode : ComparisonReplayDiagnosticLogData.NonAsciiSuppressed
        };
        var field = configuration ? ComparisonReplayDiagnosticLogData.NodeField : ComparisonReplayDiagnosticLogData.SuppressedField;
        var malformed = (configuration ? config : quota).Replace(field + token, field + changed, StringComparison.Ordinal);
        await Assert.That(ComparisonReplayDiagnosticLog.TryRead(malformed, out _)).IsFalse();
        var buffer = new ComparisonResourceLogBuffer(3, 4096, 1024);
        buffer.Add(config);
        buffer.Add(quota);
        buffer.Add(malformed);
        buffer.Add(ComparisonReplayDiagnosticLogData.Ordinary);
        await Assert.That(buffer.Snapshot().SequenceEqual(new[] { config, quota, ComparisonReplayDiagnosticLogData.Ordinary })).IsTrue();
    }
}
