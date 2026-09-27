using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MorganHacks.Identity.Services;
using MorganHacks.Lark.Data.Data;
using MorganHacks.Lark.Data.Domain;
using NpgsqlTypes;

namespace MorganHacks.Api.Tests;

public class CampaignEngagementTests(ApplicationsDatabase db) : IClassFixture<ApplicationsDatabase>
{
    [Fact]
    public async Task Report_scopes_totals_to_sent_messages_and_counts_unique_messages_once()
    {
        var campaign = await Campaign();
        var first = await Message(campaign);
        var second = await Message(campaign);
        await Message(campaign);
        var unsent = await Message(campaign, sent: false);
        await Link(first, "first", 3);
        await Link(first, "second", 4);
        await Link(second, "first", 2);
        await Link(unsent, "ignored", 200);
        await Link(await Message(await Campaign()), "other", 900);
        var token = await Token(first);
        var store = new LinkTrackingStore(db.DataSource);
        var visitor = EmailVisitor.FromUserAgent("Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) Mobile Safari/604.1", "US");
        await store.RecordOpenAsync(token, visitor);
        await store.RecordOpenAsync(token, visitor);
        var clickedLink = await Link(second, "current", 0);
        await store.VisitAsync(clickedLink, true, visitor: visitor);

        var report = await new EmailAnalyticsStore(db.DataSource).ReadCampaignAsync(campaign, true);
        Assert.Equal(3, report.SentEmails);
        Assert.Equal(2, report.TrackedEmails);
        Assert.Equal(2, report.ClickedEmails);
        Assert.Equal(10, report.TotalClicks);
        Assert.Equal(1, report.Engagement.OpenTrackedEmails);
        Assert.Equal(1, report.Engagement.OpenedEmails);
        Assert.Equal(2, report.Engagement.TotalOpens);
        Assert.Equal(1, report.Engagement.RecordedClicks);
        Assert.Equal(2, Assert.Single(report.Engagement.Daily).Opens);
        Assert.Equal(1, Assert.Single(report.Engagement.Hourly).Clicks);
        Assert.Equal("Safari", Assert.Single(report.Engagement.Browsers).Name);
        Assert.Equal("Mobile", Assert.Single(report.Engagement.Platforms).Name);
        Assert.Equal("US", Assert.Single(report.Engagement.Countries).Name);
        Assert.Equal(3, report.Links!.Count);
        var firstLink = Assert.Single(report.Links, link => link.Destination.EndsWith("/first"));
        Assert.Equal(5, firstLink.TotalClicks);
        Assert.Equal(2, firstLink.ClickedEmails);
        Assert.True(report.Content!.IsSample);
        Assert.Equal("Actual subject", report.Content.Subject);
        Assert.Equal("<p>Actual message</p>", report.Content.Html);

        await using var deliver = db.DataSource.CreateCommand("UPDATE notify.messages SET status = 'delivered' WHERE id = @id");
        deliver.Parameters.AddWithValue("id", first);
        await deliver.ExecuteNonQueryAsync();
        var summary = Assert.Single(await new EmailAnalyticsStore(db.DataSource)
            .ReadListSummariesAsync([campaign], true)).Value;
        Assert.Equal(3, summary.SentEmails);
        Assert.Equal(1, summary.DeliveredEmails);
        Assert.Equal(1, summary.OpenedEmails);
        Assert.Equal(1, summary.OpenTrackedEmails);
        Assert.Equal(2, summary.TrackedEmails);
        Assert.Equal(2, summary.ClickedEmails);
        Assert.Equal("<p>Template</p>", summary.PreviewHtml);
    }

