using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MorganHacks.Applications.Forms;
using MorganHacks.Applications.Segments;
using MorganHacks.Identity.Services;
using MorganHacks.Lark.Data.Data;
using MorganHacks.Lark.Data.Domain;
using Npgsql;
using NpgsqlTypes;

namespace MorganHacks.Api.Tests;

/// <summary>
/// The emails that follow a decision, against a real database running the real
/// migrations.
/// </summary>
/// <remarks>
/// Almost nothing here would be worth running against a double. The promise
/// this feature makes is "exactly once, however many times the occasion
/// happens", and that promise is a primary key on
/// <c>notify.email_trigger_sends</c> and a transaction that rolls its own
/// message back when it loses — see <c>0049</c>. An in-memory stand-in would
/// pass whether or not the constraint exists, which is to say it would pass on
/// the version of this feature that mails four hundred people twice.
/// <para>
/// The templates these send are inserted by the tests rather than by a
/// migration, and the copy in them is deliberate nonsense. Template wording
/// belongs to the people who send the mail, and a plausible-looking body
/// committed to a test is a body somebody eventually copies.
/// </para>
/// <para>
/// One applicant and one event per test, on a unique address. Several of these
/// count rows in <c>notify.messages</c> by recipient, and a shared event would
/// make the second test fail on the address the first one used — the unique
/// index on (event_id, lower(email)) says so.
/// </para>
/// </remarks>
public class EmailTriggerTests(ApplicationsDatabase db)
    : IClassFixture<ApplicationsDatabase>, IAsyncLifetime
{
    private WebApplicationFactory<Program> _app = null!;

    public Task InitializeAsync()
    {
        _app = Factory();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _app.Dispose();
        return Task.CompletedTask;
    }

    // -------------------------------------------------------- exactly once ---

    [Fact]
    public async Task A_decision_mails_the_applicant_once_however_often_it_is_made()
    {
        // The case the ledger exists for, and it is not a double-click: the
        // lifecycle already refuses accepted -> accepted, so a repeated POST
        // cannot be what sends twice. What can is reinstatement, which
        // StatusTransition permits on purpose — accepted, then expired when
        // the RSVP deadline passes, then accepted again because the applicant
        // got in touch. That is two arrivals at `accepted` and two
        // status_history rows, and anything keyed on the transition rather
        // than on the person would send a second acceptance letter.
        var (eventId, applicationId, email) = await Applicant("twice", "under_review");
        var key = await Template("Hello {{firstName}}");
        var organizer = await SuperAdmin();

        await Bind(organizer, eventId, "accepted", key);

        Assert.Equal(HttpStatusCode.OK, (await Decide(organizer, applicationId, "accepted")).StatusCode);
        Assert.Equal(1, await MessagesTo(email));

        // No trigger on expired, so this leg must queue nothing of its own.
        Assert.Equal(HttpStatusCode.OK, (await Decide(organizer, applicationId, "expired")).StatusCode);
        Assert.Equal(1, await MessagesTo(email));

        Assert.Equal(HttpStatusCode.OK, (await Decide(organizer, applicationId, "accepted")).StatusCode);
        Assert.Equal(1, await MessagesTo(email));

        // And the ledger says one, not two, which is the fact the count above
        // is a consequence of.
        Assert.Equal(1, await LedgerRowsFor(applicationId));
    }

    [Fact]
    public async Task A_second_attempt_at_one_occasion_queues_nothing_and_leaves_nothing_behind()
    {
        // The constraint on its own, reached directly rather than through the
        // lifecycle. The test above proves the behaviour an organizer sees;
        // this one proves why, and it asserts the part that is easy to get
        // wrong: the loser inserts its message first and has to roll it back.
        // A version that claimed the slot before queueing would pass the
        // message count here and silently drop the mail the first time the
        // enqueue failed.
        var (eventId, applicationId, email) = await Applicant("constraint", "under_review");
        var key = await Template("Hello {{firstName}}");
        var organizer = await SuperAdmin();
        await Bind(organizer, eventId, "accepted", key);
        await Decide(organizer, applicationId, "accepted");

        var triggers = new TriggerStore(db.DataSource, new MessageQueue(db.DataSource));
        var trigger = Assert.Single(await triggers.ListAsync(eventId)).Trigger;
        var template = (await new TemplateStore(db.DataSource).FindAsync(key))!;

        var again = await triggers.QueueOnceAsync(
            trigger, applicationId, template, email, null,
            new Dictionary<string, string> { ["firstName"] = "Ada" });

        Assert.Null(again);

        // One message, not two — so the speculative insert really was rolled
        // back rather than left pending for lark to pick up.
        Assert.Equal(1, await MessagesTo(email));
    }

    // --------------------------------------------- the decision comes first ---

    [Fact]
    public async Task A_decision_still_lands_when_the_email_cannot_be_queued()
    {
        // The whole trade this feature makes. The reviewer's next action after
        // pressing Accept is to move on to the next row believing this one is
        // decided, so a send that failed must not have undone the decision —
        // and the only honest way to test that is to make the send genuinely
        // throw rather than merely find nothing to do.
        //
        // ISegmentResolver is the seam because it is the one interface on the
        // fire path: a resolver that throws on MemberOfAsync fails the send
        // after the trigger has been found and before anything is queued,
        // which is the shape a database blip has.
        using var app = Factory(services =>
            services.AddSingleton<ISegmentResolver, FailingSegmentResolver>());

        var (eventId, applicationId, email) = await Applicant("broken", "under_review");
        var key = await Template("Hello {{firstName}}");
        var organizer = await SuperAdmin();
        await Bind(organizer, eventId, "accepted", key, app);

        var decided = await Send(app, HttpMethod.Post,
            $"/admin/applicants/{applicationId}/status", organizer, new { status = "accepted" });

        Assert.Equal(HttpStatusCode.OK, decided.StatusCode);
        Assert.Equal("accepted", (await Body(decided)).GetProperty("status").GetString());

        // The decision is in the table, which is the assertion that matters:
        // a 200 with no row would be the same bug wearing a better hat.
        Assert.Equal("accepted", await StatusOf(applicationId));
        Assert.Equal(0, await MessagesTo(email));

        // And nothing claimed the occasion. The ledger only records sends that
        // happened, so re-reaching `accepted` fires properly once whatever
        // broke is fixed — which is the difference between an email that is
        // owed and one that has been written off.
        Assert.Equal(0, await LedgerRowsFor(applicationId));
    }

    // ------------------------------------------------------- suppressions ---

    [Theory]
    [InlineData("hard_bounce", 0)]
    [InlineData("complaint", 0)]
    [InlineData("unsubscribed", 1)]
    public async Task A_suppressed_address_is_respected_on_the_lane_it_applies_to(
        string reason, int expected)
    {
        // The lane rule, not just the list. MessageQueue says it in as many
        // words: a bounce or a complaint blocks both lanes because a dead
        // address is dead either way, and an unsubscribe blocks broadcast only
        // because somebody who opted out of announcements must still get their
        // decision — which they asked for by applying. A triggered decision is
        // transactional, so the third row here has to send.
        //
        // Checked through MessageQueue.IsSuppressedAsync rather than against a
        // list of reasons written here, so the two cannot drift: the same
        // method the claim query's rules are documented against is the one
        // deciding.
        var (eventId, applicationId, email) = await Applicant($"suppressed-{reason}", "under_review");
        await Suppress(email, reason);
        var key = await Template("Hello {{firstName}}");
        var organizer = await SuperAdmin();
        await Bind(organizer, eventId, "accepted", key);

        Assert.Equal(HttpStatusCode.OK, (await Decide(organizer, applicationId, "accepted")).StatusCode);

        Assert.Equal("accepted", await StatusOf(applicationId));
        Assert.Equal(expected, await MessagesTo(email));

        // The ledger agrees with the queue either way. A skipped address that
        // claimed the slot would mean lifting the suppression and re-deciding
        // never told them anything.
        Assert.Equal(expected, await LedgerRowsFor(applicationId));
    }

    // ------------------------------------------------------- merge values ---

    [Fact]
    public async Task A_triggered_email_fills_in_the_whole_placeholder_catalogue()
    {
        // The reason this feature could not simply reuse the transactional
        // path. QueuedEmailSender hands the renderer a one- or two-key
        // dictionary, which is everything a sign-in link needs and nothing an
        // acceptance letter does: a decision email wants the applicant's name,
        // the season's name, a link to the portal, a link to a form and
        // whatever an organizer has saved. All five come from
        // MergeFields.Values, which is the same call a campaign preview makes
        // — so a template that previews correctly in the editor renders
        // correctly here, and there is no second catalogue to keep in step.
        var (eventId, applicationId, email) = await Applicant("merge", "under_review");
        await Rename(eventId, "Hackathon Nonsense");
        await SaveValue("venue", "Room 101");

        // The form the template is about, which is 0047's binding and is a
        // different question from which form a trigger fires on. An
        // acceptance letter linking the RSVP form is the case that makes them
        // different, and it is the one set up here.
        var rsvp = await new PostgresFormStore(db.DataSource)
            .CreateAsync(eventId, "RSVP", "survey", null);

        var key = await Template(
            subject: "Hello {{firstName}}",
            html: "<p>{{event.name}} — {{link.portal}} — {{saved.venue}}</p>",
            text: "{{form.link}} {{lastName}}",
            formId: rsvp.Id);

        var organizer = await SuperAdmin();
        await Bind(organizer, eventId, "accepted", key);
        await Decide(organizer, applicationId, "accepted");

        var (subject, html, text) = await RenderedTo(email);

        // The applicant's own columns, read the way a segment reads them.
        Assert.Equal("Hello Ada", subject);
        Assert.Contains("Lovelace", text);

        // The season, by the trigger's own event rather than the newest one.
        Assert.Contains("Hackathon Nonsense", html);

        // The origins, so a staging template cannot link to production.
        Assert.Contains("http://localhost:3000", html);
        Assert.Contains($"http://localhost:3002/{rsvp.Code}", text);

        // And what somebody typed once and reuses.
        Assert.Contains("Room 101", html);

        // Nothing left standing. A placeholder that survives rendering is the
        // failure this whole catalogue exists to prevent, and it reads fine
        // right up until an applicant gets "Hi {{firstName}},".
        Assert.DoesNotContain("{{", subject);
        Assert.DoesNotContain("{{", html);
        Assert.DoesNotContain("{{", text);
    }

    // ------------------------------------------------- the other occasion ---

    [Fact]
    public async Task Finishing_the_application_form_mails_the_applicant_once()
    {
        // The other half of the feature, through the public endpoint rather
        // than through a store. The form is unauthenticated and open to the
        // internet, so the only version of this worth asserting is the one a
        // browser actually produces.
        var forms = new PostgresFormStore(db.DataSource);
        var eventId = await db.AddEventAsync();
        var form = await forms.CreateAsync(eventId, "Application", "application", null);
        var draft = await forms.DraftAsync(form.Id, null);
        await forms.SaveDraftAsync(form.Id, draft.Fields);
        await forms.PublishAsync(form.Id, null);

        var key = await Template("Thanks {{firstName}}");
        var organizer = await SuperAdmin();

        var bound = await Send(_app, HttpMethod.Put, "/admin/email-triggers", organizer, new
        {
            eventId,
            occasion = "form_submitted",
            formId = form.Id,
            templateKey = key,
        });
        Assert.Equal(HttpStatusCode.OK, bound.StatusCode);

        var email = Unique("submitted");
        var submitted = await Client().PostAsJsonAsync(
            $"/forms/{form.Code}/submit", new { answers = Answers(email) });

        Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        Assert.Equal(1, await MessagesTo(email));

        // The second submission is refused by the unique index on
        // (event_id, lower(email)) before anything is queued, which is the
        // point: on this path the application row is the occurrence, so the
        // two protections agree rather than overlapping by accident.
        var twice = await Client().PostAsJsonAsync(
            $"/forms/{form.Code}/submit", new { answers = Answers(email) });

        Assert.Equal(HttpStatusCode.Conflict, twice.StatusCode);
        Assert.Equal(1, await MessagesTo(email));
    }

    // -------------------------------------------------------- the bindings ---

    [Fact]
    public async Task A_broadcast_template_cannot_be_bound_to_an_occasion()
    {
        // kind decides the lane and the sending subdomain — 0003's whole
        // two-lane design — so a decision sent from a broadcast template goes
        // out at priority 10 behind whatever blast is draining, from the
        // domain that collects the spam complaints. Refused at bind time,
        // where somebody can read the sentence, as well as at fire time, where
        // only a log line can.
        var eventId = await db.AddEventAsync();
        var organizer = await SuperAdmin();
        var broadcast = await Template("Hello", kind: "broadcast");

        var refused = await Send(_app, HttpMethod.Put, "/admin/email-triggers", organizer, new
        {
            eventId,
            occasion = "status_reached",
            status = "accepted",
            templateKey = broadcast,
        });

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("transactional", (await Body(refused)).GetProperty("error").GetString()!);
        Assert.Empty(await Triggers(eventId));
    }

    [Fact]
    public async Task Saving_the_same_occasion_twice_rebinds_it_rather_than_adding_a_second()
    {
        // Two bindings on one status would mean two emails, and nobody has
        // ever wanted two emails — somebody with two things to say says them
        // in one template. The partial unique index in 0049 is what holds it;
        // the upsert under this endpoint is what turns it into an edit rather
        // than a refusal about a row the person is looking at.
        var eventId = await db.AddEventAsync();
        var organizer = await SuperAdmin();
        var first = await Template("One");
        var second = await Template("Two");

        await Bind(organizer, eventId, "accepted", first);
        await Bind(organizer, eventId, "accepted", second);

        var only = Assert.Single(await Triggers(eventId));
        Assert.Equal(second, only.GetProperty("templateKey").GetString());

        // A different status is a different occasion, so it is a second row.
        await Bind(organizer, eventId, "rejected", first);
        Assert.Equal(2, (await Triggers(eventId)).Count);
    }

    [Fact]
    public async Task Switching_a_trigger_off_stops_the_mail_without_forgetting_who_was_told()
    {
        // The reason the switch exists rather than only a delete. Deleting
        // takes the ledger with it, so deleting and re-creating would mail
        // everybody already told; switching keeps it, which is what somebody
        // pausing an automation for ten minutes to fix the wording is
        // assuming.
        var (eventId, applicationId, email) = await Applicant("switched", "under_review");
        var key = await Template("Hello {{firstName}}");
        var organizer = await SuperAdmin();
        await Bind(organizer, eventId, "accepted", key);

        var id = Assert.Single(await Triggers(eventId)).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await Send(_app, HttpMethod.Put,
            $"/admin/email-triggers/{id}/enabled", organizer, new { enabled = false })).StatusCode);

        await Decide(organizer, applicationId, "accepted");
        Assert.Equal(0, await MessagesTo(email));

        // Back on, and the reinstatement path through expired: the occasion
        // genuinely has not been mailed, so now it is.
        Assert.Equal(HttpStatusCode.OK, (await Send(_app, HttpMethod.Put,
            $"/admin/email-triggers/{id}/enabled", organizer, new { enabled = true })).StatusCode);

        await Decide(organizer, applicationId, "expired");
        await Decide(organizer, applicationId, "accepted");
        Assert.Equal(1, await MessagesTo(email));
    }

    [Fact]
    public async Task A_trigger_pointing_at_a_deleted_template_says_so_and_sends_nothing()
    {
        // The cost of binding by key rather than by id, which 0049 argues is
        // the right cost: an edit must not break the automation, and the other
        // side of that is that a deletion leaves it pointing at nothing.
        // Loud on the screen, and silent in the inbox rather than throwing
        // under somebody's decision.
        var (eventId, applicationId, email) = await Applicant("orphaned", "under_review");
        var key = await Template("Hello {{firstName}}");
        var organizer = await SuperAdmin();
        await Bind(organizer, eventId, "accepted", key);
        await Retire(key);

        Assert.True(Assert.Single(await Triggers(eventId))
            .GetProperty("templateMissing").GetBoolean());

        Assert.Equal(HttpStatusCode.OK, (await Decide(organizer, applicationId, "accepted")).StatusCode);
        Assert.Equal("accepted", await StatusOf(applicationId));
        Assert.Equal(0, await MessagesTo(email));
    }

    // ----------------------------------------------------------------- setup ---

    /// <summary>
    /// The API in-process, pointed at the test database.
    /// </summary>
    /// <remarks>
    /// Takes an optional service override so one test can replace a dependency
    /// on the fire path with one that fails. Registering it after the app's own
    /// registrations is what makes the replacement win, which is the documented
    /// behaviour of the last registration for a service type.
    /// </remarks>
    private WebApplicationFactory<Program> Factory(Action<IServiceCollection>? services = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Postgres", db.ConnectionString);

            if (services is not null)
            {
                builder.ConfigureServices(services);
            }
        });

    /// <summary>
    /// A resolver that cannot answer, standing in for a bad afternoon.
    /// </summary>
    /// <remarks>
    /// Only <see cref="MemberOfAsync"/> fails. <see cref="ResolveAsync"/>
    /// delegates to the real one so that nothing else in the app changes
    /// behaviour under this factory — a double that broke campaigns as well
    /// would make the test pass for a reason it is not about.
    /// </remarks>
    private sealed class FailingSegmentResolver(NpgsqlDataSource dataSource) : ISegmentResolver
    {
        private readonly PostgresSegmentResolver _real = new(dataSource);

        public Task<ResolvedSegment> ResolveAsync(
            Segment segment, CancellationToken ct = default) =>
            _real.ResolveAsync(segment, ct);

        public Task<SegmentMember?> MemberOfAsync(
            Guid applicationId, CancellationToken ct = default) =>
            throw new InvalidOperationException("No.");
    }

    private HttpClient Client(WebApplicationFactory<Program>? app = null) =>
        (app ?? _app).CreateClient(
            new WebApplicationFactoryClientOptions { HandleCookies = false });

    /// <summary>
    /// Somebody who can both decide an application and bind an automation.
    /// </summary>
    /// <remarks>
    /// Through the seeded baseline rather than hand-written grants, so these
    /// tests fail if the migration that puts <c>email.manage_templates</c> and
    /// <c>applications.decide</c> on a team is ever changed.
    /// <para>
    /// One person holding both rather than two, because every test here needs
    /// both and the split between them is
    /// <see cref="PermissionEnforcementTests"/>' subject rather than this
    /// file's.
    /// </para>
    /// </remarks>
    private async Task<string> SuperAdmin()
    {
        var id = await db.AddPersonAsync(Unique("organizer"));
        await db.AddToTeamAsync(id, "super-admin");

        using var scope = _app.Services.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<SessionService>();
        return $"mh_session={await sessions.StartAsync(id)}";
    }

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}@example.com";

    private Task<HttpResponseMessage> Bind(
        string cookie,
        Guid eventId,
        string status,
        string templateKey,
        WebApplicationFactory<Program>? app = null) =>
        Send(app ?? _app, HttpMethod.Put, "/admin/email-triggers", cookie, new
        {
            eventId,
            occasion = "status_reached",
            status,
            templateKey,
        });

    private Task<HttpResponseMessage> Decide(
        string cookie, Guid applicationId, string status) =>
        Send(_app, HttpMethod.Post, $"/admin/applicants/{applicationId}/status", cookie,
            new { status });

    private Task<HttpResponseMessage> Send(
        WebApplicationFactory<Program> app,
        HttpMethod method,
        string path,
        string cookie,
        object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("Cookie", cookie);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return Client(app).SendAsync(request);
    }

    private static async Task<JsonElement> Body(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private async Task<List<JsonElement>> Triggers(Guid eventId) =>
        [.. (await Body(await Send(
                _app, HttpMethod.Get, $"/admin/email-triggers?eventId={eventId}", await SuperAdmin())))
            .GetProperty("triggers")
            .EnumerateArray()];

    // ------------------------------------------------------------- fixtures ---

    /// <summary>
    /// A transactional template with deliberately meaningless copy.
    /// </summary>
    /// <remarks>
    /// Not cached between tests: these differ in exactly the placeholders they
    /// ask for and in which form they are about, which is the thing being
    /// tested.
    /// </remarks>
    private async Task<string> Template(
        string subject,
        string? html = null,
        string? text = null,
        string kind = "transactional",
        Guid? formId = null)
    {
        var key = $"test-trigger-{Guid.NewGuid():N}";
        await using var cmd = db.DataSource.CreateCommand("""
            INSERT INTO notify.templates
                (key, kind, subject, body_html, body_text, from_local, from_domain, form_id)
            VALUES (@key, @kind, @subject, @html, @text, 'mail', 'mail.example.invalid', @formId)
            """);
        cmd.Parameters.AddWithValue("key", key);
        cmd.Parameters.AddWithValue("kind", kind);
        cmd.Parameters.AddWithValue("subject", subject);
        cmd.Parameters.AddWithValue("html", html ?? $"<p>{subject}</p>");
        cmd.Parameters.AddWithValue("text", text ?? subject);
        cmd.Parameters.AddWithValue("formId", (object?)formId ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();

        return key;
    }

    /// <summary>Retires a template the way an editor's delete does.</summary>
    /// <remarks>
    /// <c>superseded_at</c> rather than a DELETE, because the foreign key from
    /// <c>notify.campaigns</c> would refuse one — 0017 says so — and because
    /// what a trigger sees in either case is the same thing: no live row for
    /// that key.
    /// </remarks>
    private async Task Retire(string key)
    {
        await using var cmd = db.DataSource.CreateCommand(
            "UPDATE notify.templates SET superseded_at = now() WHERE key = @key");
        cmd.Parameters.AddWithValue("key", key);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// An application complete enough for the schema to accept a real status,
    /// on an event of its own.
    /// </summary>
    /// <remarks>
    /// Every column here is one <c>submitted_applications_are_complete</c>
    /// requires. Filling them is not ceremony: a decision made against a
    /// half-written row would be testing a shape the database does not allow
    /// to exist, and it is also the reason a decision email's
    /// <c>{{firstName}}</c> can be relied on.
    /// </remarks>
    private async Task<(Guid EventId, Guid ApplicationId, string Email)> Applicant(
        string prefix, string status)
    {
        var eventId = await db.AddEventAsync();
        var email = Unique(prefix);

        await using var cmd = db.DataSource.CreateCommand("""
            INSERT INTO applications.applications
                (event_id, email, status, first_name, last_name, age, phone, school,
                 level_of_study, country, mlh_coc_agreed_at, mlh_data_sharing_at,
                 submitted_at)
            VALUES (@eventId, @email, @status, 'Ada', 'Lovelace', 20, '+15550000000',
                    'Morgan State University', 'undergraduate', 'United States',
                    now(), now(), now())
            RETURNING id
            """);
        cmd.Parameters.AddWithValue("eventId", eventId);
        cmd.Parameters.AddWithValue("email", email);
        cmd.Parameters.AddWithValue("status", status);

        return (eventId, (Guid)(await cmd.ExecuteScalarAsync())!, email);
    }

    /// <summary>A complete, valid set of answers to the seeded questions.</summary>
    /// <remarks>
    /// The same shape <see cref="FormSubmissionTests"/> uses, because the
    /// questions a fresh application form is created with are the ones MLH
    /// requires and a submission missing any of them is refused by the
    /// completeness check rather than by the endpoint.
    /// </remarks>
    private static Dictionary<string, JsonElement> Answers(string email) =>
        new Dictionary<string, object?>
        {
            ["email"] = email,
            ["first_name"] = "Ada",
            ["last_name"] = "Lovelace",
            ["age"] = 20,
            ["phone"] = "+1 555 0100",
            ["school"] = "Morgan State University",
            ["country"] = "United States",
            ["level_of_study"] = "undergraduate-3y",
            ["mlh_coc_agreed_at"] = true,
            ["mlh_data_sharing_at"] = true,
        }.ToDictionary(
            answer => answer.Key, answer => JsonSerializer.SerializeToElement(answer.Value));

    private async Task Rename(Guid eventId, string name)
    {
        await using var cmd = db.DataSource.CreateCommand(
            "UPDATE applications.events SET name = @name WHERE id = @id");
        cmd.Parameters.AddWithValue("id", eventId);
        cmd.Parameters.AddWithValue("name", name);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task SaveValue(string name, string value)
    {
        await using var cmd = db.DataSource.CreateCommand("""
            INSERT INTO notify.saved_values (name, value) VALUES (@name, @value)
            ON CONFLICT (name) DO UPDATE SET value = excluded.value
            """);
        cmd.Parameters.AddWithValue("name", name);
        cmd.Parameters.AddWithValue("value", value);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task Suppress(string email, string reason)
    {
        await using var cmd = db.DataSource.CreateCommand("""
            INSERT INTO notify.suppressions (email, reason) VALUES (@email, @reason)
            ON CONFLICT (email) DO UPDATE SET reason = excluded.reason
            """);
        cmd.Parameters.Add(new NpgsqlParameter("email", NpgsqlDbType.Text) { Value = email });
        cmd.Parameters.AddWithValue("reason", reason);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<string?> StatusOf(Guid applicationId)
    {
        await using var cmd = db.DataSource.CreateCommand(
            "SELECT status FROM applications.applications WHERE id = @id");
        cmd.Parameters.AddWithValue("id", applicationId);
        return await cmd.ExecuteScalarAsync() as string;
    }

    /// <summary>
    /// How many messages have been queued to one address.
    /// </summary>
    /// <remarks>
    /// By recipient rather than by campaign, because a transactional send
    /// writes a campaign of its own per message — <see cref="MessageQueue"/>
    /// explains why — so counting campaigns would be counting the same thing
    /// twice. Every test here uses a unique address, which is what makes this
    /// a count of its own sends rather than of the table.
    /// </remarks>
    private async Task<int> MessagesTo(string email)
    {
        await using var cmd = db.DataSource.CreateCommand(
            "SELECT count(*) FROM notify.messages WHERE to_email = @email::citext");
        cmd.Parameters.Add(new NpgsqlParameter("email", NpgsqlDbType.Text) { Value = email });
        return (int)(long)(await cmd.ExecuteScalarAsync())!;
    }

    /// <summary>What the ledger says has already been sent for one application.</summary>
    private async Task<int> LedgerRowsFor(Guid applicationId)
    {
        await using var cmd = db.DataSource.CreateCommand(
            "SELECT count(*) FROM notify.email_trigger_sends WHERE application_id = @id");
        cmd.Parameters.AddWithValue("id", applicationId);
        return (int)(long)(await cmd.ExecuteScalarAsync())!;
    }

    /// <summary>What the send actually froze onto one person's row.</summary>
    private async Task<(string Subject, string Html, string Text)> RenderedTo(string email)
    {
        await using var cmd = db.DataSource.CreateCommand("""
            SELECT rendered_subject, rendered_body_html, rendered_body_text
              FROM notify.messages
             WHERE to_email = @email::citext
            """);
        cmd.Parameters.Add(new NpgsqlParameter("email", NpgsqlDbType.Text) { Value = email });

        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetString(0), reader.GetString(1), reader.GetString(2));
    }
}
