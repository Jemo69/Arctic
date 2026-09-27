using System.Net;
using MaxMind.Db;

namespace MorganHacks.Api;

public sealed class EmailCountryLookup : IDisposable
{
    private readonly Reader? reader;
    private readonly ILogger<EmailCountryLookup> logger;

    public EmailCountryLookup(IConfiguration configuration, ILogger<EmailCountryLookup> logger)
    {
        this.logger = logger;
        var path = configuration["EmailTracking:CountryDatabasePath"];
        if (!string.IsNullOrWhiteSpace(path)) reader = new Reader(path);
    }

    public string? Find(string? address)
    {
        if (reader is null || !IPAddress.TryParse(address, out var ip)) return null;
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip) || ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal) return null;
        try
        {
            var record = reader.Find<Dictionary<string, object>>(ip);
            return record?.GetValueOrDefault("country") is Dictionary<string, object> country
                ? country.GetValueOrDefault("iso_code") as string
                : null;
        }
        catch (InvalidDatabaseException exception)
        {
            logger.LogError(exception, "Email country database lookup failed");
            return null;
        }
    }

    public void Dispose() => reader?.Dispose();
}
