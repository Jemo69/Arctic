using System.Security.Cryptography;
using System.Text;
using System.Net;
using MorganHacks.Lark.Data.Domain;
using Npgsql;

namespace MorganHacks.Lark.Data.Data;

public sealed class LinkTrackingStore(NpgsqlDataSource dataSource)
{
    public async Task<ClaimedMessage> PrepareAsync(
        ClaimedMessage message, string publicBaseUrl, CancellationToken ct = default)
    {
        if (!message.ClickTracking)
        {
            return message;
        }

        var destinations = EmailLinks.Destinations(message.BodyHtml);
        if (!EmailLinks.IsWebUrl(publicBaseUrl)
            || !Uri.TryCreate(publicBaseUrl, UriKind.Absolute, out var origin)
            || origin.Query.Length != 0 || origin.Fragment.Length != 0
            || (origin.Scheme != "https" && !origin.IsLoopback))
        {
            throw new InvalidOperationException("Set SendLoop:ClickTrackingBaseUrl to the public API address.");
        }

        var links = new Dictionary<string, string>(StringComparer.Ordinal);
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        Guid openToken;
        await using (var track = new NpgsqlCommand("""
            INSERT INTO notify.message_tracking (message_id) VALUES (@message)
            ON CONFLICT (message_id) DO UPDATE SET message_id = EXCLUDED.message_id RETURNING id
            """, connection, transaction))
        {
            track.Parameters.AddWithValue("message", message.Id);
            openToken = (Guid)(await track.ExecuteScalarAsync(ct))!;
        }
        if (destinations.Count > 0)
            await using (var write = new NpgsqlCommand("""
            INSERT INTO notify.tracked_links (message_id, destination, destination_hash)
            SELECT @message, link.destination, link.hash
              FROM unnest(@destinations::text[], @hashes::bytea[]) AS link(destination, hash)
            ON CONFLICT (message_id, destination_hash) DO NOTHING
            """, connection, transaction))
            {
                write.Parameters.AddWithValue("message", message.Id);
                write.Parameters.AddWithValue("destinations", destinations.ToArray());
                write.Parameters.AddWithValue("hashes", destinations
                    .Select(destination => SHA256.HashData(Encoding.UTF8.GetBytes(destination))).ToArray());
                await write.ExecuteNonQueryAsync(ct);
            }
        await using (var read = new NpgsqlCommand("""
            SELECT id, destination FROM notify.tracked_links WHERE message_id = @message
            """, connection, transaction))
        {
            read.Parameters.AddWithValue("message", message.Id);
            await using var reader = await read.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                links[reader.GetString(1)] = $"{publicBaseUrl.TrimEnd('/')}/email/click/{reader.GetGuid(0):N}";
            }
        }
        await transaction.CommitAsync(ct);
        var html = EmailLinks.RewriteHtml(message.BodyHtml, links);
        var pixel = $"<img src=\"{WebUtility.HtmlEncode($"{publicBaseUrl.TrimEnd('/')}/email/open/{openToken:N}")}\" width=\"1\" height=\"1\" alt=\"\" style=\"display:block;width:1px;height:1px;border:0\" />";
        var bodyEnd = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        return message with
        {
            BodyHtml = bodyEnd >= 0 ? html.Insert(bodyEnd, pixel) : html + pixel,
            BodyText = EmailLinks.RewriteText(message.BodyText, links),
        };
    }

    public async Task<string?> VisitAsync(Guid id, bool recordClick, CancellationToken ct = default, EmailVisitor? visitor = null)
    {
        var sql = recordClick
            ? """
              WITH visited AS (UPDATE notify.tracked_links
                 SET click_count = click_count + 1,
                     first_clicked_at = COALESCE(first_clicked_at, now()),
                     last_clicked_at = now()
               WHERE id = @id
              RETURNING id, message_id, destination), recorded AS (
                  INSERT INTO notify.email_engagement_events
                      (message_id, link_id, kind, browser, operating_system, platform, automated, country_code)
                  SELECT message_id, id, 'click', @browser, @os, @platform, @automated, @country FROM visited
              ) SELECT destination FROM visited
              """
            : "SELECT destination FROM notify.tracked_links WHERE id = @id";
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("id", id);
        if (recordClick) AddVisitor(command, visitor);
        return await command.ExecuteScalarAsync(ct) as string;
    }

    public async Task RecordOpenAsync(Guid token, EmailVisitor visitor, CancellationToken ct = default)
    {
        await using var command = dataSource.CreateCommand("""
            INSERT INTO notify.email_engagement_events
                (message_id, kind, browser, operating_system, platform, automated, country_code)
            SELECT message_id, 'open', @browser, @os, @platform, @automated, @country
              FROM notify.message_tracking WHERE id = @token
            """);
        command.Parameters.AddWithValue("token", token);
        AddVisitor(command, visitor);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static void AddVisitor(NpgsqlCommand command, EmailVisitor? visitor)
    {
        visitor ??= EmailVisitor.FromUserAgent(null);
        command.Parameters.AddWithValue("browser", visitor.Browser);
        command.Parameters.AddWithValue("os", visitor.OperatingSystem);
        command.Parameters.AddWithValue("platform", visitor.Platform);
        command.Parameters.AddWithValue("automated", visitor.Automated);
        command.Parameters.AddWithValue("country", NpgsqlTypes.NpgsqlDbType.Text, (object?)visitor.CountryCode ?? DBNull.Value);
    }
}
