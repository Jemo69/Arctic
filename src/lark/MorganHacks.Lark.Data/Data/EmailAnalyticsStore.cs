using System.Text.Json;
using Npgsql;

namespace MorganHacks.Lark.Data.Data;

public sealed record EmailTemplateClicks(string Name, long Clicks, long ClickedEmails);
public sealed record CampaignListSummary(
    long SentEmails, long DeliveredEmails, long OpenTrackedEmails, long OpenedEmails,
    long TrackedEmails, long ClickedEmails, string? PreviewHtml);
public sealed record EmailCampaignPerformance(
    Guid Id, string Name, DateTimeOffset SentAt, long SentEmails, long TrackedEmails,
    long ClickedEmails, long TotalClicks, string? PreviewHtml);
public sealed record EmailAnalytics(
    long SentEmails, long TrackedEmails, long ClickedEmails, long TotalClicks,
    long ClickedRecipients, long TrackingEnabledTemplates, IReadOnlyList<EmailTemplateClicks> Templates);
public sealed record CampaignLinkPerformance(
    string Destination, long TrackedEmails, long ClickedEmails, long TotalClicks, DateTimeOffset? LastClickedAt);
public sealed record CampaignEmailContent(
    string TemplateName, string Subject, string Html, string? PreviewText,
    string? FromName, string FromEmail, string? ReplyTo, bool IsSample);
public sealed record CampaignEmailAnalytics(
    long SentEmails, long TrackedEmails, long ClickedEmails, long TotalClicks,
    bool ClickTrackingEnabled, CampaignEmailContent? Content, IReadOnlyList<CampaignLinkPerformance>? Links,
    CampaignEngagement Engagement);
public sealed record EmailActivityPoint(string Bucket, long Opens, long Clicks);
public sealed record EmailClientCount(string Name, long Count);
public sealed record CampaignEngagement(
    long OpenTrackedEmails, long OpenedEmails, long TotalOpens, long RecordedClicks,
    long AutomatedEvents, DateTimeOffset? FirstRecordedAt,
    IReadOnlyList<EmailActivityPoint> Daily, IReadOnlyList<EmailActivityPoint> Hourly,
    IReadOnlyList<EmailClientCount> Browsers, IReadOnlyList<EmailClientCount> OperatingSystems,
    IReadOnlyList<EmailClientCount> Platforms, IReadOnlyList<EmailClientCount> Countries);

