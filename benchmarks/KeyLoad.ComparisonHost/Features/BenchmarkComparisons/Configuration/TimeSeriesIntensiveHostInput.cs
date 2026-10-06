using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace KeyLoad.ComparisonHost.Features.BenchmarkComparisons;

[KeyLoad.ConfigurationBinding]
internal static class TimeSeriesIntensiveHostInput
{
    internal static InvalidOperationException Invalid() => new(TimeSeriesIntensiveHostConstants.InvalidCode);

    internal static string Required(IConfiguration configuration, string key)
    {
        var value = configuration[key];
        if (!IsPrivateText(value))
        {
            throw Invalid();
        }
        return value;
    }

    private static bool IsPrivateText([NotNullWhen(true)] string? value) => !string.IsNullOrWhiteSpace(value)
        && !value.Contains(TimeSeriesIntensiveHostConstants.CarriageReturn, StringComparison.Ordinal)
        && !value.Contains(TimeSeriesIntensiveHostConstants.LineFeed, StringComparison.Ordinal);

    internal static long ReadJob(IConfiguration configuration)
    {
        var value = Required(configuration, TimeSeriesIntensiveHostConstants.JobId);
        if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var result)
            || result <= TimeSeriesIntensiveHostConstants.FirstIndex || value != result.ToString(CultureInfo.InvariantCulture))
        {
            throw Invalid();
        }
        return result;
    }

    internal static string ReadOutput(IConfiguration configuration)
    {
        var value = Required(configuration, ComparisonHostConstants.Output);
        if (!Path.IsPathFullyQualified(value))
        {
            throw Invalid();
        }
        return Path.GetFullPath(value);
    }

    internal static void ValidateNativeSection(IConfiguration configuration, TimeSeriesIntensiveTargetKind target)
    {
        var allowed = target == TimeSeriesIntensiveTargetKind.KeyLoad
            ? TimeSeriesIntensiveHostConstants.KeyLoadNativeFields : TimeSeriesIntensiveHostConstants.TimescaleNativeFields;
        var section = configuration.GetSection(TimeSeriesIntensiveHostConstants.NativeSection);
        var children = section.GetChildren().Take(allowed.Length + TimeSeriesIntensiveHostConstants.OverflowLookahead).ToArray();
        if (section.Value is not null || children.Length > allowed.Length)
        {
            throw Invalid();
        }
        foreach (var child in children)
        {
            if (!allowed.Contains(child.Key, StringComparer.OrdinalIgnoreCase)
                || !string.Equals(child.Key, TimeSeriesIntensiveHostConstants.EndpointsField, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(child.Key, TimeSeriesIntensiveHostConstants.VoterIdsField, StringComparison.OrdinalIgnoreCase)
                    && child.GetChildren().Any())
            {
                throw Invalid();
            }
        }
    }

    internal static ImmutableArray<string> ReadArray(IConfiguration configuration, string key, int count)
    {
        var section = configuration.GetSection(key);
        var children = section.GetChildren().Take(count + TimeSeriesIntensiveHostConstants.OverflowLookahead).ToArray();
        if (section.Value is not null || children.Length != count)
        {
            throw Invalid();
        }
        var values = ImmutableArray.CreateBuilder<string>(count);
        for (var index = TimeSeriesIntensiveHostConstants.FirstIndex; index < count; index++)
        {
            var suffix = index.ToString(CultureInfo.InvariantCulture);
            var child = children.SingleOrDefault(item => item.Key == suffix);
            if (child is null || child.GetChildren().Any())
            {
                throw Invalid();
            }
            values.Add(Required(configuration, child.Path));
        }
        return values.MoveToImmutable();
    }

    internal static ImmutableArray<Uri> ReadEndpoints(IConfiguration configuration, string key, int count, bool tcp)
        => ValidateAuthorities(ReadArray(configuration, key, count), tcp);

    internal static ImmutableArray<Uri> ValidateAuthorities(ImmutableArray<string> values, bool tcp)
    {
        var endpoints = ImmutableArray.CreateBuilder<Uri>(values.Length);
        foreach (var value in values)
        {
            endpoints.Add(ReadAuthority(value, tcp));
        }
        var result = endpoints.MoveToImmutable();
        if (result.Select(item => item.GetLeftPart(UriPartial.Authority)).Distinct(StringComparer.OrdinalIgnoreCase).Count() != values.Length)
        {
            throw Invalid();
        }
        return result;
    }

    private static Uri ReadAuthority(string value, bool tcp)
    {
        if (value.Any(static character => char.IsWhiteSpace(character) || char.IsControl(character))
            || !Uri.TryCreate(value, UriKind.Absolute, out var endpoint) || !endpoint.IsWellFormedOriginalString()
            || string.IsNullOrWhiteSpace(endpoint.Host) || endpoint.UserInfo.Length != TimeSeriesIntensiveHostConstants.FirstIndex
            || endpoint.Query.Length != TimeSeriesIntensiveHostConstants.FirstIndex || endpoint.Fragment.Length != TimeSeriesIntensiveHostConstants.FirstIndex
            || !string.IsNullOrEmpty(endpoint.AbsolutePath) && endpoint.AbsolutePath != TimeSeriesIntensiveHostConstants.RootPath
            || (tcp ? endpoint.Scheme != TimeSeriesIntensiveHostConstants.TcpScheme
                || endpoint.Port is < TimeSeriesIntensiveHostConstants.MinimumPort or > TimeSeriesIntensiveHostConstants.MaximumPort
                : endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps))
        {
            throw Invalid();
        }
        return endpoint;
    }

    internal static Guid ReadIncarnation(IConfiguration configuration)
    {
        var value = Required(configuration, TimeSeriesIntensiveHostConstants.Incarnation);
        if (!Guid.TryParseExact(value, TimeSeriesIntensiveHostConstants.GuidD, out var result)
            || result == Guid.Empty || value != result.ToString(TimeSeriesIntensiveHostConstants.GuidD, CultureInfo.InvariantCulture))
        {
            throw Invalid();
        }
        return result;
    }

    internal static void RejectPresent(IConfiguration configuration, string key)
    {
        var section = configuration.GetSection(key);
        if (section.Value is not null || section.GetChildren().Any())
        {
            throw Invalid();
        }
    }

    internal static string ReadConnection(IConfiguration configuration)
    {
        var value = Required(configuration, TimeSeriesIntensiveHostConstants.ConnectionString);
        try
        {
            var parsed = new NpgsqlConnectionStringBuilder(value);
            if (!IsPrivateText(parsed.Host) || parsed.Host.Contains(TimeSeriesIntensiveHostConstants.HostSeparator, StringComparison.Ordinal)
                || parsed.Port is < TimeSeriesIntensiveHostConstants.MinimumPort or > TimeSeriesIntensiveHostConstants.MaximumPort
                || !IsPrivateText(parsed.Username) || !IsPrivateText(parsed.Password))
            {
                throw Invalid();
            }
            return value;
        }
        catch (ArgumentException)
        {
            throw Invalid();
        }
        catch (FormatException)
        {
            throw Invalid();
        }
        catch (OverflowException)
        {
            throw Invalid();
        }
    }
}
