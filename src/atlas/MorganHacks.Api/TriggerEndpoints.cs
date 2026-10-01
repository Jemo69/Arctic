using MorganHacks.Applications.Domain;
using MorganHacks.Applications.Forms;
using MorganHacks.Applications.Services;
using MorganHacks.Identity.Domain;
using MorganHacks.Lark.Data.Data;
using MorganHacks.Lark.Data.Domain;

namespace MorganHacks.Api;

/// <summary>
/// Where an organizer says which email follows which event.
/// </summary>
/// <remarks>
/// <b>Behind two permissions, and both are the point.</b>
/// <c>email.manage_templates</c> because binding a template is deciding what a
/// template is for, which is the same reach as writing one;
/// <c>email.send_templated</c> because this is the screen on which somebody
/// causes four hundred emails to leave the building without pressing send
/// again. The same pair gates the template test send next door, for the same
/// reason: it is a templates-screen action whose consequence is mail.
/// <para>
/// Deliberately not <c>email.send_broadcast</c>, which is the other candidate
/// and is on <see cref="Permission.Sensitive"/>. That one exists because a
/// broadcast is approved by somebody other than its author and cannot be
/// recalled; the ceremony around it is a second person reading the copy before
/// several hundred people do. A trigger is closer to the sign-in link than to
/// a blast — one message, to one person, about something that happened to
/// them — and requiring an approver per binding would mean the acceptance
/// email is either set up weeks early or not at all.
/// </para>
/// <para>
/// <b>The read is one event's worth.</b> There is one event a year and
/// <c>0049</c> scopes a binding to it, so this answers for the season being
/// run unless a caller names another — the same defaulting the applicants list
/// does, and for the same reason: the console cannot link to its own screen
/// without already knowing an id otherwise.
/// </para>
/// <para>
/// Nothing here logs a subject, a body or an address. Event ids, trigger ids,
/// template keys and statuses.
/// </para>
/// </remarks>
public static class TriggerEndpoints
{
    public static IEndpointRouteBuilder MapEmailTriggers(this IEndpointRouteBuilder app)
    {
        var bindings = app.MapGroup("/admin/email-triggers");

        bindings.MapGet("", List)
                .RequirePermission(Permission.EmailManageTemplates);

        // A PUT on the collection rather than a POST, because the occasion is
        // the identity: there is one binding per status per season and saving
        // the same occasion twice is one automation rather than two. 0049's
        // partial unique indexes are what actually hold that, and the upsert
        // under this means the screen has one form instead of a create and an
        // edit that differ only in which refusal they produce.
        bindings.MapPut("", Save)
                .RequirePermission(Permission.EmailManageTemplates)
                .RequirePermission(Permission.EmailSendTemplated);

        bindings.MapPut("/{id:guid}/enabled", SetEnabled)
                .RequirePermission(Permission.EmailManageTemplates)
                .RequirePermission(Permission.EmailSendTemplated);

        bindings.MapDelete("/{id:guid}", Remove)
                .RequirePermission(Permission.EmailManageTemplates)
                .RequirePermission(Permission.EmailSendTemplated);

        return app;
    }

    /// <summary>
    /// The bodies these take.
    /// </summary>
    /// <remarks>
    /// Nullable, and checked in the handler rather than required on the
    /// parameter, for the reason every other admin surface here gives: minimal
    /// APIs bind the body before endpoint filters run, so a required body
    /// answers a request with none before the permission gate has looked at
    /// it.
    /// </remarks>
    public sealed record SaveRequest(
        Guid? EventId,
        string? Occasion,
        Guid? FormId,
        string? Status,
        string? TemplateKey,
        bool? Enabled);

    public sealed record EnabledRequest(bool? Enabled);

