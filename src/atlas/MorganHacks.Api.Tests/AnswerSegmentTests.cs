using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MorganHacks.Applications.Domain;
using MorganHacks.Applications.Forms;
using MorganHacks.Applications.Segments;
using MorganHacks.Identity.Services;
using Npgsql;
using NpgsqlTypes;

namespace MorganHacks.Api.Tests;

/// <summary>
/// The criterion an answer segment stores, with no database behind it.
/// </summary>
/// <remarks>
/// <c>notify.campaigns.segment</c> is a jsonb document that has to survive
/// being read by something that is not <see cref="Segment"/> — a support query
/// at 2am, a later version of this class — so the document is the contract and
/// these are the tests of it. A serializer that happened to emit the right
/// shape today would pass a test that compared objects and fail the next
/// reader.
/// </remarks>
public class AnswerSegmentDocumentTests
{
    [Fact]
    public void An_answer_segment_survives_the_round_trip_through_jsonb()
    {
        // Written by ToJson, read by TryParse, because that is the actual
        // round trip: the endpoint stores what the server understood and the
        // next request parses the row back. Comparing the records at both ends
        // is what catches a property that is written and not read, which is
        // the failure that leaves a stored segment quietly meaning something
        // else.
        var formId = Guid.NewGuid();
        var original = new Segment.FormAnswer(formId, "track", "hardware");

        using var document = JsonDocument.Parse(original.ToJson());

        // Named here rather than taken from the record, so a rename of a C#
        // property cannot silently rename the stored field.
        Assert.Equal("formAnswer", document.RootElement.GetProperty("type").GetString());
        Assert.Equal(formId, document.RootElement.GetProperty("formId").GetGuid());
        Assert.Equal("track", document.RootElement.GetProperty("question").GetString());
        Assert.Equal("hardware", document.RootElement.GetProperty("value").GetString());

        Assert.True(Segment.TryParse(document.RootElement, out var parsed, out var error));
        Assert.Null(error);
        Assert.Equal(original, parsed);
    }

    [Fact]
    public void An_answer_segment_keeps_the_value_exactly_as_it_was_chosen()
    {
        // Trimmed, and nothing else. Lower-casing here would make the stored
        // segment disagree with what the organizer picked — the comparison is
        // case-insensitive in the resolver, which is a different question from
        // what the row should say happened.
        using var document = JsonDocument.Parse(
            """
            {
                "type": "formAnswer",
                "formId": "5d4b8f2a-0000-4000-8000-000000000001",
                "question": "  track  ",
                "value": "  Hardware  "
            }
            """);

        Assert.True(Segment.TryParse(document.RootElement, out var parsed, out _));

        var answer = Assert.IsType<Segment.FormAnswer>(parsed);
        Assert.Equal("track", answer.Question);
        Assert.Equal("Hardware", answer.Value);
    }

