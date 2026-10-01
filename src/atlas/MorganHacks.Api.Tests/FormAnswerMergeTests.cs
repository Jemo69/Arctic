using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MorganHacks.Applications.Forms;
using MorganHacks.Applications.Segments;
using MorganHacks.Identity.Services;
using Npgsql;
using NpgsqlTypes;

namespace MorganHacks.Api.Tests;

/// <summary>
/// Mailing somebody what they answered, against a real database.
/// </summary>
/// <remarks>
/// Every claim here is a claim about two different write paths and the two
/// tables they write to, so none of it means anything without Postgres. The
/// answers go in through the real stores rather than through hand-built
/// INSERTs, for the reason <see cref="AnswerSegmentTests"/> gives: what a
/// stored answer looks like is decided by <see cref="PostgresSubmissionStore"/>
/// and <see cref="PostgresRespondentStore"/>, and a test that invented the
/// encoding would be testing itself. That matters more here than it does
/// there — the shape of the stored value is what decides how it reads, and a
/// number posted as <c>"4"</c> is the ordinary case rather than the awkward
/// one.
/// <para>
/// The assertions are mostly on <c>notify.messages</c> rather than on the
/// preview, because the frozen row is the thing a person actually receives.
/// A preview that agreed with a render that disagreed with the queue would be
/// the worst of the three to debug.
/// </para>
/// <para>
/// The templates are inserted here and their copy is deliberate nonsense, like
/// every other template in these tests: wording belongs to the people who send
/// the mail, and a plausible body in a test is a body somebody eventually
/// sends.
/// </para>
/// </remarks>
public class FormAnswerMergeTests(ApplicationsDatabase db)
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

    // ------------------------------------------------------------ the send ---

    [Fact]
    public async Task An_answer_reaches_the_message_the_person_who_gave_it_receives()
    {
        // The feature, end to end and through the queue: two people answered
        // the same question differently, and each is told what they said rather
        // than both being told the same thing. Asserted per address, because a
        // merge that filled everybody's blank with the first answer it found
        // would pass any assertion made on one message.
        var (_, author) = await Comms();
        var (_, approver) = await Comms();
        var eventId = await db.AddEventAsync();
        var form = await SurveyAsync(eventId, Question("track", FieldType.Radio, "hardware", "software"));

        var hardware = await AnsweredAsync(eventId, form, ("track", "\"hardware\""));
        var software = await AnsweredAsync(eventId, form, ("track", "\"software\""));

        var id = await DraftAsync(author, eventId, await TemplateFor(
            form, "Your track", "<p>You chose {{form.answer.track}}.</p>",
            "You chose {{form.answer.track}}."));

        Assert.Equal(HttpStatusCode.OK, (await Send(id, approver)).StatusCode);

        Assert.Contains("You chose hardware.", (await RenderedIn(id, hardware)).Text);
        Assert.Contains("You chose software.", (await RenderedIn(id, software)).Text);
    }

    [Fact]
    public async Task A_multi_select_reaches_the_body_as_a_sentence()
    {
        // The stored value is a jsonb array and the body is a sentence, so this
        // is the one shape where the encoding is visible if nothing converts
        // it. ["hardware","design"] in an email is us showing the reader our
        // schema.
        var (_, author) = await Comms();
        var (_, approver) = await Comms();
        var eventId = await db.AddEventAsync();
        var form = await SurveyAsync(
            eventId, Question("track", FieldType.Checkboxes, "hardware", "design", "games"));

        var email = await AnsweredAsync(
            eventId, form, ("track", """["hardware","design","games"]"""));

        var id = await DraftAsync(author, eventId, await TemplateFor(
            form, "Your tracks", "<p>You picked {{form.answer.track}}.</p>",
            "You picked {{form.answer.track}}."));

        Assert.Equal(HttpStatusCode.OK, (await Send(id, approver)).StatusCode);

        Assert.Contains(
            "You picked hardware, design and games.", (await RenderedIn(id, email)).Text);
    }

    [Fact]
    public async Task A_number_and_a_tick_read_the_way_the_applicant_columns_do()
    {
        // The browser posts a number as "4" and a tick as true, so what is in
        // the jsonb is not what the question's declared type says. Both have to
        // come out the way the equivalent applicant column does — "yes", and a
        // year with no thousands separator — because an organizer merging a
        // tick on a form and a boolean column in the same email must not get
        // two different words for it.
        var (_, author) = await Comms();
        var (_, approver) = await Comms();
        var eventId = await db.AddEventAsync();
        var form = await SurveyAsync(
            eventId,
            Question("hackathons", FieldType.Number),
            Question("returning", FieldType.Consent));

        var email = await AnsweredAsync(
            eventId, form, ("hackathons", "\"4\""), ("returning", "true"));

        var id = await DraftAsync(author, eventId, await TemplateFor(
            form, "About you",
            "<p>{{form.answer.hackathons}} and {{form.answer.returning}}.</p>",
            "{{form.answer.hackathons}} and {{form.answer.returning}}."));

        Assert.Equal(HttpStatusCode.OK, (await Send(id, approver)).StatusCode);

        Assert.Contains("4 and yes.", (await RenderedIn(id, email)).Text);
    }

    [Fact]
    public async Task An_application_forms_answers_come_off_the_application_itself()
    {
        // The other table, and the reason this lookup lives in the resolver: an
        // application form writes its answers to
        // applications.applications.responses and every other kind writes
        // applications.form_submissions.answers. A merge that knew only one of
        // them would fill nothing in for half the forms in the table, with
        // every check green.
        var (_, author) = await Comms();
        var (_, approver) = await Comms();
        var eventId = await db.AddEventAsync();
        var (form, version) = await ApplicationFormAsync(
            eventId, Question("track", FieldType.Radio, "hardware", "software"));

        var email = Unique("applied");
        await new PostgresSubmissionStore(db.DataSource).SubmitApplicationAsync(
            form, version, Answers(email, ("track", "hardware")));

        var id = await DraftAsync(author, eventId, await TemplateFor(
            form, "Your track", "<p>You chose {{form.answer.track}}.</p>",
            "You chose {{form.answer.track}}."), "submitted");

        Assert.Equal(HttpStatusCode.OK, (await Send(id, approver)).StatusCode);

        Assert.Contains("You chose hardware.", (await RenderedIn(id, email)).Text);
    }

    // ----------------------------------------------------------- the gaps ---

    [Fact]
    public async Task Somebody_who_never_answered_is_counted_and_the_send_refused()
    {
        // The case the whole coverage check exists for, reached through a new
        // door. Somebody who never opened the survey has no answer, so the
        // blank stays standing — and a broadcast that would reach them as "You
        // chose {{form.answer.track}}." is refused rather than sent. Not
        // substituted empty, which would leave a sentence with a hole in it
        // that nobody notices until it has left.
        var (_, author) = await Comms();
        var (_, approver) = await Comms();
        var eventId = await db.AddEventAsync();
        var form = await SurveyAsync(eventId, Question("track", FieldType.Radio, "hardware"));

        var answered = await AnsweredAsync(eventId, form, ("track", "\"hardware\""));

        // An applicant who never opened the survey, so there is no row keyed
        // on them in form_submissions at all.
        var silent = Unique("silent");
        await ApplicantAsync(eventId, silent);

        var id = await DraftAsync(author, eventId, await TemplateFor(
            form, "Your track", "<p>You chose {{form.answer.track}}.</p>",
            "You chose {{form.answer.track}}."));

        var preview = await Body(await Preview(id, author));

        // One of the two, named, so somebody can go and open the row.
        var coverage = CoverageOf(preview, "form.answer.track");
        Assert.Equal(1, coverage.GetProperty("missing").GetInt32());
        Assert.Equal(2, coverage.GetProperty("total").GetInt32());
        Assert.Equal(
            [silent],
            coverage.GetProperty("examples").EnumerateArray().Select(e => e.GetString()));

        // The one with the hole in it is the message the preview puts first,
        // because that is the one worth reading.
        Assert.Contains(
            "{{form.answer.track}}",
            preview.GetProperty("renders")[0].GetProperty("text").GetString()!);

        Assert.Equal(HttpStatusCode.BadRequest, (await Send(id, approver)).StatusCode);
        Assert.Equal(0, await MessageCount(id));

        // And the person who did answer is not the reason it was refused.
        Assert.DoesNotContain(answered, coverage.GetProperty("examples").ToString());
    }

    [Fact]
    public async Task A_form_placeholder_that_resolves_no_longer_blocks_the_send()
    {
        // Unfilled dropped the form on its way to Values, so every {{form.*}}
        // placeholder was a gap for the whole segment and a template using the
        // form's own link could never be sent — with the refusal naming a
        // number of recipients rather than the bug. Asserted here rather than
        // only in the unit test because this is the path that was broken: the
        // coverage check, over a real segment, through the campaign screen.
        var (_, author) = await Comms();
        var (_, approver) = await Comms();
        var eventId = await db.AddEventAsync();
        var form = await SurveyAsync(eventId, Question("track", FieldType.Radio, "hardware"));
        var email = await AnsweredAsync(eventId, form, ("track", "\"hardware\""));

        var id = await DraftAsync(author, eventId, await TemplateFor(
            form, "Fill it in", "<p>Open {{form.link}} to answer.</p>",
            "Open {{form.link}} to answer."));

        var preview = await Body(await Preview(id, author));
        Assert.Equal(0, CoverageOf(preview, "form.link").GetProperty("missing").GetInt32());
        Assert.Empty(preview.GetProperty("problems").EnumerateArray());

        Assert.Equal(HttpStatusCode.OK, (await Send(id, approver)).StatusCode);
        Assert.Contains($"/{form.Code}", (await RenderedIn(id, email)).Text);
    }

    // -------------------------------------------------------- the lookup ---

    [Fact]
    public async Task The_whole_send_resolves_its_answers_in_one_lookup()
    {
        // The requirement that cannot be read off the code once somebody has
        // moved the call: a campaign is several hundred recipients, and the
        // obvious place to resolve an answer is inside the render loop. That
        // version reads perfectly and is four hundred round trips behind one
        // button. Counted through a decorator rather than asserted on timing,
        // because a slow test is a test somebody deletes.
        var calls = 0;

        using var app = Factory(services => services.AddSingleton<ISegmentResolver>(
            new CountingResolver(new PostgresSegmentResolver(db.DataSource), () => calls++)));

        var (_, author) = await Comms(app);
        var (_, approver) = await Comms(app);
        var eventId = await db.AddEventAsync();
        var form = await SurveyAsync(eventId, Question("track", FieldType.Radio, "hardware"));

        foreach (var _ in Enumerable.Range(0, 3))
        {
            await AnsweredAsync(eventId, form, ("track", "\"hardware\""));
        }

        var id = await DraftAsync(author, eventId, await TemplateFor(
            form, "Your track", "<p>You chose {{form.answer.track}}.</p>",
            "You chose {{form.answer.track}}."), app: app);

        calls = 0;
        Assert.Equal(HttpStatusCode.OK, (await Send(id, approver, app)).StatusCode);

        // One, for three recipients. The number that matters is that it does
        // not move when the third recipient is added.
        Assert.Equal(1, calls);
        Assert.Equal(3, await MessageCount(id));
    }

    /// <summary>
    /// The real resolver, counting how often the answers are looked up.
    /// </summary>
    /// <remarks>
    /// A decorator rather than a stub, so the send under test is the real one
    /// end to end and the only thing this adds is the count. A stub would make
    /// the test agree with itself about what the lookup returns, which is the
    /// half that is already covered next door.
    /// </remarks>
    private sealed class CountingResolver(ISegmentResolver inner, Action counted) : ISegmentResolver
    {
        public Task<ResolvedSegment> ResolveAsync(
            Segment segment, CancellationToken ct = default) =>
            inner.ResolveAsync(segment, ct);

        public Task<FormAnswers> AnswersToAsync(
            Form form,
            IReadOnlyList<AnswerQuestion> questions,
            IReadOnlyList<SegmentMember> members,
            CancellationToken ct = default)
        {
            counted();
            return inner.AnswersToAsync(form, questions, members, ct);
        }

        // Passed through uncounted. These tests count answer lookups; a
        // triggered send's lookup of its own recipient is a different question
        // and counting it here would make the assertion mean two things.
        public Task<SegmentMember?> MemberOfAsync(
            Guid applicationId, CancellationToken ct = default) =>
            inner.MemberOfAsync(applicationId, ct);
    }

    // --------------------------------------------------------- the editor ---

    [Fact]
    public async Task The_editor_is_offered_the_bound_forms_questions()
    {
        // What the console's placeholder menu reads, through the endpoint it
        // reads it from. The names have to arrive with a heading and a sentence
        // like every other placeholder — the menu lays a row out around them,
        // and a name with nothing beside it is one somebody has to guess at.
        var (_, cookie) = await Comms();
        var eventId = await db.AddEventAsync();
        var form = await SurveyAsync(
            eventId,
            Question("track", FieldType.Radio, "hardware") with { Label = "Which track?" },
            Question("cv", FieldType.File));

        var offered = await PlaceholdersFor(form.Id, cookie);

        var track = Assert.Single(offered, p => p.GetProperty("name").GetString() == "form.answer.track");
        Assert.Equal("Which track?", track.GetProperty("description").GetString());
        Assert.False(string.IsNullOrWhiteSpace(track.GetProperty("group").GetString()));

        // The upload is not offered, because what is stored is where the file
        // went. An upload id in a body is applications.resume_key leaving the
        // schema by another door.
        Assert.DoesNotContain(
            offered.Select(p => p.GetProperty("name").GetString()), name => name == "form.answer.cv");

        // And nothing is offered at all without a form named, which is the
        // narrowing the form group already had.
        Assert.DoesNotContain(
            (await PlaceholdersFor(null, cookie)).Select(p => p.GetProperty("name").GetString()),
            name => name!.StartsWith("form.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_campaign_offers_the_answers_its_own_template_can_fill()
    {
        // The second list, narrowed by the segment as well as by the form. Both
        // come off MergeFields and both have to agree with the send, because a
        // name offered on either screen is a template that gets written,
        // reviewed, and then refused.
        var (_, cookie) = await Comms();
        var eventId = await db.AddEventAsync();
        await ApplicantAsync(eventId, Unique("offered"));
        var form = await SurveyAsync(eventId, Question("track", FieldType.Radio, "hardware"));

        var bound = await DraftAsync(cookie, eventId, await TemplateFor(
            form, "placeholder", "<p>placeholder</p>", "placeholder"));

        var unbound = await DraftAsync(cookie, eventId, await TemplateFor(
            null, "placeholder", "<p>placeholder</p>", "placeholder"));

        Assert.Contains("form.answer.track", await PlaceholdersOn(bound, cookie));
        Assert.DoesNotContain("form.answer.track", await PlaceholdersOn(unbound, cookie));
    }

    [Fact]
    public async Task A_typed_list_of_addresses_is_offered_no_answers()
    {
        // These recipients are sponsors and mentors this system has never heard
        // of, so there is no answer of theirs to echo — and a name offered here
        // is a template somebody writes and the send refuses. The form's own
        // link is still offered, because it does not depend on who receives it.
        var (_, cookie) = await Comms();
        var eventId = await db.AddEventAsync();
        var form = await SurveyAsync(eventId, Question("track", FieldType.Radio, "hardware"));

        var key = await TemplateFor(form, "placeholder", "<p>placeholder</p>", "placeholder");

        var addresses = await DraftAsync(
            cookie, new { type = "explicitList", emails = new[] { Unique("sponsor") } }, key);

        var offered = await PlaceholdersOn(addresses, cookie);

        Assert.DoesNotContain("form.answer.track", offered);
        Assert.Contains("form.link", offered);
    }

    [Fact]
    public async Task A_template_asking_for_an_answer_its_form_does_not_hold_is_refused_at_draft()
    {
        // The line under the menu, for the template somebody typed a name into
        // by hand — or whose form was changed underneath it. Refused while the
        // person who can still fix it is looking at the screen, rather than on
        // Thursday by the approver who could not send it.
        var (_, cookie) = await Comms();
        var eventId = await db.AddEventAsync();
        await ApplicantAsync(eventId, Unique("unfillable"));
        var form = await SurveyAsync(eventId, Question("track", FieldType.Radio, "hardware"));

        var key = await TemplateFor(
            form, "Placeholder", "<p>You chose {{form.answer.nothing}}.</p>",
            "You chose {{form.answer.nothing}}.");

        var refused = await Client().SendAsync(Request(HttpMethod.Post, "/admin/campaigns", cookie, new
        {
            name = "People who answered",
            templateKey = key,
            segment = InStatus(eventId, "accepted"),
        }));

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

        // Named, because the useful thing to say is which one is wrong.
        Assert.Contains(
            "{{form.answer.nothing}}",
            (await Body(refused)).GetProperty("error").GetString()!,
            StringComparison.Ordinal);
    }

    // ------------------------------------------------------------ fixtures ---

    private PostgresFormStore Forms => new(db.DataSource);

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}@example.com";

    private static object InStatus(Guid eventId, params string[] statuses) =>
        new { type = "applicationStatus", eventId, statuses };

    private static FormField Question(
        string key, FieldType type, params string[] options) => new()
        {
            Key = key,
            Type = type,
            Label = $"Question {key}",
            Options = [.. options.Select(option => new FieldOption(option, option))],
        };

    /// <summary>A published survey on one event.</summary>
    private async Task<Form> SurveyAsync(Guid eventId, params FormField[] questions)
    {
        var form = await Forms.CreateAsync(eventId, "Interest survey", "survey", null);
        await Forms.DraftAsync(form.Id, null);
        await Forms.SaveDraftAsync(form.Id, questions);
        await Forms.PublishAsync(form.Id, null);
        return form;
    }

    /// <summary>
    /// A published application form, with MLH's questions and some of ours.
    /// </summary>
    /// <remarks>
    /// Started from the draft the store seeds rather than from a hand-written
    /// list, because an application form that does not ask for an address
    /// cannot create an applicant at all and the seeded questions are what make
    /// it one. They are also exactly the promoted ones, which is why this form
    /// offers one answer placeholder and not fifteen.
    /// </remarks>
    private async Task<(Form Form, FormVersion Version)> ApplicationFormAsync(
        Guid eventId, params FormField[] extra)
    {
        var form = await Forms.CreateAsync(eventId, "Application", "application", null);
        var draft = await Forms.DraftAsync(form.Id, null);
        await Forms.SaveDraftAsync(form.Id, [.. draft.Fields, .. extra]);
        return (form, await Forms.PublishAsync(form.Id, null));
    }

    /// <summary>A complete set of answers to the seeded application form.</summary>
    private static Dictionary<string, JsonElement> Answers(
        string email, params (string Key, object Value)[] extra)
    {
        var answers = new Dictionary<string, object>(StringComparer.Ordinal)
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
        };

        foreach (var (key, value) in extra)
        {
            answers[key] = value;
        }

        return answers.ToDictionary(
            answer => answer.Key,
            answer => JsonSerializer.SerializeToElement(answer.Value),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// An applicant who signed in and answered the survey. Returns their
    /// address.
    /// </summary>
    /// <remarks>
    /// Three rows, because that is the real shape: a person, their application
    /// on the form's event, and their answer filed against the person. The
    /// lookup walks all three — it has only the address and has to reach a row
    /// keyed on a person id — and a fixture that skipped the middle one would be
    /// testing a path that does not exist.
    /// </remarks>
    private async Task<string> AnsweredAsync(
        Guid eventId, Form form, params (string Key, string Json)[] answers)
    {
        var email = Unique("answered");
        var personId = await db.AddPersonAsync(email);
        var applicationId = await ApplicantAsync(eventId, email, personId);
        var published = (await Forms.PublishedAsync(form.Id))!;

        await new PostgresRespondentStore(db.DataSource).RecordAsync(
            form.Id,
            published.Version,
            new Respondent(
                personId, applicationId, "unused@example.invalid",
                null, null, "accepted", true, true,
                new Dictionary<string, JsonElement>(StringComparer.Ordinal)),

            // Written as JSON text rather than as C# values, because the cases
            // that matter are a number posted as "4" and a tick posted as true
            // — and writing them as objects would let the serializer decide the
            // thing under test.
            answers.ToDictionary(
                answer => answer.Key,
                answer => JsonDocument.Parse(answer.Json).RootElement.Clone(),
                StringComparer.Ordinal));

        return email;
    }

    /// <summary>
    /// An application complete enough for the schema to accept a real status.
    /// </summary>
    /// <remarks>
    /// Every column here is one the <c>submitted_applications_are_complete</c>
    /// check requires. <c>person_id</c> is the one that matters for a survey
    /// answer, which is filed against a person rather than against an
    /// application — so an applicant who never signed in cannot be reached
    /// through one, and is the shape the gap test uses.
    /// </remarks>
    private async Task<Guid> ApplicantAsync(
        Guid eventId, string email, Guid? personId = null)
    {
        await using var cmd = db.DataSource.CreateCommand("""
            INSERT INTO applications.applications
                (event_id, person_id, email, status, first_name, last_name, age, phone,
                 school, level_of_study, country, mlh_coc_agreed_at, mlh_data_sharing_at,
                 submitted_at)
            VALUES (@eventId, @personId, @email, 'accepted', 'Ada', 'Lovelace', 20,
                    '+15550000000', 'Morgan State University', 'undergraduate',
                    'United States', now(), now(), now())
            RETURNING id
            """);
        cmd.Parameters.AddWithValue("eventId", eventId);
        cmd.Parameters.AddWithValue("personId", (object?)personId ?? DBNull.Value);
        cmd.Parameters.Add(new NpgsqlParameter("email", NpgsqlDbType.Text) { Value = email });

        return (Guid)(await cmd.ExecuteScalarAsync())!;
    }

    /// <summary>
    /// A broadcast template bound to a form, with deliberately meaningless copy.
    /// </summary>
    /// <remarks>
    /// <c>form_id</c> is the column 0047 added and the whole reason any of this
    /// resolves: it is what says which form's questions a template may echo.
    /// Null for the one test that needs a template about no form.
    /// </remarks>
    private async Task<string> TemplateFor(
        Form? form, string subject, string html, string text)
    {
        var key = $"test-answer-merge-{Guid.NewGuid():N}";
        await using var cmd = db.DataSource.CreateCommand("""
            INSERT INTO notify.templates
                (key, kind, subject, body_html, body_text, from_local, from_domain, form_id)
            VALUES (@key, 'broadcast', @subject, @html, @text, 'news',
                    'news.example.invalid', @formId)
            """);
        cmd.Parameters.AddWithValue("key", key);
        cmd.Parameters.AddWithValue("subject", subject);
        cmd.Parameters.AddWithValue("html", html);
        cmd.Parameters.AddWithValue("text", text);
        cmd.Parameters.AddWithValue("formId", (object?)form?.Id ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();

        return key;
    }

    /// <summary>What the send actually froze onto one person's row.</summary>
    private async Task<(string Subject, string Html, string Text)> RenderedIn(
        Guid campaignId, string email)
    {
        await using var cmd = db.DataSource.CreateCommand("""
            SELECT rendered_subject, rendered_body_html, rendered_body_text
              FROM notify.messages
             WHERE campaign_id = @id AND to_email = @email
            """);
        cmd.Parameters.AddWithValue("id", campaignId);
        cmd.Parameters.Add(new NpgsqlParameter("email", NpgsqlDbType.Text) { Value = email });

        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetString(0), reader.GetString(1), reader.GetString(2));
    }

    /// <summary>One placeholder's row out of a preview's coverage.</summary>
    private static JsonElement CoverageOf(JsonElement preview, string placeholder) =>
        preview.GetProperty("placeholderCoverage")
               .EnumerateArray()
               .Single(row => row.GetProperty("placeholder").GetString() == placeholder);

    private async Task<int> MessageCount(Guid campaignId)
    {
        await using var cmd = db.DataSource.CreateCommand(
            "SELECT count(*) FROM notify.messages WHERE campaign_id = @id");
        cmd.Parameters.AddWithValue("id", campaignId);
        return (int)(long)(await cmd.ExecuteScalarAsync())!;
    }

    /// <summary>Every name the template editor is offered for one form.</summary>
    private async Task<IReadOnlyList<JsonElement>> PlaceholdersFor(Guid? formId, string cookie)
    {
        var path = formId is { } id
            ? $"/admin/templates/placeholders?form={id}"
            : "/admin/templates/placeholders";

        return [.. (await Body(await Client().SendAsync(Request(HttpMethod.Get, path, cookie))))
            .GetProperty("placeholders").EnumerateArray()];
    }

    /// <summary>Every name one campaign's own list offers.</summary>
    private async Task<IReadOnlyList<string?>> PlaceholdersOn(Guid id, string cookie) =>
        [.. (await Body(await Client().SendAsync(
                Request(HttpMethod.Get, $"/admin/campaigns/{id}/placeholders", cookie))))
            .GetProperty("placeholders").EnumerateArray()
            .Select(p => p.GetProperty("name").GetString())];

    /// <summary>A draft campaign aimed at one event's accepted applicants.</summary>
    private Task<Guid> DraftAsync(
        string cookie,
        Guid eventId,
        string templateKey,
        string status = "accepted",
        WebApplicationFactory<Program>? app = null) =>
        DraftAsync(cookie, InStatus(eventId, status), templateKey, app);

    private async Task<Guid> DraftAsync(
        string cookie,
        object segment,
        string templateKey,
        WebApplicationFactory<Program>? app = null)
    {
        var created = await Client(app).SendAsync(Request(HttpMethod.Post, "/admin/campaigns", cookie, new
        {
            name = "People who answered",
            templateKey,
            segment,
        }));

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return (await Body(created)).GetProperty("id").GetGuid();
    }

    private Task<HttpResponseMessage> Preview(Guid id, string cookie) =>
        Client().SendAsync(Request(HttpMethod.Post, $"/admin/campaigns/{id}/preview", cookie));

    private Task<HttpResponseMessage> Send(
        Guid id, string cookie, WebApplicationFactory<Program>? app = null) =>
        Client(app).SendAsync(Request(HttpMethod.Post, $"/admin/campaigns/{id}/send", cookie));

    /// <summary>An organizer on comms, which is the team that sends broadcasts.</summary>
    private async Task<(Guid Person, string Cookie)> Comms(
        WebApplicationFactory<Program>? app = null)
    {
        var id = await db.AddPersonAsync(Unique("comms"));
        await db.AddToTeamAsync(id, "comms");

        using var scope = (app ?? _app).Services.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<SessionService>();
        return (id, $"mh_session={await sessions.StartAsync(id)}");
    }

    /// <summary>
    /// The API over the test database, optionally with a service swapped.
    /// </summary>
    /// <remarks>
    /// Parameterised so one test can wrap the resolver and count its calls.
    /// Every other test takes the plain one, because a host per test is several
    /// seconds each and the thing being measured is usually a row.
    /// </remarks>
    private WebApplicationFactory<Program> Factory(Action<IServiceCollection>? services = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:Postgres", db.ConnectionString);
            if (services is not null)
            {
                b.ConfigureServices(services);
            }
        });

    private HttpClient Client(WebApplicationFactory<Program>? app = null) =>
        (app ?? _app).CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    private static HttpRequestMessage Request(
        HttpMethod method, string path, string cookie, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("Cookie", cookie);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }

    private static async Task<JsonElement> Body(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
}
