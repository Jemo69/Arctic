namespace MorganHacks.Lark.Data.Domain;

public sealed record EmailVisitor(string Browser, string OperatingSystem, string Platform, bool Automated, string? CountryCode = null)
{
    public static EmailVisitor FromUserAgent(string? userAgent, string? country = null)
    {
        var ua = (userAgent ?? "").ToLowerInvariant();
        var automated = new[] { "bot", "spider", "crawler", "googleimageproxy", "proofpoint", "mimecast", "barracuda" }
            .Any(ua.Contains);
        var os = ua.Contains("iphone") || ua.Contains("ipad") || ua.Contains("ipod") ? "iOS"
            : ua.Contains("android") ? "Android" : ua.Contains("windows") ? "Windows"
            : ua.Contains("cros") ? "Chrome OS" : ua.Contains("macintosh") || ua.Contains("mac os") ? "macOS"
            : ua.Contains("linux") ? "Linux" : "Unknown";
        var browser = ua.Contains("googleimageproxy") ? "Image proxy"
            : ua.Contains("edg/") || ua.Contains("edgios") || ua.Contains("edga/") ? "Edge"
            : ua.Contains("opr/") || ua.Contains("opera") ? "Opera"
            : ua.Contains("samsungbrowser") ? "Samsung Internet"
            : ua.Contains("firefox") || ua.Contains("fxios") ? "Firefox"
            : ua.Contains("chrome") || ua.Contains("crios") ? "Chrome"
            : ua.Contains("safari") ? "Safari" : "Unknown";
        var platform = automated ? "Unknown" : ua.Contains("ipad") || (os == "Android" && !ua.Contains("mobile")) ? "Tablet"
            : ua.Contains("mobile") || ua.Contains("iphone") || ua.Contains("ipod") ? "Mobile"
            : os is "Windows" or "macOS" or "Linux" or "Chrome OS" ? "Desktop" : "Unknown";
        var countryCode = country?.Trim().ToUpperInvariant();
        if (countryCode is not { Length: 2 } || countryCode.Any(c => c is < 'A' or > 'Z') || countryCode == "XX")
            countryCode = null;
        return new EmailVisitor(browser, os, platform, automated, countryCode);
    }
}