    /// <summary>
    /// Every automation on one season, plus what can be bound.
    /// </summary>
    /// <remarks>
    /// The statuses and the forms ride along with the list. The console draws
    /// one screen out of all three — a table of bindings, a status picker and
    /// a form picker — and three round trips to fill them is a waterfall for
    /// no benefit. Same reasoning as the applicants list next door.
    /// <para>
    /// The forms are this event's, by <see cref="IFormStore.ForEventAsync"/>,
    /// which already excludes the ones <c>0038</c> removed. A binding pointing
    /// at a form that has since been removed therefore has no name to show;
    /// the row still lists, because an automation nobody can see is worse than
    /// one with a blank in it.
    /// </para>
    /// </remarks>
    private static async Task<IResult> List(
        TriggerStore triggers,
        IEventStore events,
        IFormStore forms,
        CancellationToken ct,
        Guid? eventId = null)
    {
        var all = await events.ListAsync(ct);
        if (all.Count == 0)
        {
            // A fresh database. Not an error: there is nothing to bind an
            // automation to yet, and the screen says so.
            return Results.Ok(new
            {
                events = all,
                chosen = (object?)null,
                triggers = Array.Empty<object>(),
                statuses = Bindable(),
                forms = Array.Empty<object>(),
            });
        }

        var chosen = all.FirstOrDefault(e => e.Id == eventId) ?? all[0];

        var listings = await triggers.ListAsync(chosen.Id, ct);
        var paper = await forms.ForEventAsync(chosen.Id, ct);
        var names = paper.ToDictionary(form => form.Id, form => form.Name);

        return Results.Ok(new
        {
            events = all,
            chosen,
            triggers = listings.Select(listing => Describe(listing, names)),
            statuses = Bindable(),

            // Only the forms a submission trigger makes sense on. A gated or
            // anonymous form never reaches the fire path — see
            // TriggeredEmails.FormSubmittedAsync for why — so offering one
            // would be offering an automation that silently never runs.
            forms = paper.Where(form => form.IsApplication)
                         .Select(form => new { id = form.Id, name = form.Name, code = form.Code }),
        });
    }

