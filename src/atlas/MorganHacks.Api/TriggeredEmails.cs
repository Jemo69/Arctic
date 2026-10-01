using MorganHacks.Applications.Domain;
using MorganHacks.Applications.Forms;
using MorganHacks.Applications.Segments;
using MorganHacks.Applications.Services;
using MorganHacks.Lark.Data.Data;
using MorganHacks.Lark.Data.Domain;
using MorganHacks.Observability;

namespace MorganHacks.Api;

/// <summary>
/// Sends the email that follows a decision, or a form being filled in.
/// </summary>
/// <remarks>
/// <b>Nothing here may fail the thing that caused it.</b> An organizer pressing
/// Accept is making a decision; the email is a consequence of it. A send that
/// throws and takes the status change with it would mean the decision did not
/// happen because the mail did not — and the decision is the part that cannot
/// be redone, because the next thing that happens is the reviewer moving on to
/// the next row believing they have decided this one.
/// <para>
/// So every method here swallows everything, and the price of that is paid by
/// logging rather than by hoping. A silently dropped email is the other half
/// of the same failure: an applicant who never hears, an organizer who has no
/// reason to look, and nothing anywhere that says so. Every path that does not
/// send says why at <c>Error</c> or <c>Warning</c> with an
/// <see cref="Events"/> name on it, and the one that does send counts itself —
/// so the alert is the usual shape for this system, a rate that collapses
/// while the thing causing it carries on. See
/// <see cref="Events.TriggeredEmailQueued"/>.
/// </para>
/// <para>
/// <b>It goes through the queue, not around it.</b>
/// <see cref="TriggerStore.QueueOnceAsync"/> writes a <c>notify.messages</c>
/// row through <see cref="MessageQueue"/>'s own statement, so a triggered
/// email gets the same suppression check, retry schedule, bounce handling and
/// priority as a sign-in link. Sending inline from here would skip all four,
/// and the first thing it would skip is the list of addresses that have
/// already hard-bounced.
/// </para>
/// <para>
/// <b>Exactly once is the database's job.</b> This class does not ask whether
/// an occurrence has been handled; it tries to handle it and is told no. The
/// reasoning is in <c>0049</c> and in
/// <see cref="TriggerStore.QueueOnceAsync"/>, and it is the reason a
/// double-tapped Accept, a retried request, and a reviewer moving somebody
/// from accepted to expired and back all produce one letter.
/// </para>
/// <para>
/// Nothing here logs an address, a name or a subject. Application ids, trigger
/// ids, template keys and statuses — enough to find the row, and nothing about
/// who anybody is.
/// </para>
/// </remarks>
public sealed class TriggeredEmails(
    TriggerStore triggers,
    TemplateStore templates,
    MessageQueue queue,
    SavedValueStore savedValues,
    ISegmentResolver segments,
    IEventStore events,
    IFormStore forms,
    IConfiguration config,
    IHttpContextAccessor http,
    ILogger<TriggeredEmails> log)
{
    /// <summary>
    /// The statuses a trigger may be bound to.
    /// </summary>
    /// <remarks>
    /// Everything except <see cref="ApplicationStatus.Incomplete"/>, which is
    /// where a row starts rather than somewhere it is moved to — the form
    /// autosaves, so an application is created incomplete and nothing ever
    /// transitions into it. A binding on it could never fire, and offering it
    /// would be offering an automation that silently does nothing.
    /// <para>
    /// Derived from the enum rather than typed out, so a status added to the
    /// lifecycle becomes bindable by existing rather than by somebody
    /// remembering this list. The check constraint in <c>0049</c> is the one
    /// that holds; this is what the console offers and what the endpoint
    /// refuses on, so the refusal is a sentence rather than a 500.
    /// </para>
    /// </remarks>
    public static readonly IReadOnlySet<ApplicationStatus> Bindable =
        Enum.GetValues<ApplicationStatus>()
            .Where(status => status is not ApplicationStatus.Incomplete)
            .ToHashSet();

    /// <summary>
    /// An application has reached a new status. Mail whoever asked to be
    /// mailed about it.
    /// </summary>
    /// <remarks>
    /// Called after the transition has committed, never inside it. The
    /// decision is the thing that must survive, and a message queued inside
    /// the status transaction would be rolled back by anything that went wrong
    /// afterwards — including, once, by the retry that then re-queued it.
    /// </remarks>
    public Task StatusReachedAsync(
        Guid applicationId, ApplicationStatus status, CancellationToken ct = default) =>
        FireAsync(
            () => triggers.ForStatusAsync(applicationId, status.ToWire(), ct),
            applicationId,
            ct);

    /// <summary>
    /// Somebody has completed a form. Mail them, if somebody asked for that.
    /// </summary>
    /// <remarks>
    /// <paramref name="applicationId"/> is the row the submission wrote, and it
    /// is what "once" is counted per — so a double-tapped Submit on a slow
    /// phone cannot send two copies even though the unique index on
    /// (event_id, lower(email)) has already refused the second application.
    /// <para>
    /// Only the application form reaches this today, and that is a deliberate
    /// bound rather than an unfinished one. A gated form's respondent already
    /// has an application and is already in the lifecycle, so the interesting
    /// thing that happened to them is a status rather than an answer; and an
    /// anonymous submission has no address at all, which
    /// <c>0027_anonymous_form_answers.sql</c> allows on purpose and which
    /// means there is nobody to mail. Both would need their own notion of what
    /// one occurrence is, and inventing two more while the ledger has one
    /// would be inventing the drift.
    /// </para>
    /// </remarks>
    public Task FormSubmittedAsync(
        Guid formId, Guid applicationId, CancellationToken ct = default) =>
        FireAsync(() => triggers.ForFormAsync(formId, ct), applicationId, ct);

    /// <summary>
    /// Finds the binding, builds the values, and queues once.
    /// </summary>
    /// <remarks>
    /// One body for both occasions, because everything after "which binding"
    /// is identical and the one thing worse than two code paths is two code
    /// paths that disagree about whether suppression was checked.
    /// <para>
    /// The lookup is a delegate rather than a resolved trigger so that the
    /// catch below covers it too. A trigger lookup is a database round trip
    /// like any other, and the whole promise of this class is that a database
    /// having a bad afternoon does not undo somebody's decision.
    /// </para>
    /// </remarks>
    private async Task FireAsync(
        Func<Task<EmailTrigger?>> lookup, Guid applicationId, CancellationToken ct)
    {
        try
        {
            if (await lookup() is not { } trigger)
            {
                // The ordinary case by a long way: most events have no
                // automation on most occasions. Not logged, because a line per
                // status change that says nothing happened is a line nobody
                // reads and a bill somebody pays.
                return;
            }

            await QueueAsync(trigger, applicationId, ct);
        }
        catch (OperationCanceledException)
        {
            // The caller's request was abandoned, which is not a fault and not
            // something to page anybody about. The occurrence is simply not
            // mailed: the ledger has no row, so the next time this status is
            // written the trigger fires properly.
            log.LogWarning(
                "A triggered email was abandoned mid-flight. {applicationId} {event}",
                applicationId, Events.TriggeredEmailDropped);
        }
        catch (Exception e)
        {
            // Loud, and swallowed. This is the whole trade this class exists
            // to make, and the log line is the half of it that somebody can
            // act on — the status change has already committed and the
            // applicant has not been told.
            log.LogError(
                e, "A triggered email could not be queued. {applicationId} {event}",
                applicationId, Events.TriggeredEmailDropped);
        }
    }

    private async Task QueueAsync(
        EmailTrigger trigger, Guid applicationId, CancellationToken ct)
    {
        var template = await templates.FindAsync(trigger.TemplateKey, ct);
        if (template is null)
        {
            // 0049 explains why this is reachable: the binding names a key
            // rather than a row, so deleting a template leaves the automation
            // pointing at nothing instead of silently deleting it. Loud,
            // because nobody is being mailed and the fix is a person's.
            log.LogError(
                "A triggered email names a template that is not live. "
                + "{triggerId} {key} {applicationId} {event}",
                trigger.Id, trigger.TemplateKey, applicationId,
                Events.TriggeredEmailDropped);
            return;
        }

        if (!template.IsTransactional)
        {
            // The endpoint refuses to bind a broadcast template and 0017's
            // trigger makes a template's kind fixed, so reaching this means
            // the key was deleted and re-created as a broadcast. Refused here
            // anyway: kind decides the lane and the sending subdomain, so
            // sending a decision this way would put it at priority 10 behind
            // whatever blast is draining, from the domain that collects the
            // spam complaints.
            log.LogError(
                "A triggered email names a broadcast template. {triggerId} {key} {event}",
                trigger.Id, trigger.TemplateKey, Events.TriggeredEmailDropped);
            return;
        }

        var member = await segments.MemberOfAsync(applicationId, ct);
        if (member is null)
        {
            // An id that names no application, which a caller acting on a row
            // it just wrote should never produce. Logged rather than thrown
            // because the alternative is a failed send becoming a failed
            // decision.
            log.LogError(
                "A triggered email found no application to merge from. "
                + "{triggerId} {applicationId} {event}",
                trigger.Id, applicationId, Events.TriggeredEmailDropped);
            return;
        }

        // The same check the claim query makes, through the same method, on
        // the transactional lane. Checked here as well as there because a row
        // the claim query skips stays pending for ever, and pending is the
        // queue's way of saying still owed — MessageQueue.SuppressAsync says
        // so in as many words. The lane matters: a hard bounce or a complaint
        // stops this, and an unsubscribe does not, because somebody who opted
        // out of announcements still asked for their decision by applying.
        if (await queue.IsSuppressedAsync(member.Email, transactional: true, ct))
        {
            log.LogWarning(
                "A triggered email was not sent to a suppressed address. "
                + "{triggerId} {applicationId} {event}",
                trigger.Id, applicationId, Events.TriggeredEmailDropped);
            return;
        }

        var messageId = await triggers.QueueOnceAsync(
            trigger,
            applicationId,
            template,
            member.Email,
            member.PersonId,
            await ValuesAsync(trigger, template, member, ct),
            http.HttpContext?.CorrelationId(),
            ct);

        if (messageId is null)
        {
            // Already mailed. The ordinary shape of this is a double-tap or a
            // retry, so it is not a warning — but it is worth a line, because
            // a rate of these that climbs on its own would mean something is
            // replaying status changes.
            log.LogInformation(
                "A triggered email had already been sent for this. "
                + "{triggerId} {applicationId} {event}",
                trigger.Id, applicationId, Events.TriggeredEmailSkipped);
            return;
        }

        // Counted so the absence can be alerted on, like the sign-in link
        // next door. Decisions being made while this rate sits at zero is the
        // failure that looks like nothing at all from the outside.
        log.LogInformation(
            "A triggered email was queued. {triggerId} {key} {applicationId} {event}",
            trigger.Id, trigger.TemplateKey, applicationId, Events.TriggeredEmailQueued);
    }

    /// <summary>
    /// The placeholders this one message can fill in.
    /// </summary>
    /// <remarks>
    /// <see cref="MergeFields.Values"/>, and nothing of its own. That is the
    /// point: the catalogue an author is offered in the editor, the one a
    /// campaign checks coverage against, and the one a triggered send fills
    /// have to be the same catalogue, or a template that previews correctly
    /// goes out with braces in it. The transactional path's hand-built
    /// one-or-two-key dictionary is what the sign-in link needs and is not a
    /// catalogue; this is why a triggered email can say
    /// <c>{{firstName}}</c> and <c>{{link.portal}}</c> at all.
    /// <para>
    /// The season comes from the binding's own event rather than from the
    /// newest one, so a decision letter on last year's event names last year's
    /// dates. <see cref="CampaignEndpoints"/> falls back to the newest event
    /// for a segment that does not name one; a trigger always names one.
    /// </para>
    /// <para>
    /// The form behind <c>{{form.*}}</c> is the <em>template's</em> form from
    /// <c>0047</c>, not the trigger's. They are two different questions: the
    /// trigger's form is when to send, and the template's is what the email is
    /// about. They are usually the same form on a form-submitted binding and
    /// are deliberately not forced to be — an acceptance letter that links the
    /// RSVP form is the case that would otherwise be impossible.
    /// </para>
    /// <para>
    /// A placeholder with nothing behind it is left standing rather than
    /// refusing the send, which is the opposite of what a campaign does and is
    /// the right way round here. A campaign has an approver in front of it who
    /// can be told to fix the wording; a trigger fires with nobody watching,
    /// and withholding somebody's decision because a template names a date the
    /// team has not agreed is worse than an awkward sentence. It is also mostly
    /// theoretical for the fields that matter: the
    /// <c>submitted_applications_are_complete</c> check means every
    /// application that can legally reach a decided status has a first name, a
    /// school and a country on it.
    /// </para>
    /// </remarks>
    private async Task<Dictionary<string, string>> ValuesAsync(
        EmailTrigger trigger,
        EmailTemplate template,
        SegmentMember member,
        CancellationToken ct)
    {
        var season = await events.ByIdAsync(trigger.EventId, ct);
        var saved = await savedValues.ListAsync(ct);
        var paper = template.FormId is { } formId
            ? await forms.ByIdAsync(formId, ct)
            : null;

        return MergeFields.Values(member, config, season, saved, paper);
    }
}