    [Theory]
    // No form, so there is nothing to look the answer up on.
    [InlineData("""{"type":"formAnswer","question":"track","value":"hardware"}""")]
    // A question and no value would mean "answered this with anything", which
    // is a much larger audience than anybody picking a value intends.
    [InlineData("""{"type":"formAnswer","formId":"5d4b8f2a-0000-4000-8000-000000000001","question":"track"}""")]
    [InlineData("""{"type":"formAnswer","formId":"5d4b8f2a-0000-4000-8000-000000000001","question":"track","value":"   "}""")]
    // A value and no question is the same gap facing the other way.
    [InlineData("""{"type":"formAnswer","formId":"5d4b8f2a-0000-4000-8000-000000000001","value":"hardware"}""")]
    // Neither is a string, so neither is an answer to anything.
    [InlineData("""{"type":"formAnswer","formId":"5d4b8f2a-0000-4000-8000-000000000001","question":7,"value":true}""")]
    public void An_incomplete_answer_segment_is_refused_with_a_sentence(string json)
    {
        // Refused rather than defaulted, and refused with something somebody
        // can act on: this is the screen where the next step mails several
        // hundred people, and "the JSON was wrong" is not a thing to say on
        // it.
        using var document = JsonDocument.Parse(json);

        Assert.False(Segment.TryParse(document.RootElement, out var parsed, out var error));
        Assert.Null(parsed);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void An_answer_longer_than_a_short_answer_could_be_is_refused()
    {
        // The bound is what SubmissionValidation caps a short answer at, so a
        // value this refuses is one no stored answer could equal. Without it
        // this is a way to put arbitrary text into a jsonb document and a
        // bound parameter from the campaign screen.
        var json = JsonSerializer.Serialize(new
        {
            type = "formAnswer",
            formId = Guid.NewGuid(),
            question = "track",
            value = new string('x', 501),
        });

        using var document = JsonDocument.Parse(json);

        Assert.False(Segment.TryParse(document.RootElement, out _, out var error));
        Assert.Contains("500", error);
    }

    [Fact]
    public void An_answer_segment_fills_the_same_merge_fields_as_any_applicant_segment()
    {
        // The point of resolving to applicants rather than to answers. These
        // recipients are people with applications, so a template greeting them
        // by name is fillable — and if it were not, every answer-segment
        // campaign would be refused at Unfillable for a template that works
        // for every other segment.
        var answered = MergeFields.Fillable(
            new Segment.FormAnswer(Guid.NewGuid(), "track", "hardware"));

        Assert.Equal(
            MergeFields.Fillable(new Segment.InStatus(Guid.NewGuid(), [ApplicationStatus.Accepted])),
            answered);

        Assert.Contains("firstName", answered);
        Assert.Contains("email", answered);
    }
}

/// <summary>
/// Which of a form's questions an audience may be chosen by.
/// </summary>
/// <remarks>
/// No database: this is a statement about a set of questions, and the questions
/// are the input. The endpoint that serves it is covered where the forms admin
/// surface is.
/// <para>
/// Every exclusion here is listed rather than omitted, and that is the thing
/// worth testing. An organizer hunting for the hardware-track question needs to
/// be told that the one they are looking at is the wrong kind; a list that
/// quietly dropped it reads as the question having been deleted, and the next
/// thing that happens is somebody republishing a form to get it back.
/// </para>
/// </remarks>
public class AnswerQuestionTests
{
    private static FormField Question(
        string key, FieldType type, params string[] options) => new()
        {
            Key = key,
            Type = type,
            Label = $"Question {key}",
            Options = [.. options.Select(option => new FieldOption(option, option.ToUpperInvariant()))],
        };

    [Fact]
    public void A_long_answer_cannot_pick_an_audience_and_says_why()
    {
        // The one exclusion here that is a decision rather than a consequence
        // of where the answer is stored. Matching part of a paragraph is a
        // search, and "everyone who mentioned hardware" catches the people who
        // said they are not interested in it.
        var listed = AnswerQuestions.On(
            isApplicationForm: false,
            [Question("why", FieldType.Paragraph), Question("track", FieldType.Radio, "hardware")]);

        var refusal = Assert.Single(listed, question => question.Key == "why").Unmatchable;
        Assert.NotNull(refusal);
        Assert.Contains("search", refusal);

        // And the usable one beside it is still usable, so the sentence above
        // is about the question rather than about the form.
        Assert.Null(Assert.Single(listed, question => question.Key == "track").Unmatchable);
    }

    [Fact]
    public void An_upload_has_no_answer_to_match()
    {
        var listed = AnswerQuestions.On(isApplicationForm: false, [Question("cv", FieldType.File)]);

        Assert.NotNull(Assert.Single(listed).Unmatchable);
    }

    [Fact]
    public void An_answer_promoted_to_a_column_is_offered_on_a_survey_and_not_on_an_application()
    {
        // Where the answer ends up is the whole difference. The application
        // form's submit path writes a promoted answer to its column and not
        // also to responses, so the segment's jsonb lookup would match
        // nobody — whereas a survey keeps every answer in
        // form_submissions.answers whatever the question says about a column.
        // A question offered on one and not the other is this file's most
        // surprising claim, so both directions are asserted together.
        var school = Question("school", FieldType.ShortText) with
        {
            Storage = AnswerStorage.Column,
            Column = "school",
        };

        Assert.NotNull(
            Assert.Single(AnswerQuestions.On(isApplicationForm: true, [school])).Unmatchable);

        Assert.Null(
            Assert.Single(AnswerQuestions.On(isApplicationForm: false, [school])).Unmatchable);
    }

    [Fact]
    public void A_page_break_is_not_listed_because_nobody_answers_one()
    {
        // Left out rather than listed with an explanation. A section is a
        // heading, nothing is ever stored under its key, and explaining the
        // form builder on the campaign screen is not what the picker is for.
        var listed = AnswerQuestions.On(
            isApplicationForm: false,
            [Question("page", FieldType.Section), Question("track", FieldType.Radio, "hardware")]);

        Assert.Equal(["track"], listed.Select(question => question.Key));
    }

