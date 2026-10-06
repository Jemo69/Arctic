using MorganHacks.Applications.Services;
using MorganHacks.Identity;
using MorganHacks.Identity.Domain;
using MorganHacks.Identity.Services;
using MorganHacks.Lark.Data.Data;

namespace MorganHacks.Api;

public static class EmailAnalyticsEndpoints
{
    public static IEndpointRouteBuilder MapEmailAnalytics(this IEndpointRouteBuilder app)
    {
        app.MapGet("/admin/analytics/email", Read)
             .RequirePermission(Permission.EmailViewStats);
        app.MapGet("/admin/analytics/email/campaigns", ReadCampaigns)
             .RequirePermission(Permission.EmailViewStats);
        return app;
    }

    /// <summary>
    /// How one event's mail performed. Requires <c>email.view_stats</c>.
    /// </summary>
    /// <remarks>
    /// Scoped to an event, and the events ride along so the console can draw a
    /// picker, for the same reason they do on the applicant analytics and forms
    /// screens: every other number on the dashboard belongs to an event, and
    /// one averaged across a workspace of past ones answers a question nobody
    /// is asking. With no event named it answers for the most recent one.
    /// </remarks>
    private static async Task<IResult> Read(
        Guid? eventId, IEventStore events, EmailAnalyticsStore analytics, CancellationToken ct)
    {
        var all = await events.ListAsync(ct);
        var chosen = Chosen(eventId, all);
        if (eventId.HasValue && chosen is null) return Results.NotFound(new { error = "No such event." });
        var result = chosen is null ? null : await analytics.ReadAsync(chosen.Id, ct);
        return Results.Ok(new { events = all, chosen, analytics = result });
    }

    /// <summary>
    /// The broadcasts that performed best on one event, ranked by click rate.
    /// Requires <c>email.view_stats</c>.
    /// </summary>
    /// <remarks>
    /// Ranked within the event rather than across the workspace, because a
    /// click rate is only comparable between sends that went to the same
    /// people. Preview HTML comes back only for somebody who may edit
    /// templates; the ranking never depends on it.
    /// </remarks>
    private static async Task<IResult> ReadCampaigns(
        HttpContext http,
        Guid? eventId,
        IEventStore events,
        EmailAnalyticsStore analytics,
        PermissionService permissions,
        CancellationToken ct)
    {
        var all = await events.ListAsync(ct);
        var chosen = Chosen(eventId, all);
        if (eventId.HasValue && chosen is null) return Results.NotFound(new { error = "No such event." });
        var effective = await permissions.ForAsync(http.PersonId(), ct);
        return Results.Ok(new
        {
            events = all,
            chosen,
            campaigns = chosen is null
                ? null
                : await analytics.ReadBestCampaignsAsync(chosen.Id,
                    effective.Can(Permission.EmailManageTemplates), ct)
        });
    }

    /// <summary>The named event, or the most recent one when none is named.</summary>
    /// <remarks>
    /// Newest-first, because <see cref="IEventStore.ListAsync"/> already is and
    /// that is the event being run. Null is returned rather than a fallback when
    /// an event <em>is</em> named and cannot be found, so the caller can say so
    /// rather than quietly answering for a different event.
    /// </remarks>
    private static EventSummary? Chosen(Guid? eventId, IReadOnlyList<EventSummary> all) =>
        eventId.HasValue ? all.FirstOrDefault(item => item.Id == eventId.Value) : all.FirstOrDefault();
}
