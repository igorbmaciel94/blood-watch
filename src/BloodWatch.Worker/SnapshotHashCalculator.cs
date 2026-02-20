using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using BloodWatch.Core.Models;

namespace BloodWatch.Worker;

public static class SnapshotHashCalculator
{
    public static string Compute(Snapshot snapshot)
    {
        var builder = new StringBuilder();

        builder
            .Append("source=").Append(snapshot.Source.AdapterKey)
            .Append("|reference=").Append(snapshot.ReferenceDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "null")
            .Append('|');

        foreach (var item in snapshot.Items
                     .OrderBy(entry => entry.Region.Key, StringComparer.Ordinal)
                     .ThenBy(entry => entry.Metric.Key, StringComparer.Ordinal)
                     .ThenBy(entry => entry.StatusKey, StringComparer.Ordinal))
        {
            builder
                .Append(item.Region.Key).Append('|')
                .Append(item.Metric.Key).Append('|')
                .Append(item.StatusKey).Append('|')
                .Append(item.StatusLabel).Append('|')
                .Append(item.Unit ?? string.Empty).Append('|')
                .Append(item.Value?.ToString("0.####################", CultureInfo.InvariantCulture) ?? string.Empty)
                .Append(';');
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