    [Fact]
    public void A_choice_question_offers_the_values_the_form_declared()
    {
        // The form's options and never a scan of what people answered. The
        // second would be a list of what several hundred people wrote about
        // themselves, which is applications.view_responses — a permission
        // comms deliberately does not hold — and the declared options are what
        // somebody picking "wants a hardware track" is looking for anyway.
        var listed = AnswerQuestions.On(
            isApplicationForm: false,
            [Question("track", FieldType.Checkboxes, "hardware", "software")]);

        Assert.Equal(
            ["hardware", "software"],
            Assert.Single(listed).Values.Select(option => option.Value));
    }

    [Fact]
    public void A_short_answer_offers_nothing_to_choose_between()
    {
        // Not a gap. There is no declared set of answers to a typed question,
        // so the organizer types the value, and offering a guessed list would
        // be offering values that may match nobody.
        var listed = AnswerQuestions.On(isApplicationForm: false, [Question("city", FieldType.ShortText)]);

        var question = Assert.Single(listed);
        Assert.Null(question.Unmatchable);
        Assert.Empty(question.Values);
    }
}

/// <summary>
/// Mailing people by what they answered, against a real database.
/// </summary>
/// <remarks>
/// Every claim here is a claim about a jsonb query and about rows two different
/// write paths produced, so none of it means anything without Postgres. The
/// answers are written through the real stores rather than hand-built INSERTs
/// for exactly that reason: what a stored answer looks like is decided by
/// <see cref="PostgresSubmissionStore"/> and
/// <see cref="PostgresRespondentStore"/>, and a test that invented the
/// encoding would be testing itself.
/// <para>
/// <b>The resolver never reads the form's questions.</b> It matches the stored
/// answer, so the question's declared type decides nothing here — which is why
/// the fixtures below publish a plausible form and then stop caring about it,
/// and why which types an organizer may pick is decided in
/// <see cref="AnswerQuestions"/> and tested next door without a database.
/// </para>
/// </remarks>
public class AnswerSegmentTests(ApplicationsDatabase db)
    : IClassFixture<ApplicationsDatabase>, IAsyncLifetime
{
    private WebApplicationFactory<Program> _app = null!;

    public Task InitializeAsync()
    {
        _app = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.UseSetting("ConnectionStrings:Postgres", db.ConnectionString));
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _app.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Only_the_people_who_gave_the_chosen_answer_are_recipients()
    {
        // The whole feature in one assertion. "Everyone who filled in the
        // interest survey" is three people and "everyone who said hardware" is
        // one of them, and the second is the mail somebody wants to send.
        var (_, author) = await Comms();
        var eventId = await db.AddEventAsync();
        var form = await SurveyAsync(eventId, Question("track", FieldType.Radio, "hardware", "software"));

        var wanted = await AnsweredAsync(eventId, form, ("track", "\"hardware\""));
        await AnsweredAsync(eventId, form, ("track", "\"software\""));
        await AnsweredAsync(eventId, form, ("track", "\"software\""));

        var preview = await Body(await Preview(
            await DraftAsync(author, Answer(form.Id, "track", "hardware")), author));

        Assert.Equal(1, preview.GetProperty("recipientCount").GetInt32());
        Assert.Equal([wanted], preview.GetProperty("sample").EnumerateArray().Select(e => e.GetString()));

        // Nobody was dropped, so there is nothing for the screen to explain.
        Assert.Equal(0, preview.GetProperty("unreachableCount").GetInt32());
    }

    [Fact]
    public async Task An_anonymous_answer_matches_and_is_counted_rather_than_mailed()
    {
        // 0027 made person_id nullable so an answer with nobody attached could
        // be kept. Those rows have no person and no address, so they cannot be
        // recipients — and the count is reported rather than dropped, because
        // an organizer reading "three responses" on the form screen and "one
        // recipient" here with nothing in between has been shown a bug that is
        // not there.
        var (_, author) = await Comms();
        var eventId = await db.AddEventAsync();
        var form = await SurveyAsync(eventId, Question("track", FieldType.Radio, "hardware"));

        var signedIn = await AnsweredAsync(eventId, form, ("track", "\"hardware\""));
        await AnonymouslyAsync(form, ("track", "\"hardware\""));
        await AnonymouslyAsync(form, ("track", "\"hardware\""));

        var preview = await Body(await Preview(
            await DraftAsync(author, Answer(form.Id, "track", "hardware")), author));

        Assert.Equal(1, preview.GetProperty("recipientCount").GetInt32());
        Assert.Equal([signedIn], preview.GetProperty("sample").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(2, preview.GetProperty("unreachableCount").GetInt32());
    }

    [Fact]
    public async Task An_answer_from_somebody_who_never_applied_is_counted_the_same_way()
    {
        // The other half of the same number, and the reason it is not called
        // "anonymous". A sign-in form files its answer against a person, and a
        // person with no application on the form's event has no address this
        // segment can reach — the mergeable columns are all on the
        // application. Unreported, it is the same unexplained gap.
        var (_, author) = await Comms();
        var eventId = await db.AddEventAsync();
        var form = await SurveyAsync(eventId, Question("track", FieldType.Radio, "hardware"));

        var applied = await AnsweredAsync(eventId, form, ("track", "\"hardware\""));
        await RecordAsync(form, await db.AddPersonAsync(Unique("never-applied")), null, ("track", "\"hardware\""));

        var preview = await Body(await Preview(
            await DraftAsync(author, Answer(form.Id, "track", "hardware")), author));

        Assert.Equal([applied], preview.GetProperty("sample").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(1, preview.GetProperty("unreachableCount").GetInt32());
    }

    [Theory]
    // A choice is one string, and it is matched exactly: the stored value is
    // FieldOption.Value, which is chosen from a list rather than typed.
    [InlineData("track", "\"hardware\"", "hardware", true)]
    [InlineData("track", "\"software\"", "hardware", false)]

    // Several choices are an array, and the value is one of them. jsonb
    // containment on an array means "contains this element", which is exactly
    // what a checkbox question asks.
    [InlineData("tracks", """["software","hardware"]""", "hardware", true)]
    [InlineData("tracks", """["software"]""", "hardware", false)]

    // A short answer is matched case-insensitively, because somebody who typed
    // Baltimore and somebody who typed baltimore gave the same answer and an
    // organizer picking a value cannot know which.
    [InlineData("city", "\"Baltimore\"", "baltimore", true)]

    // Still equality, though. A segment is not a search, so an answer that
    // merely contains the value is somebody else.
    [InlineData("city", "\"Baltimore MD\"", "Baltimore", false)]

    // A number arrives from a number input as a string and from the
    // application form's normaliser as a number. One answer in two encodings
    // is one answer, and matching only one of them would halve the audience.
    [InlineData("size", "4", "4", true)]
    [InlineData("size", "\"4\"", "4", true)]
    [InlineData("size", "5", "4", false)]

    // An agreement is the same story: a JSON boolean from the page, the string
    // from a caller that posted the checkbox's value.
    [InlineData("agree", "true", "true", true)]
    [InlineData("agree", "\"true\"", "true", true)]
    [InlineData("agree", "false", "true", false)]
    public async Task An_answer_matches_the_chosen_value_whatever_shape_it_was_stored_in(
        string question, string stored, string chosen, bool matches)
    {
        var (_, author) = await Comms();
        var eventId = await db.AddEventAsync();
        var form = await SurveyAsync(eventId, Question(question, FieldType.ShortText));
        var email = await AnsweredAsync(eventId, form, (question, stored));

        var preview = await Body(await Preview(
            await DraftAsync(author, Answer(form.Id, question, chosen)), author));
        var sample = preview.GetProperty("sample").EnumerateArray().ToList();

        if (matches)
        {
            Assert.Equal(email, Assert.Single(sample).GetString());
        }
        else
        {
            Assert.Empty(sample);
        }
    }

    [Fact]
    public async Task An_answer_on_the_application_form_is_matched_where_that_form_keeps_it()
    {
        // The other table. applications.responses has had a GIN index since
        // 0004, created with the comment "So answers living in `responses`
        // stay filterable without promoting them" — the application side was
        // designed for this query, and 0046 is the survey side catching up.
        // Reached through the form's event because an application carries a
        // form version and not a form id.
        var (_, author) = await Comms();
        var eventId = await db.AddEventAsync();
        var (form, version) = await ApplicationFormAsync(
            eventId, Question("track", FieldType.Radio, "hardware", "software"));

        var wanted = Unique("applied-hardware");
        await new PostgresSubmissionStore(db.DataSource).SubmitApplicationAsync(
            form, version, Answers(wanted, ("track", "hardware")));
        await new PostgresSubmissionStore(db.DataSource).SubmitApplicationAsync(
            form, version, Answers(Unique("applied-software"), ("track", "software")));

        var preview = await Body(await Preview(
            await DraftAsync(author, Answer(form.Id, "track", "hardware")), author));

        Assert.Equal([wanted], preview.GetProperty("sample").EnumerateArray().Select(e => e.GetString()));

        // Nothing to explain: an application form keeps its answers on the
        // application, so it has no form_submissions rows to leave behind.
        Assert.Equal(0, preview.GetProperty("unreachableCount").GetInt32());
    }

    [Fact]
    public async Task A_template_greeting_an_answer_segment_by_name_is_rendered_and_sent()
    {
        // Nothing about the answer reaches the message — it is a WHERE clause
        // and never a merge value — but everything that reaches an applicant
        // segment's message has to reach this one's. The failure if it did not
        // is a template that works for every other segment and refuses at
        // Unfillable for this one.
        var (_, author) = await Comms();
        var (_, approver) = await Comms();
        var eventId = await db.AddEventAsync();
        var form = await SurveyAsync(eventId, Question("track", FieldType.Radio, "hardware"));
        var email = await AnsweredAsync(eventId, form, ("track", "\"hardware\""));

        // The copy deliberately never mentions the answer, so the assertion
        // below about the answer not reaching the message is about the
        // renderer rather than about the wording.
        var key = await TemplateWith(
            "Hello {{firstName}}",
            "<p>We have a question for you, {{firstName}}.</p>",
            "We have a question for you, {{firstName}}.");
        var id = await DraftAsync(author, Answer(form.Id, "track", "hardware"), key);

        var preview = await Body(await Preview(id, author));
        Assert.Empty(preview.GetProperty("problems").EnumerateArray());
        Assert.Equal("Hello Ada", preview.GetProperty("renders")[0].GetProperty("subject").GetString());

        Assert.Equal(HttpStatusCode.OK, (await Send(id, approver)).StatusCode);

        var (subject, html, _) = await RenderedIn(id, email);
        Assert.Equal("Hello Ada", subject);
        Assert.Contains("We have a question for you, Ada.", html);

        // And the answer is nowhere in what was frozen into notify.messages,
        // because it was never a value — only the reason this row exists. That
        // is what keeps an answer out of a second schema with different
        // readers and a different retention.
        Assert.DoesNotContain("hardware", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_stored_answer_segment_is_read_back_off_the_campaign_row()
    {
        // The jsonb round trip through notify.campaigns.segment, which is the
        // only copy of who a sent campaign was aimed at. The question and the
        // value are kept as written rather than resolved to a label, so this
        // still says what happened after the question has been reworded or the
        // form taken down.
        var (_, author) = await Comms();
        var eventId = await db.AddEventAsync();
        var form = await SurveyAsync(eventId, Question("track", FieldType.Radio, "hardware"));
        var id = await DraftAsync(author, Answer(form.Id, "track", "hardware"));

        var read = await Body(await Client().SendAsync(
            Request(HttpMethod.Get, $"/admin/campaigns/{id}", author)));
        var segment = read.GetProperty("campaign").GetProperty("segment");

        Assert.Equal("formAnswer", segment.GetProperty("type").GetString());
        Assert.Equal(form.Id, segment.GetProperty("formId").GetGuid());
        Assert.Equal("track", segment.GetProperty("question").GetString());
        Assert.Equal("hardware", segment.GetProperty("value").GetString());

        // And the list says the same thing without having to load the form,
        // which is what lets it keep saying it once the form is gone.
        var listed = (await Body(await Client().SendAsync(
                Request(HttpMethod.Get, "/admin/campaigns", author))))
            .GetProperty("campaigns")
            .EnumerateArray()
            .Single(row => row.GetProperty("id").GetGuid() == id)
            .GetProperty("audience");

        Assert.Equal("formAnswer", listed.GetProperty("type").GetString());
        Assert.Equal("track", listed.GetProperty("question").GetString());
        Assert.Equal("hardware", listed.GetProperty("answer").GetString());
    }

    [Fact]
    public async Task The_picker_is_offered_the_published_questions_and_their_values()
    {
        // The middle and last steps of form → question → value, in one read,
        // behind applications.view. Comms holds that and deliberately does not
        // hold applications.view_responses, which is why this carries the
        // options the form declared and nobody's answer to anything.
        var (_, organizer) = await Comms();
        var eventId = await db.AddEventAsync();
        var form = await SurveyAsync(
            eventId,
            Question("track", FieldType.Radio, "hardware", "software"),
            Question("why", FieldType.Paragraph));

        var read = await Body(await Client().SendAsync(
            Request(HttpMethod.Get, $"/admin/forms/{form.Id}/questions", organizer)));

        // The version is named rather than implied: a question removed in a
        // later version still has answers, and the people who gave them are
        // excluded by it not being offered.
        Assert.Equal(1, read.GetProperty("version").GetInt32());

        var questions = read.GetProperty("questions").EnumerateArray().ToList();
        var track = questions.Single(q => q.GetProperty("key").GetString() == "track");

        Assert.Equal(JsonValueKind.Null, track.GetProperty("unmatchable").ValueKind);
        Assert.Equal(
            ["hardware", "software"],
            track.GetProperty("values").EnumerateArray()
                 .Select(option => option.GetProperty("value").GetString()));

        // Listed, with the sentence, rather than quietly missing.
        var paragraph = questions.Single(q => q.GetProperty("key").GetString() == "why");
        Assert.False(string.IsNullOrWhiteSpace(paragraph.GetProperty("unmatchable").GetString()));
    }

    // ------------------------------------------------------------- fixtures ---

    private PostgresFormStore Forms => new(db.DataSource);

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}@example.com";

    private static object Answer(Guid formId, string question, string value) =>
        new { type = "formAnswer", formId, question, value };

    private static FormField Question(
        string key, FieldType type, params string[] options) => new()
        {
            Key = key,
            Type = type,
            Label = $"Question {key}",
            Options = [.. options.Select(option => new FieldOption(option, option))],
        };

    /// <summary>A published survey on one event.</summary>
    /// <remarks>
    /// A real form rather than a bare form id, because the resolver reaches
    /// the answers through <c>applications.forms</c> — the kind and the event
    /// on that row are what decide which table is read.
    /// </remarks>
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
    /// cannot create an applicant at all and the seeded questions are what
    /// make it one.
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
    /// Three rows, because that is what the real shape is: a person, their
    /// application on the form's event, and their answer filed against the
    /// person. The segment walks all three, and a fixture that skipped the
    /// middle one would be testing a join that does not happen.
    /// </remarks>
    private async Task<string> AnsweredAsync(
        Guid eventId, Form form, params (string Key, string Json)[] answers)
    {
        var email = Unique("answered");
        var personId = await db.AddPersonAsync(email);
        var applicationId = await ApplicantAsync(eventId, email, personId);
        await RecordAsync(form, personId, applicationId, answers);
        return email;
    }

    /// <summary>
    /// A signed-in answer, through the store the sign-in form writes with.
    /// </summary>
    /// <remarks>
    /// The real write path, so the jsonb this test matches against is the jsonb
    /// a real submission produces — which is what the browser posted, not a
    /// normalised copy of it. That difference is the whole reason
    /// <c>Matches</c> accepts more than one encoding.
    /// </remarks>
    private async Task RecordAsync(
        Form form, Guid personId, Guid? applicationId, params (string Key, string Json)[] answers)
    {
        var published = (await Forms.PublishedAsync(form.Id))!;

        await new PostgresRespondentStore(db.DataSource).RecordAsync(
            form.Id,
            published.Version,
            new Respondent(
                personId, applicationId, "unused@example.invalid",
                null, null, "submitted", true, true,
                new Dictionary<string, JsonElement>(StringComparer.Ordinal)),
            Parsed(answers));
    }

    /// <summary>An answer with nobody attached, which 0027 allows.</summary>
    private async Task AnonymouslyAsync(Form form, params (string Key, string Json)[] answers)
    {
        var published = (await Forms.PublishedAsync(form.Id))!;

        await new PostgresAnonymousSubmissionStore(db.DataSource).RecordAsync(
            form.Id, published.Version, Guid.NewGuid(), Parsed(answers));
    }

    /// <summary>
    /// Answers as JSON text, which is how a browser's post is described here.
    /// </summary>
    /// <remarks>
    /// Written as JSON rather than as C# values on purpose: the cases that
    /// matter are a number posted as <c>"4"</c> and a tick posted as
    /// <c>true</c>, and writing them as objects would let the serializer
    /// decide the thing under test.
    /// </remarks>
    private static Dictionary<string, JsonElement> Parsed((string Key, string Json)[] answers) =>
        answers.ToDictionary(
            answer => answer.Key,
            answer => JsonDocument.Parse(answer.Json).RootElement.Clone(),
            StringComparer.Ordinal);

    /// <summary>
    /// An application complete enough for the schema to accept a real status,
    /// tied to a person.
    /// </summary>
    /// <remarks>
    /// <c>person_id</c> is the column that matters here and the one
    /// <c>CampaignTests</c> leaves null: a survey answer is filed against a
    /// person, so an applicant who never signed in cannot be reached through
    /// one. The rest are the columns
    /// <c>submitted_applications_are_complete</c> requires.
    /// </remarks>
    private async Task<Guid> ApplicantAsync(Guid eventId, string email, Guid personId)
    {
        await using var cmd = db.DataSource.CreateCommand("""
            INSERT INTO applications.applications
                (event_id, person_id, email, status, first_name, last_name, age, phone,
                 school, level_of_study, country, mlh_coc_agreed_at, mlh_data_sharing_at,
                 submitted_at)
            VALUES (@eventId, @personId, @email, 'submitted', 'Ada', 'Lovelace', 20,
                    '+15550000000', 'Morgan State University', 'undergraduate',
                    'United States', now(), now(), now())
            RETURNING id
            """);
        cmd.Parameters.AddWithValue("eventId", eventId);
        cmd.Parameters.AddWithValue("personId", personId);
        cmd.Parameters.AddWithValue("email", email);
        return (Guid)(await cmd.ExecuteScalarAsync())!;
    }

    /// <summary>A broadcast template with deliberately meaningless copy.</summary>
    /// <remarks>
    /// Inserted here rather than seeded by a migration, for the reason
    /// <c>CampaignTests</c> gives: template wording is approved by the people
    /// who send the mail, and a plausible-looking body committed to a
    /// migration is a body somebody will eventually send.
    /// </remarks>
    private async Task<string> TemplateWith(string subject, string html, string text)
    {
        var key = $"test-answers-{Guid.NewGuid():N}";
        await using var cmd = db.DataSource.CreateCommand("""
            INSERT INTO notify.templates
                (key, kind, subject, body_html, body_text, from_local, from_domain)
            VALUES (@key, 'broadcast', @subject, @html, @text, 'news',
                    'news.example.invalid')
            """);
        cmd.Parameters.AddWithValue("key", key);
        cmd.Parameters.AddWithValue("subject", subject);
        cmd.Parameters.AddWithValue("html", html);
        cmd.Parameters.AddWithValue("text", text);
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

    /// <summary>A draft campaign aimed at one answer.</summary>
    private async Task<Guid> DraftAsync(string cookie, object segment, string? templateKey = null)
    {
        var created = await Client().SendAsync(Request(HttpMethod.Post, "/admin/campaigns", cookie, new
        {
            name = "People who answered",
            templateKey = templateKey ?? await TemplateWith(
                "placeholder subject", "<p>placeholder</p>", "placeholder"),
            segment,
        }));

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return (await Body(created)).GetProperty("id").GetGuid();
    }

    private Task<HttpResponseMessage> Preview(Guid id, string cookie) =>
        Client().SendAsync(Request(HttpMethod.Post, $"/admin/campaigns/{id}/preview", cookie));

    private Task<HttpResponseMessage> Send(Guid id, string cookie) =>
        Client().SendAsync(Request(HttpMethod.Post, $"/admin/campaigns/{id}/send", cookie));

    /// <summary>An organizer on comms, which is the team that sends broadcasts.</summary>
    /// <remarks>
    /// Through the seeded baseline rather than a hand-written grant, so this
    /// also asserts that the team which builds segments can read the question
    /// list — comms holds <c>applications.view</c> and not
    /// <c>applications.view_responses</c>, and the picker is behind the first.
    /// </remarks>
    private async Task<(Guid Person, string Cookie)> Comms()
    {
        var id = await db.AddPersonAsync(Unique("comms"));
        await db.AddToTeamAsync(id, "comms");

        using var scope = _app.Services.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<SessionService>();
        return (id, $"mh_session={await sessions.StartAsync(id)}");
    }

    private HttpClient Client() =>
        _app.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

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