    [Fact]
    public async Task Public_tracking_records_gets_not_heads_and_keeps_unknown_tokens_private()
    {
        var message = await Message(await Campaign());
        var token = await Token(message);
        var link = await Link(message, "destination", 0);
        using var app = App();
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/130.0.0.0 Safari/537.36");
        client.DefaultRequestHeaders.Add("CF-IPCountry", "US");
        using var head = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, $"/email/open/{token:N}"));
        Assert.Equal(HttpStatusCode.OK, head.StatusCode);
        using var pixel = await client.GetAsync($"/email/open/{token:N}");
        Assert.Equal("image/gif", pixel.Content.Headers.ContentType!.MediaType);
        Assert.True(pixel.Headers.CacheControl!.NoStore);
        var unknown = await client.GetByteArrayAsync($"/email/open/{Guid.NewGuid():N}");
        Assert.Equal(await pixel.Content.ReadAsByteArrayAsync(), unknown);
        await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, $"/email/click/{link:N}"));
        using var click = await client.GetAsync($"/email/click/{link:N}?url=https://untrusted.invalid");
        Assert.Equal("https://example.test/destination", click.Headers.Location!.AbsoluteUri);
        await using var read = db.DataSource.CreateCommand("SELECT kind, browser, operating_system, platform, country_code FROM notify.email_engagement_events WHERE message_id = @id ORDER BY kind");
        read.Parameters.AddWithValue("id", message);
        await using var rows = await read.ExecuteReaderAsync();
        Assert.True(await rows.ReadAsync());
        Assert.Equal("click", rows.GetString(0));
        Assert.Equal("Chrome", rows.GetString(1));
        Assert.Equal("Windows", rows.GetString(2));
        Assert.Equal("Desktop", rows.GetString(3));
        Assert.True(rows.IsDBNull(4));
        Assert.True(await rows.ReadAsync());
        Assert.Equal("open", rows.GetString(0));
        Assert.False(await rows.ReadAsync());
    }

    [Fact]
    public async Task Campaign_report_protects_content_and_destinations_with_template_permission()
    {
        var campaign = await Campaign();
        await Link(await Message(campaign), "private-destination", 2);
        using var app = App();
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var path = $"/admin/campaigns/{campaign}";
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(path)).StatusCode);
        var person = await db.AddPersonAsync($"viewer-{Guid.NewGuid():N}@example.test");
        using var scope = app.Services.CreateScope();
        var session = await scope.ServiceProvider.GetRequiredService<SessionService>().StartAsync(person);
        client.DefaultRequestHeaders.Add("Cookie", $"mh_session={session}");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path)).StatusCode);
        await db.GrantAsync(person, "email.view_stats");
        var json = await client.GetFromJsonAsync<JsonElement>(path);
        var analytics = json.GetProperty("analytics");
        Assert.Equal(2, analytics.GetProperty("totalClicks").GetInt64());
        Assert.Equal(JsonValueKind.Null, analytics.GetProperty("content").ValueKind);
        Assert.Equal(JsonValueKind.Null, analytics.GetProperty("links").ValueKind);
        Assert.DoesNotContain("private-destination", json.ToString());
        Assert.DoesNotContain("Actual message", json.ToString());
        var list = await client.GetFromJsonAsync<JsonElement>("/admin/campaigns");
        var row = Assert.Single(list.GetProperty("campaigns").EnumerateArray(), item => item.GetProperty("id").GetGuid() == campaign);
        Assert.Equal(JsonValueKind.Null, row.GetProperty("summary").GetProperty("previewHtml").ValueKind);
        Assert.Equal(1, row.GetProperty("summary").GetProperty("clickedEmails").GetInt64());
        await db.GrantAsync(person, "email.manage_templates");
        json = await client.GetFromJsonAsync<JsonElement>(path);
        Assert.Equal(JsonValueKind.Object, json.GetProperty("analytics").GetProperty("content").ValueKind);
        Assert.Equal(1, json.GetProperty("analytics").GetProperty("links").GetArrayLength());
        list = await client.GetFromJsonAsync<JsonElement>("/admin/campaigns");
        row = Assert.Single(list.GetProperty("campaigns").EnumerateArray(), item => item.GetProperty("id").GetGuid() == campaign);
        Assert.Equal("<p>Template</p>", row.GetProperty("summary").GetProperty("previewHtml").GetString());
    }

    [Fact]
    public async Task Campaign_list_pages_filters_and_sorts_without_exposing_recipient_addresses()
    {
        var prefix = $"list%_{Guid.NewGuid():N}";
        for (var index = 0; index < 12; index++)
        {
            var id = await Campaign();
            await using var update = db.DataSource.CreateCommand("""
                UPDATE notify.campaigns SET name = @name, status = @status,
                    segment = '{"type":"explicitList","emails":["private@example.test"]}'::jsonb
                 WHERE id = @id
                """);
            update.Parameters.AddWithValue("name", $"{prefix}-{index:D2}");
            update.Parameters.AddWithValue("status", index < 2 ? "sent" : "draft");
            update.Parameters.AddWithValue("id", id);
            await update.ExecuteNonQueryAsync();
        }
        using var app = App();
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var person = await db.AddPersonAsync($"list-viewer-{Guid.NewGuid():N}@example.test");
        await db.GrantAsync(person, "email.view_stats");
        using var scope = app.Services.CreateScope();
        var session = await scope.ServiceProvider.GetRequiredService<SessionService>().StartAsync(person);
        client.DefaultRequestHeaders.Add("Cookie", $"mh_session={session}");
        var path = $"/admin/campaigns?search={Uri.EscapeDataString(prefix)}&sort=name";
        var first = await client.GetFromJsonAsync<JsonElement>(path);
        Assert.Equal(12, first.GetProperty("total").GetInt64());
        Assert.Equal(10, first.GetProperty("campaigns").GetArrayLength());
        Assert.Equal(10, first.GetProperty("counts").GetProperty("draft").GetInt64());
        Assert.Equal($"{prefix}-00", first.GetProperty("campaigns")[0].GetProperty("name").GetString());
        Assert.DoesNotContain("private@example.test", first.ToString());
        Assert.Equal(1, first.GetProperty("campaigns")[0].GetProperty("audience").GetProperty("count").GetInt32());
        var second = await client.GetFromJsonAsync<JsonElement>($"{path}&page=2");
        Assert.Equal(2, second.GetProperty("campaigns").GetArrayLength());
        Assert.Equal($"{prefix}-10", second.GetProperty("campaigns")[0].GetProperty("name").GetString());
        var filtered = await client.GetFromJsonAsync<JsonElement>($"{path}&status=sent&page=99");
        Assert.Equal(2, filtered.GetProperty("total").GetInt64());
        Assert.Equal(1, filtered.GetProperty("page").GetInt32());
        Assert.All(filtered.GetProperty("campaigns").EnumerateArray(), row => Assert.Equal("sent", row.GetProperty("status").GetString()));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/admin/campaigns?page=0")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/admin/campaigns?sort=unknown")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/admin/campaigns?status=unknown")).StatusCode);
    }

    [Fact]
    public async Task Empty_campaign_has_zero_totals_and_no_fabricated_rates_or_activity()
    {
        var report = await new EmailAnalyticsStore(db.DataSource).ReadCampaignAsync(await Campaign(), true);
        Assert.Equal(0, report.SentEmails);
        Assert.Equal(0, report.TrackedEmails);
        Assert.Equal(0, report.Engagement.OpenTrackedEmails);
        Assert.Empty(report.Engagement.Daily);
        Assert.Empty(report.Engagement.Countries);
        Assert.False(report.Content!.IsSample);
        Assert.Empty(report.Links!);
    }

    [Fact]
    public async Task Activity_window_keeps_lifetime_totals_and_groups_recent_events_in_utc()
    {
        var campaign = await Campaign();
        var message = await Message(campaign);
        await Token(message);
        await using var command = db.DataSource.CreateCommand("""
            INSERT INTO notify.email_engagement_events
                (message_id, kind, occurred_at, browser, operating_system, platform)
            VALUES (@id, 'open', now() - interval '100 days', 'Unknown', 'Unknown', 'Unknown'),
                (@id, 'open', (date_trunc('day', now() AT TIME ZONE 'UTC') - interval '1 day' + interval '1 hour') AT TIME ZONE 'UTC', 'Unknown', 'Unknown', 'Unknown'),
                (@id, 'open', (date_trunc('day', now() AT TIME ZONE 'UTC') - interval '1 day' + interval '23 hours') AT TIME ZONE 'UTC', 'Unknown', 'Unknown', 'Unknown')
            """);
        command.Parameters.AddWithValue("id", message);
        await command.ExecuteNonQueryAsync();
        var report = await new EmailAnalyticsStore(db.DataSource).ReadCampaignAsync(campaign, false);
        Assert.Equal(3, report.Engagement.TotalOpens);
        Assert.Equal(1, report.Engagement.OpenedEmails);
        Assert.Equal(2, Assert.Single(report.Engagement.Daily).Opens);
        Assert.Equal(["01", "23"], report.Engagement.Hourly.Select(point => point.Bucket));
    }

    [Fact]
    public async Task Country_collection_requires_an_explicitly_configured_ingress_header()
    {
        var message = await Message(await Campaign());
        var token = await Token(message);
        using var app = App().WithWebHostBuilder(builder => builder.UseSetting("EmailTracking:CountryHeader", "X-Trusted-Country"));
        using var client = app.CreateClient();
        client.DefaultRequestHeaders.Add("X-Trusted-Country", "ng");
        using var pixel = await client.GetAsync($"/email/open/{token:N}");
        Assert.Equal(HttpStatusCode.OK, pixel.StatusCode);
        await using var read = db.DataSource.CreateCommand("SELECT country_code FROM notify.email_engagement_events WHERE message_id = @id");
        read.Parameters.AddWithValue("id", message);
        Assert.Equal("NG", await read.ExecuteScalarAsync());
        Assert.Null(EmailVisitor.FromUserAgent(null, "XX").CountryCode);
        Assert.Null(EmailVisitor.FromUserAgent(null, "US,NG").CountryCode);
    }

    [Theory]
    [InlineData(true, "GB")]
    [InlineData(false, null)]
    public async Task Country_lookup_uses_only_a_trusted_client_address(bool trusted, string? expectedCountry)
    {
        var campaign = await Campaign();
        var message = await Message(campaign);
        var link = await Link(message, "geography", 0);
        using var app = App().WithWebHostBuilder(builder => builder
            .UseSetting("EmailTracking:CountryDatabasePath", Path.Combine(AppContext.BaseDirectory, "TestData", "GeoIP2-Country-Test.mmdb"))
            .UseSetting("Network:ProxySecret", "test-proxy-proof"));
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Real-IP", "81.2.69.160");
        client.DefaultRequestHeaders.Add("CF-IPCountry", "NG");
        if (trusted) client.DefaultRequestHeaders.Add("X-MH-Proxy", "test-proxy-proof");
        using var response = await client.GetAsync($"/email/click/{link:N}");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        await using var read = db.DataSource.CreateCommand("SELECT country_code FROM notify.email_engagement_events WHERE message_id = @id");
        read.Parameters.AddWithValue("id", message);
        var country = await read.ExecuteScalarAsync();
        Assert.Equal(expectedCountry, country is DBNull ? null : country);
        var report = await new EmailAnalyticsStore(db.DataSource).ReadCampaignAsync(
            campaign, true);
        Assert.Equal(expectedCountry ?? "Unknown", Assert.Single(report.Engagement.Countries).Name);
    }

    [Theory]
    [InlineData("Mozilla/5.0 (iPad; CPU OS 17_0) Mobile Safari/604.1", "Safari", "iOS", "Tablet", false)]
    [InlineData("Mozilla/5.0 (Linux; Android 14) Chrome/120 Mobile Safari/537.36 EdgA/120", "Edge", "Android", "Mobile", false)]
    [InlineData("GoogleImageProxy", "Image proxy", "Unknown", "Unknown", true)]
    [InlineData("", "Unknown", "Unknown", "Unknown", false)]
    public void Client_classification_preserves_unknown_and_proxy_clients(string ua, string browser, string os, string platform, bool automated)
    {
        Assert.Equal(new EmailVisitor(browser, os, platform, automated), EmailVisitor.FromUserAgent(ua));
    }

    private WebApplicationFactory<Program> App() => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        builder.UseSetting("ConnectionStrings:Postgres", db.ConnectionString));

    private async Task<Guid> Campaign()
    {
        var actor = await db.AddPersonAsync($"author-{Guid.NewGuid():N}@example.test");
        await using var command = db.DataSource.CreateCommand("""
            WITH template AS (
                INSERT INTO notify.templates (key, name, kind, subject, body_html, body_text, from_local, from_domain, click_tracking)
                VALUES (@key, 'Campaign template', 'broadcast', 'Template subject', '<p>Template</p>', 'Body', 'mail', 'example.test', true)
                RETURNING id
            )
            INSERT INTO notify.campaigns (template_id, name, created_by)
            SELECT id, 'Engagement report', @actor FROM template RETURNING id
            """);
        command.Parameters.AddWithValue("key", $"engagement-{Guid.NewGuid():N}");
        command.Parameters.AddWithValue("actor", actor);
        return (Guid)(await command.ExecuteScalarAsync())!;
    }

    private async Task<Guid> Message(Guid campaign, bool sent = true)
    {
        await using var command = db.DataSource.CreateCommand("""
            INSERT INTO notify.messages (campaign_id, to_email, rendered_subject, rendered_body_html, rendered_body_text, status, sent_at)
            VALUES (@campaign, @email, 'Actual subject', '<p>Actual message</p>', 'Actual message',
                CASE WHEN @sent THEN 'sent' ELSE 'pending' END, CASE WHEN @sent THEN now() END) RETURNING id
            """);
        command.Parameters.AddWithValue("campaign", campaign);
        command.Parameters.AddWithValue("email", $"recipient-{Guid.NewGuid():N}@example.test");
        command.Parameters.AddWithValue("sent", sent);
        return (Guid)(await command.ExecuteScalarAsync())!;
    }

    private async Task<Guid> Token(Guid message)
    {
        await using var command = db.DataSource.CreateCommand("INSERT INTO notify.message_tracking (message_id) VALUES (@id) RETURNING id");
        command.Parameters.AddWithValue("id", message);
        return (Guid)(await command.ExecuteScalarAsync())!;
    }

    private async Task<Guid> Link(Guid message, string path, int clicks)
    {
        var destination = $"https://example.test/{path}";
        await using var command = db.DataSource.CreateCommand("""
            INSERT INTO notify.tracked_links (message_id, destination, destination_hash, click_count)
            VALUES (@message, @destination, @hash, @clicks) RETURNING id
            """);
        command.Parameters.AddWithValue("message", message);
        command.Parameters.AddWithValue("destination", destination);
        command.Parameters.AddWithValue("hash", NpgsqlDbType.Bytea, SHA256.HashData(Encoding.UTF8.GetBytes(destination)));
        command.Parameters.AddWithValue("clicks", clicks);
        return (Guid)(await command.ExecuteScalarAsync())!;
    }
}
