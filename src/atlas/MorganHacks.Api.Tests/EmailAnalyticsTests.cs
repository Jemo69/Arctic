using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MorganHacks.Applications.Services;
using MorganHacks.Identity.Services;
using MorganHacks.Lark.Data.Data;
using NpgsqlTypes;

namespace MorganHacks.Api.Tests;

public class EmailAnalyticsTests(ApplicationsDatabase db)
    : IClassFixture<ApplicationsDatabase>
{
    [Fact]
    public async Task Aggregates_clicks_without_double_counting_emails_or_including_tests_and_unsent_mail()
    {
        var eventId = await db.AddEventAsync();
        var template = await Template($"analytics-{Guid.NewGuid():N}", true);
        var first = await Message(eventId, template, "one@example.test");
        await Link(first, "https://example.test/a", 3);
        await Link(first, "https://example.test/b", 4);
        var second = await Message(eventId, template, "ONE@example.test");
        await Link(second, "https://example.test/a", 2);
        await Link(await Message(eventId, template, "other@example.test"), "https://example.test/a", 0);
        await Message(eventId, template, "untracked@example.test");
        await Link(await Message(eventId, template, "pending@example.test", false), "https://example.test/a", 20);
        var testTemplate = await Template($"test_{Guid.NewGuid():N}", true);
        await Link(await Message(eventId, testTemplate, "test@example.test"), "https://example.test/a", 50);

        var result = await new EmailAnalyticsStore(db.DataSource).ReadAsync(eventId);

        Assert.Equal(4, result.SentEmails);
        Assert.Equal(3, result.TrackedEmails);
        Assert.Equal(2, result.ClickedEmails);
        Assert.Equal(9, result.TotalClicks);
        Assert.Equal(1, result.ClickedRecipients);
        Assert.Equal(1, result.TrackingEnabledTemplates);
        var top = Assert.Single(result.Templates);
        Assert.Equal("Newsletter", top.Name);
        Assert.Equal(9, top.Clicks);
        Assert.Equal(2, top.ClickedEmails);
    }

    [Fact]
    public async Task Answers_for_the_event_asked_about_and_not_for_the_whole_workspace()
    {
        var store = new EmailAnalyticsStore(db.DataSource);
        var firstEvent = await db.AddEventAsync();
        var secondEvent = await db.AddEventAsync();

        // Two events' worth of mail, each an order of magnitude apart, so a
        // total that quietly spans both is not the same number as either.
        var quiet = await Template($"quiet-{Guid.NewGuid():N}", true);
        await Message(firstEvent, quiet, "quiet@example.test");
        var loud = await Template($"loud-{Guid.NewGuid():N}", true);
        for (var index = 0; index < 5; index++)
            await Link(await Message(secondEvent, loud, $"loud-{index}@example.test"),
                "https://example.test/loud", 10);

        // Mail sent to a hand-picked list names no event, so it belongs to none
        // of them rather than to whichever one was running.
        var unaddressed = await Template($"unaddressed-{Guid.NewGuid():N}", true);
        await Link(await Message(null, unaddressed, "anybody@example.test"),
            "https://example.test/anybody", 1000);

        var quietResult = await store.ReadAsync(firstEvent);
        Assert.Equal(1, quietResult.SentEmails);
        Assert.Equal(0, quietResult.TotalClicks);
        Assert.Empty(quietResult.Templates);
        Assert.Equal(1, quietResult.TrackingEnabledTemplates);

        var loudResult = await store.ReadAsync(secondEvent);
        Assert.Equal(5, loudResult.SentEmails);
        Assert.Equal(50, loudResult.TotalClicks);
        var top = Assert.Single(loudResult.Templates);
        Assert.Equal(50, top.Clicks);
        Assert.Equal(5, top.ClickedEmails);

        var empty = await store.ReadAsync(Guid.NewGuid());
        Assert.Equal(0, empty.SentEmails);
        Assert.Equal(0, empty.TotalClicks);
        Assert.Empty(empty.Templates);
    }

    [Fact]
    public async Task Unknown_events_are_refused_rather_than_answered_for_another_one()
    {
        var app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:Postgres", db.ConnectionString));
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var person = await db.AddPersonAsync($"analytics-{Guid.NewGuid():N}@example.test");
        await db.GrantAsync(person, "email.view_stats");
        using var scope = app.Services.CreateScope();
        var session = await scope.ServiceProvider.GetRequiredService<SessionService>().StartAsync(person);
        client.DefaultRequestHeaders.Add("Cookie", $"mh_session={session}");

        using var response = await client.GetAsync(
            $"/admin/analytics/email?eventId={Guid.NewGuid()}");
        using var campaigns = await client.GetAsync(
            $"/admin/analytics/email/campaigns?eventId={Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, campaigns.StatusCode);
    }

    [Fact]
    public async Task Analytics_answer_for_the_newest_event_when_none_is_named()
    {
        var app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:Postgres", db.ConnectionString));
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var person = await db.AddPersonAsync($"analytics-{Guid.NewGuid():N}@example.test");
        await db.GrantAsync(person, "email.view_stats");
        using var scope = app.Services.CreateScope();
        var session = await scope.ServiceProvider.GetRequiredService<SessionService>().StartAsync(person);
        client.DefaultRequestHeaders.Add("Cookie", $"mh_session={session}");

        // ListAsync orders by starts_at then created_at, and none of these are dated,
        // so the one created last is the one the dashboard would pick.
        await db.AddEventAsync();
        var older = await db.AddEventAsync();
        var newest = await db.AddEventAsync();
        var template = await Template($"newest-{Guid.NewGuid():N}", true);
        await Link(await Message(newest, template, "newest@example.test"), "https://example.test/n", 2);
        await Link(await Message(older, template, "older@example.test"), "https://example.test/o", 40);

        var read = await client.GetFromJsonAsync<EmailResponse>("/admin/analytics/email");
        var campaigns = await client.GetFromJsonAsync<EmailResponse>("/admin/analytics/email/campaigns");

        Assert.NotNull(read);
        Assert.Equal(newest, read.Chosen?.Id);
        Assert.Equal(2, read.Analytics?.TotalClicks);
        Assert.NotNull(campaigns);
        Assert.Equal(newest, campaigns.Chosen?.Id);
    }

    private sealed record EmailResponse(
        EventSummary[] Events, EventSummary? Chosen, EmailAnalytics? Analytics, EmailCampaignPerformance[]? Campaigns);

    [Fact]
    public async Task Email_statistics_require_their_own_permission_and_do_not_expose_recipients()
    {
        using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:Postgres", db.ConnectionString));
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/admin/analytics/email")).StatusCode);
        var person = await db.AddPersonAsync($"analytics-{Guid.NewGuid():N}@example.test");
        await db.GrantAsync(person, "applications.view");
        using var scope = app.Services.CreateScope();
        var session = await scope.ServiceProvider.GetRequiredService<SessionService>().StartAsync(person);
        client.DefaultRequestHeaders.Add("Cookie", $"mh_session={session}");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/admin/analytics/email")).StatusCode);
        await db.GrantAsync(person, "email.view_stats");
        using var response = await client.GetAsync("/admin/analytics/email");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("@example.test", json);
        Assert.DoesNotContain("https://example.test", json);
    }

    private async Task<Guid> Template(string key, bool tracking)
    {
        await using var command = db.DataSource.CreateCommand("""
            INSERT INTO notify.templates (key, name, kind, subject, body_html, body_text,
                                          from_local, from_domain, click_tracking)
            VALUES (@key, 'Newsletter', 'broadcast', 'Subject', '<p>Body</p>', 'Body',
                    'mail', 'example.test', @tracking) RETURNING id
            """);
        command.Parameters.AddWithValue("key", key);
        command.Parameters.AddWithValue("tracking", tracking);
        return (Guid)(await command.ExecuteScalarAsync())!;
    }

    private async Task<Guid> Message(Guid? eventId, Guid template, string email, bool sent = true)
    {
        await using var command = db.DataSource.CreateCommand("""
            WITH campaign AS (
                INSERT INTO notify.campaigns (template_id, name, event_id)
                VALUES (@template, 'Analytics test', @eventId) RETURNING id
            )
            INSERT INTO notify.messages (campaign_id, to_email, rendered_subject, rendered_body_html,
                                         rendered_body_text, status, sent_at)
            SELECT id, @email, 'Subject', '<p>Body</p>', 'Body',
                   CASE WHEN @sent THEN 'sent' ELSE 'pending' END,
                   CASE WHEN @sent THEN now() END FROM campaign RETURNING id
            """);
        command.Parameters.AddWithValue("template", template);
        command.Parameters.AddWithValue("eventId", (object?)eventId ?? DBNull.Value);
        command.Parameters.AddWithValue("email", email);
        command.Parameters.AddWithValue("sent", sent);
        return (Guid)(await command.ExecuteScalarAsync())!;
    }

    private async Task Link(Guid message, string destination, int clicks)
    {
        await using var command = db.DataSource.CreateCommand("""
            INSERT INTO notify.tracked_links (message_id, destination, destination_hash, click_count)
            VALUES (@message, @destination, @hash, @clicks)
            """);
        command.Parameters.AddWithValue("message", message);
        command.Parameters.AddWithValue("destination", destination);
        command.Parameters.AddWithValue("hash", NpgsqlDbType.Bytea, SHA256.HashData(Encoding.UTF8.GetBytes(destination)));
        command.Parameters.AddWithValue("clicks", clicks);
        await command.ExecuteNonQueryAsync();
    }
}