public sealed class EmailAnalyticsStore(NpgsqlDataSource dataSource)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyDictionary<Guid, CampaignListSummary>> ReadListSummariesAsync(
        Guid[] campaignIds, bool includePreviews, CancellationToken ct = default)
    {
        if (campaignIds.Length == 0) return new Dictionary<Guid, CampaignListSummary>();
        const string sql = """
            WITH sent AS MATERIALIZED (
                SELECT id, campaign_id, status FROM notify.messages
                 WHERE campaign_id = ANY(@ids) AND sent_at IS NOT NULL
            ), links AS (
                SELECT l.message_id, bool_or(l.click_count > 0) AS clicked
                  FROM notify.tracked_links l JOIN sent s ON s.id = l.message_id
                 GROUP BY l.message_id
            ), opens AS (
                SELECT DISTINCT e.message_id FROM notify.email_engagement_events e
                  JOIN sent s ON s.id = e.message_id WHERE e.kind = 'open'
            ), totals AS (
                SELECT s.campaign_id, count(*) AS sent,
                       count(*) FILTER (WHERE s.status IN ('delivered', 'complained')) AS delivered,
                       count(mt.message_id) AS open_tracked, count(o.message_id) AS opened,
                       count(l.message_id) AS tracked, count(*) FILTER (WHERE l.clicked) AS clicked
                  FROM sent s
                  LEFT JOIN links l ON l.message_id = s.id
                  LEFT JOIN opens o ON o.message_id = s.id
                  LEFT JOIN notify.message_tracking mt ON mt.message_id = s.id
                 GROUP BY s.campaign_id
            )
            SELECT c.id, COALESCE(s.sent, 0), COALESCE(s.delivered, 0),
                   COALESCE(s.open_tracked, 0), COALESCE(s.opened, 0),
                   COALESCE(s.tracked, 0), COALESCE(s.clicked, 0),
                   CASE WHEN @previews THEN t.body_html END
              FROM notify.campaigns c JOIN notify.templates t ON t.id = c.template_id
              LEFT JOIN totals s ON s.campaign_id = c.id
             WHERE c.id = ANY(@ids)
            """;
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("ids", campaignIds);
        command.Parameters.AddWithValue("previews", includePreviews);
        var summaries = new Dictionary<Guid, CampaignListSummary>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            summaries[reader.GetGuid(0)] = new CampaignListSummary(reader.GetInt64(1), reader.GetInt64(2),
                reader.GetInt64(3), reader.GetInt64(4), reader.GetInt64(5), reader.GetInt64(6),
                reader.IsDBNull(7) ? null : reader.GetString(7));
        return summaries;
    }

    public async Task<CampaignEmailAnalytics> ReadCampaignAsync(
        Guid campaignId, bool includeContent, CancellationToken ct = default)
    {
        const string sql = """
            WITH sent AS MATERIALIZED (
                SELECT id FROM notify.messages WHERE campaign_id = @id AND sent_at IS NOT NULL
            ), link_totals AS (
                SELECT l.message_id, sum(l.click_count)::bigint AS clicks
                  FROM notify.tracked_links l JOIN sent s ON s.id = l.message_id
                 GROUP BY l.message_id
            )
            SELECT count(s.id), count(l.message_id), count(*) FILTER (WHERE l.clicks > 0),
                   COALESCE(sum(l.clicks), 0)::bigint,
                   COALESCE((SELECT COALESCE(c.tracking_enabled, t.click_tracking) FROM notify.campaigns c
                       JOIN notify.templates t ON t.id = c.template_id WHERE c.id = @id), false)
              FROM sent s LEFT JOIN link_totals l ON l.message_id = s.id;

            SELECT COALESCE(NULLIF(t.name, ''), t.key), COALESCE(m.rendered_subject, c.email_settings->>'subject', t.subject),
                   COALESCE(m.rendered_body_html, t.body_html), COALESCE(c.email_settings->>'previewText', t.preview_text),
                   COALESCE(c.email_settings->>'fromName', t.from_name),
                   COALESCE(c.email_settings->>'fromEmail', t.from_local || '@' || t.from_domain),
                   NULLIF(COALESCE(c.email_settings->>'replyTo', t.reply_to), ''), m.id IS NOT NULL
              FROM notify.campaigns c JOIN notify.templates t ON t.id = c.template_id
              LEFT JOIN LATERAL (
                  SELECT id, rendered_subject, rendered_body_html FROM notify.messages
                   WHERE campaign_id = c.id AND sent_at IS NOT NULL
                   ORDER BY sent_at, id LIMIT 1
              ) m ON true
             WHERE c.id = @id AND @content;

            SELECT l.destination, count(*), count(*) FILTER (WHERE l.click_count > 0),
                   sum(l.click_count)::bigint, max(l.last_clicked_at)
              FROM notify.tracked_links l JOIN notify.messages m ON m.id = l.message_id
             WHERE m.campaign_id = @id AND m.sent_at IS NOT NULL AND @content
             GROUP BY l.destination
             ORDER BY sum(l.click_count) DESC, count(*) FILTER (WHERE l.click_count > 0) DESC, l.destination
             LIMIT 10;

            WITH events AS MATERIALIZED (
                SELECT e.* FROM notify.email_engagement_events e
                  JOIN notify.messages m ON m.id = e.message_id
                 WHERE m.campaign_id = @id AND m.sent_at IS NOT NULL
            ), daily AS (
                SELECT to_char(occurred_at AT TIME ZONE 'UTC', 'YYYY-MM-DD') AS bucket,
                       count(*) FILTER (WHERE kind = 'open') AS opens,
                       count(*) FILTER (WHERE kind = 'click') AS clicks
                  FROM events WHERE occurred_at >= date_trunc('day', now() AT TIME ZONE 'UTC') AT TIME ZONE 'UTC' - interval '89 days'
                 GROUP BY 1 ORDER BY 1
            ), hourly AS (
                SELECT to_char(occurred_at AT TIME ZONE 'UTC', 'HH24') AS bucket,
                       count(*) FILTER (WHERE kind = 'open') AS opens,
                       count(*) FILTER (WHERE kind = 'click') AS clicks
                  FROM events WHERE occurred_at >= date_trunc('day', now() AT TIME ZONE 'UTC') AT TIME ZONE 'UTC' - interval '89 days'
                 GROUP BY 1 ORDER BY 1
            ), breakdown AS (
                SELECT label.dimension, label.name, count(*) AS count FROM events e
                  CROSS JOIN LATERAL (VALUES ('browser', browser), ('os', operating_system),
                      ('platform', platform), ('country', COALESCE(country_code, 'Unknown'))) label(dimension, name)
                 WHERE kind = 'click'
                 GROUP BY label.dimension, label.name
                 ORDER BY count(*) DESC, label.name
            )
            SELECT (SELECT count(*) FROM notify.message_tracking mt JOIN notify.messages m ON m.id = mt.message_id
                     WHERE m.campaign_id = @id AND m.sent_at IS NOT NULL),
                   count(DISTINCT message_id) FILTER (WHERE kind = 'open'),
                   count(*) FILTER (WHERE kind = 'open'), count(*) FILTER (WHERE kind = 'click'),
                   count(*) FILTER (WHERE automated), min(occurred_at),
                   COALESCE((SELECT jsonb_agg(daily) FROM daily), '[]'::jsonb),
                   COALESCE((SELECT jsonb_agg(hourly) FROM hourly), '[]'::jsonb),
                   COALESCE((SELECT jsonb_agg(jsonb_build_object('name', name, 'count', count)) FROM breakdown WHERE dimension = 'browser'), '[]'::jsonb),
                   COALESCE((SELECT jsonb_agg(jsonb_build_object('name', name, 'count', count)) FROM breakdown WHERE dimension = 'os'), '[]'::jsonb),
                   COALESCE((SELECT jsonb_agg(jsonb_build_object('name', name, 'count', count)) FROM breakdown WHERE dimension = 'platform'), '[]'::jsonb),
                   COALESCE((SELECT jsonb_agg(jsonb_build_object('name', name, 'count', count)) FROM breakdown WHERE dimension = 'country'), '[]'::jsonb)
              FROM events;
            """;
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("id", campaignId);
        command.Parameters.AddWithValue("content", includeContent);
        await using var reader = await command.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        var sent = reader.GetInt64(0);
        var tracked = reader.GetInt64(1);
        var clicked = reader.GetInt64(2);
        var clicks = reader.GetInt64(3);
        var trackingEnabled = reader.GetBoolean(4);
        await reader.NextResultAsync(ct);
        CampaignEmailContent? content = null;
        if (await reader.ReadAsync(ct))
            content = new CampaignEmailContent(reader.GetString(0), reader.GetString(1), reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6), reader.GetBoolean(7));
        await reader.NextResultAsync(ct);
        List<CampaignLinkPerformance>? links = includeContent ? [] : null;
        while (await reader.ReadAsync(ct))
            links!.Add(new CampaignLinkPerformance(reader.GetString(0), reader.GetInt64(1), reader.GetInt64(2),
                reader.GetInt64(3), reader.IsDBNull(4) ? null : reader.GetFieldValue<DateTimeOffset>(4)));
        await reader.NextResultAsync(ct);
        await reader.ReadAsync(ct);
        var engagement = new CampaignEngagement(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2),
            reader.GetInt64(3), reader.GetInt64(4), reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5),
            JsonSerializer.Deserialize<EmailActivityPoint[]>(reader.GetString(6), Json) ?? [],
            JsonSerializer.Deserialize<EmailActivityPoint[]>(reader.GetString(7), Json) ?? [],
            JsonSerializer.Deserialize<EmailClientCount[]>(reader.GetString(8), Json) ?? [],
            JsonSerializer.Deserialize<EmailClientCount[]>(reader.GetString(9), Json) ?? [],
            JsonSerializer.Deserialize<EmailClientCount[]>(reader.GetString(10), Json) ?? [],
            JsonSerializer.Deserialize<EmailClientCount[]>(reader.GetString(11), Json) ?? []);
        return new CampaignEmailAnalytics(sent, tracked, clicked, clicks, trackingEnabled, content, links, engagement);
    }

    public async Task<IReadOnlyList<EmailCampaignPerformance>> ReadBestCampaignsAsync(
        bool includePreviews, CancellationToken ct = default)
    {
        const string sql = """
            WITH link_totals AS (
                SELECT message_id, sum(click_count)::bigint AS clicks
                  FROM notify.tracked_links GROUP BY message_id
            ), ranked AS (
                SELECT c.id, c.name, c.template_id, max(m.sent_at) AS sent_at,
                       count(*) AS sent_emails,
                       count(*) FILTER (WHERE l.message_id IS NOT NULL) AS tracked_emails,
                       count(*) FILTER (WHERE l.clicks > 0) AS clicked_emails,
                       COALESCE(sum(l.clicks), 0)::bigint AS total_clicks
                  FROM notify.campaigns c
                  JOIN notify.templates t ON t.id = c.template_id
                  JOIN notify.messages m ON m.campaign_id = c.id
                  LEFT JOIN link_totals l ON l.message_id = m.id
                 WHERE c.created_by IS NOT NULL AND t.kind = 'broadcast'
                   AND t.key !~ '^test_[0-9a-f]{32}$' AND m.sent_at IS NOT NULL
                 GROUP BY c.id, c.name, c.template_id
            )
            SELECT r.id, r.name, r.sent_at, r.sent_emails, r.tracked_emails,
                   r.clicked_emails, r.total_clicks,
                   CASE WHEN @previews THEN t.body_html END AS preview_html
              FROM ranked r JOIN notify.templates t ON t.id = r.template_id
             ORDER BY r.clicked_emails::numeric / NULLIF(r.tracked_emails, 0) DESC NULLS LAST,
                      r.clicked_emails DESC, r.sent_emails DESC, r.sent_at DESC, r.id
             LIMIT 5
            """;
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("previews", includePreviews);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var campaigns = new List<EmailCampaignPerformance>();
        while (await reader.ReadAsync(ct))
            campaigns.Add(new EmailCampaignPerformance(reader.GetGuid(0), reader.GetString(1),
                reader.GetFieldValue<DateTimeOffset>(2), reader.GetInt64(3), reader.GetInt64(4),
                reader.GetInt64(5), reader.GetInt64(6), reader.IsDBNull(7) ? null : reader.GetString(7)));
        return campaigns;
    }

    public async Task<EmailAnalytics> ReadAsync(CancellationToken ct = default)
    {
        const string sql = """
            WITH link_totals AS (
                SELECT message_id, sum(click_count)::bigint AS clicks
                  FROM notify.tracked_links GROUP BY message_id
            ), sent AS MATERIALIZED (
                SELECT m.id, m.to_email, t.key, COALESCE(NULLIF(t.name, ''), t.key) AS name,
                       l.message_id IS NOT NULL AS tracked, COALESCE(l.clicks, 0) AS clicks
                  FROM notify.messages m
                  JOIN notify.campaigns c ON c.id = m.campaign_id
                  JOIN notify.templates t ON t.id = c.template_id
                  LEFT JOIN link_totals l ON l.message_id = m.id
                 WHERE m.sent_at IS NOT NULL AND t.key !~ '^test_[0-9a-f]{32}$'
            )
            SELECT count(*), count(*) FILTER (WHERE tracked), count(*) FILTER (WHERE clicks > 0),
                   COALESCE(sum(clicks), 0)::bigint,
                   count(DISTINCT lower(to_email::text)) FILTER (WHERE clicks > 0),
                   (SELECT count(*) FROM notify.templates WHERE click_tracking AND superseded_at IS NULL),
                   COALESCE((SELECT jsonb_agg(ranked) FROM (
                       SELECT name, sum(clicks)::bigint AS clicks,
                              count(*) FILTER (WHERE clicks > 0) AS "clickedEmails"
                         FROM sent GROUP BY key, name HAVING sum(clicks) > 0
                        ORDER BY sum(clicks) DESC, name LIMIT 6
                   ) ranked), '[]'::jsonb)
              FROM sent
            """;
        await using var command = dataSource.CreateCommand(sql);
        await using var reader = await command.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return new EmailAnalytics(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2),
            reader.GetInt64(3), reader.GetInt64(4), reader.GetInt64(5),
            JsonSerializer.Deserialize<EmailTemplateClicks[]>(reader.GetString(6), Json) ?? []);
    }
}
