using Npgsql;
using NpgsqlTypes;

namespace OutreachStudio.Web.Services;

/// <summary>Provider calls in a window. MedianMs is the median latency of the calls in it.</summary>
public sealed record ProviderStats(string Provider, int Calls, int Ok, int MedianMs)
{
    public double SuccessRate => Calls == 0 ? 0 : (double)Ok / Calls;
}

/// <summary>
/// Health for the four providers, read from delivery_attempts. The median is a percentile_cont in
/// Postgres rather than a scan in C#, because the attempts table is the largest one in the database.
/// It opens its own connection instead of borrowing the request's DbContext, because the tiles
/// render while the page around them is still waiting for its own query.
/// </summary>
public sealed class ProviderHealth(IConfiguration configuration)
{
    public static readonly string[] Providers = ["push-a", "push-b", "email-a", "email-b"];

    public static readonly TimeSpan Recent = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan Fallback = TimeSpan.FromHours(24);

    public async Task<IReadOnlyList<ProviderStats>> ReadAsync(TimeSpan window, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(configuration.GetConnectionString("outreach"));
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand("""
            SELECT provider,
                   count(*) AS calls,
                   count(*) FILTER (WHERE succeeded) AS ok,
                   coalesce(percentile_cont(0.5) WITHIN GROUP (ORDER BY latency_ms), 0) AS median_ms
            FROM delivery_attempts
            WHERE started_at > now() - @window
            GROUP BY provider
            """, connection);
        command.Parameters.Add(new NpgsqlParameter("window", NpgsqlDbType.Interval) { Value = window });

        var found = new Dictionary<string, ProviderStats>(StringComparer.Ordinal);
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                var provider = reader.GetString(0);
                found[provider] = new ProviderStats(provider, (int)reader.GetInt64(1), (int)reader.GetInt64(2), (int)reader.GetDouble(3));
            }
        }
        return Providers.Select(p => found.TryGetValue(p, out var stats) ? stats : new ProviderStats(p, 0, 0, 0)).ToList();
    }
}