    /// <summary>
    /// Writes the binding for one occasion.
    /// </summary>
    /// <remarks>
    /// Four refusals, and each one is a sentence rather than a constraint
    /// violation. The constraints in <c>0049</c> are what actually hold —
    /// a hand-written INSERT during the event is bound by them and not by this
    /// — and these exist so that somebody typing into the console is told what
    /// is wrong with what they typed.
    /// <para>
    /// The template has to exist and has to be transactional. The second is
    /// the one worth stating: <c>kind</c> decides the sending lane and the
    /// subdomain, so a broadcast template bound here would send somebody's
    /// decision at priority 10 behind whatever blast is draining, from the
    /// domain that collects the spam complaints — which is the precise failure
    /// <c>0003</c>'s two-lane design exists to prevent.
    /// </para>
    /// <para>
    /// The form has to be this event's and has to be an application form. The
    /// first stops a binding scoped to one season firing on another's form;
    /// the second refuses an automation that could never run, rather than
    /// storing one that looks set up.
    /// </para>
    /// </remarks>
    private static async Task<IResult> Save(
        SaveRequest? request,
        HttpContext http,
        TriggerStore triggers,
        TemplateStore templates,
        IEventStore events,
        IFormStore forms,
        ILogger<EmailTrigger> log,
        CancellationToken ct)
    {
        if (!TriggerOccasions.TryParse(request?.Occasion, out var occasion))
        {
            return Results.BadRequest(new { error = "No such trigger occasion." });
        }

        // A named event is checked rather than trusted, because it decides
        // which season's applicants an automation will mail and it arrives in
        // the request body. With none named this takes the newest, like the
        // read above.
        var season = request!.EventId is { } named
            ? (await events.ListAsync(ct)).FirstOrDefault(e => e.Id == named)
            : (await events.ListAsync(ct)).FirstOrDefault();

        if (season is null)
        {
            return Results.BadRequest(new { error = "No such event." });
        }

        if (string.IsNullOrWhiteSpace(request.TemplateKey))
        {
            return Results.BadRequest(new { error = "Choose an email to send." });
        }

        var template = await templates.FindAsync(request.TemplateKey, ct);
        if (template is null)
        {
            return Results.BadRequest(new { error = "No such template." });
        }

        if (!template.IsTransactional)
        {
            return Results.BadRequest(new
            {
                error = "An automatic email has to be a transactional template. "
                        + "A broadcast template sends from the announcements "
                        + "subdomain and queues behind every blast, which is not "
                        + "where a decision belongs.",
            });
        }

        Guid? formId = null;
        string? status = null;

        if (occasion is TriggerOccasion.FormSubmitted)
        {
            if (request.FormId is not { } chosen)
            {
                return Results.BadRequest(new { error = "Choose a form." });
            }

            var form = await forms.ByIdAsync(chosen, ct);
            if (form is null || form.EventId != season.Id)
            {
                // One answer for both, because from here they are the same
                // thing: there is no form of that id on this season to bind to.
                return Results.BadRequest(new { error = "No such form on this event." });
            }

            if (!form.IsApplication)
            {
                return Results.BadRequest(new
                {
                    error = "Only the application form can send an email on "
                            + "submission. A form people sign in to answer is "
                            + "already about somebody with an application, and an "
                            + "anonymous one has no address to send to.",
                });
            }

            formId = form.Id;
        }
        else
        {
            if (!ApplicationStatuses.TryParse(request.Status, out var reached)
                || !TriggeredEmails.Bindable.Contains(reached))
            {
                return Results.BadRequest(new { error = "No such status to send on." });
            }

            status = reached.ToWire();
        }

        var saved = await triggers.SaveAsync(
            season.Id, occasion, formId, status, template.Key,
            request.Enabled ?? true, http.PersonId(), ct);

        // The key and the occasion, never the subject. This is the line that
        // explains, weeks later, why acceptance emails started going out on a
        // Tuesday.
        log.LogInformation(
            "An email trigger was bound. {actor} {triggerId} {occasion} {status} {key}",
            http.PersonId(), saved.Id, saved.Occasion.ToWire(), saved.Status,
            saved.TemplateKey);

        return Results.Ok(new { triggers = await Rows(triggers, forms, season.Id, ct) });
    }

    /// <summary>
    /// Switches one on or off.
    /// </summary>
    /// <remarks>
    /// Its own route rather than a field on the save, because this is the
    /// thing somebody does in a hurry. The week decisions go out is exactly
    /// the week somebody needs the mail to stop for ten minutes while they fix
    /// the wording, and making them re-submit the whole binding to do it is
    /// how a binding comes back pointing at the wrong template.
    /// <para>
    /// The ledger is untouched, which is the whole reason this is not a
    /// delete: switching back on does not re-mail the people already told.
    /// </para>
    /// </remarks>
    private static async Task<IResult> SetEnabled(
        Guid id,
        EnabledRequest? request,
        HttpContext http,
        TriggerStore triggers,
        IFormStore forms,
        ILogger<EmailTrigger> log,
        CancellationToken ct)
    {
        if (request?.Enabled is not { } enabled)
        {
            return Results.BadRequest(new { error = "Say whether it is on or off." });
        }

        var updated = await triggers.SetEnabledAsync(id, enabled, ct);
        if (updated is null)
        {
            return Results.NotFound(new { error = "No such trigger." });
        }

        log.LogInformation(
            "An email trigger was switched. {actor} {triggerId} {enabled}",
            http.PersonId(), id, enabled);

        return Results.Ok(new
        {
            triggers = await Rows(triggers, forms, updated.EventId, ct),
        });
    }

    /// <summary>
    /// Removes a binding, and the record of what it has sent with it.
    /// </summary>
    /// <remarks>
    /// The cascade in <c>0049</c> is deliberate and has one consequence worth
    /// knowing: deleting a binding and setting an identical one up again will
    /// mail everybody it has already mailed, because the ledger the uniqueness
    /// is counted against went with it. That is why <see cref="SetEnabled"/>
    /// exists, and why the console offers the switch first.
    /// <para>
    /// Keeping the ledger instead was the alternative, and it is worse: an
    /// orphaned record keyed on a trigger id nobody can see would silently
    /// stop a genuinely new automation from ever mailing the people the old one
    /// reached, and nothing on any screen would say why.
    /// </para>
    /// </remarks>
    private static async Task<IResult> Remove(
        Guid id,
        HttpContext http,
        TriggerStore triggers,
        IFormStore forms,
        ILogger<EmailTrigger> log,
        CancellationToken ct)
    {
        // Read before the delete, because the answer is the remaining list for
        // that event and afterwards there is no row to learn the event from.
        var doomed = await triggers.FindAsync(id, ct);
        if (doomed is null || !await triggers.DeleteAsync(id, ct))
        {
            return Results.NotFound(new { error = "No such trigger." });
        }

        log.LogInformation(
            "An email trigger was removed. {actor} {triggerId} {occasion} {key}",
            http.PersonId(), id, doomed.Occasion.ToWire(), doomed.TemplateKey);

        return Results.Ok(new
        {
            triggers = await Rows(triggers, forms, doomed.EventId, ct),
        });
    }

    /// <summary>
    /// The whole list, after every write.
    /// </summary>
    /// <remarks>
    /// Every write answers with the current list, so the screen never has to
    /// work out what it now looks like — the same contract the saved values
    /// endpoint has, and for the same reason: two people editing at once means
    /// one of them sees the other's row appear rather than a stale screen that
    /// disagrees with what a decision will actually send.
    /// </remarks>
    private static async Task<IEnumerable<object>> Rows(
        TriggerStore triggers, IFormStore forms, Guid eventId, CancellationToken ct)
    {
        var listings = await triggers.ListAsync(eventId, ct);
        var names = (await forms.ForEventAsync(eventId, ct))
            .ToDictionary(form => form.Id, form => form.Name);

        return listings.Select(listing => Describe(listing, names));
    }

    private static object Describe(
        TriggerListing listing, IReadOnlyDictionary<Guid, string> formNames) => new
        {
            id = listing.Trigger.Id,

            // The stored spelling on the wire, like every status this API
            // serves. Two spellings of one occasion is one of them being
            // wrong somewhere.
            occasion = listing.Trigger.Occasion.ToWire(),
            formId = listing.Trigger.FormId,

            // Null where the form has been removed since the binding was made.
            // The screen says so rather than hiding the row.
            formName = listing.Trigger.FormId is { } id && formNames.TryGetValue(id, out var name)
                ? name
                : null,

            status = listing.Trigger.Status,
            templateKey = listing.Trigger.TemplateKey,

            // The reason 0049 binds by key rather than by id, surfaced. An
            // automation pointing at a deleted template sends nothing, and the
            // only way anybody finds out otherwise is an applicant who never
            // heard.
            templateMissing = !listing.TemplateLive,

            enabled = listing.Trigger.Enabled,
            sent = listing.Sent,
            updatedAt = listing.Trigger.UpdatedAt,
        };

    /// <summary>
    /// The statuses an automation may be bound to, as the console offers them.
    /// </summary>
    /// <remarks>
    /// From <see cref="TriggeredEmails.Bindable"/> rather than typed out here,
    /// so the list the console shows, the list this endpoint refuses on, and
    /// the list the fire path can act on are one list. The check constraint in
    /// <c>0049</c> is the one that holds.
    /// </remarks>
    private static IEnumerable<string> Bindable() =>
        Enum.GetValues<ApplicationStatus>()
            .Where(TriggeredEmails.Bindable.Contains)
            .Select(status => status.ToWire());
}
