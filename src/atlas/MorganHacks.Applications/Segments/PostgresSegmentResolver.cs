using System.Globalization;
using System.Text.Json;
using MorganHacks.Applications.Domain;
using Npgsql;
using NpgsqlTypes;

namespace MorganHacks.Applications.Segments;

/// <summary>
/// Resolves segments against <c>applications.*</c>.
/// </summary>
/// <remarks>
/// Every query here reads one more row than it is allowed to return, so the
/// caller can tell "exactly the maximum" from "more than the maximum" without
/// counting the whole table first. See <see cref="Segment.MaxRecipients"/> for
/// why there is a maximum at all.
/// </remarks>
public sealed class PostgresSegmentResolver(NpgsqlDataSource dataSource) : ISegmentResolver
{
    /// <summary>
    /// The select list every applicant query shares.
    /// </summary>
    /// <remarks>
    /// Built from <see cref="ApplicantColumns.Mergeable"/> rather than typed
    /// out, so a column becomes readable here by being declared there and by
    /// nothing else. The two queries below would otherwise be a second and a
    /// third place to remember, and the failure of forgetting one is an editor
    /// offering a placeholder that comes out blank for a whole segment.
    /// <para>
    /// Interpolated into SQL, which is worth being explicit about: every part
    /// of this string is a column name from a list declared in code, and a
    /// test asserts that every one of them is a real column of
    /// <c>applications.applications</c>. Nothing a caller sends reaches it.
    /// </para>
    /// <para>
    /// Withheld columns are not selected at all. A resume key that is never
    /// read out of the table cannot end up in a rendered body by accident.
    /// </para>
    /// </remarks>
    private static readonly string Selected = string.Join(
        ", ",
        new[] { "a.person_id" }.Concat(
            ApplicantColumns.Mergeable.Select(column => $"a.{column.Column}")));

    public Task<ResolvedSegment> ResolveAsync(
        Segment segment, CancellationToken ct = default) => segment switch
        {
            Segment.InStatus s => InStatusAsync(s, ct),
            Segment.FormRespondents s => RespondentsAsync(s, ct),
            Segment.FormAnswer s => AnsweredAsync(s, ct),
            Segment.Addresses s => Task.FromResult(Addresses(s)),
            _ => throw new ArgumentOutOfRangeException(nameof(segment), segment, null),
        };

    /// <summary>
    /// One application, read exactly as a segment reads it.
    /// </summary>
    /// <remarks>
    /// The same select list and the same reader as everything else here, which
    /// is the only reason this method is allowed to exist — see
    /// <see cref="ISegmentResolver.MemberOfAsync"/> for why a second place
    /// building a <see cref="SegmentMember"/> would be a second list of
    /// columns to keep in step.
    /// <para>
    /// No limit and no overflow check. One id is one row by primary key, so
    /// the reasoning <see cref="Segment.MaxRecipients"/> exists for has
    /// nothing to apply to.
    /// </para>
    /// </remarks>
    public async Task<SegmentMember?> MemberOfAsync(
        Guid applicationId, CancellationToken ct = default)
    {
        var sql = $"""
            SELECT {Selected}
              FROM applications.applications a
             WHERE a.id = @applicationId
            """;

        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("applicationId", applicationId);

        var resolved = await ReadAsync(cmd, ct);
        return resolved.Members.Count == 1 ? resolved.Members[0] : null;
    }

    /// <summary>
    /// Everyone on one event whose application is in one of these states.
    /// </summary>
    /// <remarks>
    /// No <c>DISTINCT</c> and no dedupe pass, because the unique index on
    /// (event_id, lower(email)) already guarantees one row per address per
    /// event. That index is the reason one person cannot appear twice in a
    /// decision email, and it holds no matter what wrote the rows.
    /// </remarks>
    private async Task<ResolvedSegment> InStatusAsync(
        Segment.InStatus segment, CancellationToken ct)
    {
        var sql = $"""
            SELECT {Selected}
              FROM applications.applications a
             WHERE a.event_id = @eventId AND a.status = ANY(@statuses)
             ORDER BY a.email
             LIMIT @limit
            """;

        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("eventId", segment.EventId);
        cmd.Parameters.Add(new NpgsqlParameter("statuses", NpgsqlDbType.Array | NpgsqlDbType.Text)
        {
            Value = segment.Statuses.Select(s => s.ToWire()).ToArray(),
        });
        cmd.Parameters.AddWithValue("limit", Segment.MaxRecipients + 1);

        return await ReadAsync(cmd, ct);
    }

    /// <summary>
    /// Everyone who submitted a given form.
    /// </summary>
    /// <remarks>
    /// Reached through the form's event, because an application carries a form
    /// version and not a form id. The <c>kind = 'application'</c> check is what
    /// keeps that honest: a survey sitting on the same event would otherwise
    /// resolve to the application form's respondents, and somebody would mail
    /// four hundred applicants a note meant for eleven mentors.
    /// <para>
    /// <c>submitted_at IS NOT NULL</c> rather than a status list. Somebody who
    /// opened the form and typed their name has a row — the form autosaves —
    /// and they have not answered anything.
    /// </para>
    /// </remarks>
    private async Task<ResolvedSegment> RespondentsAsync(
        Segment.FormRespondents segment, CancellationToken ct)
    {
        var sql = $"""
            SELECT {Selected}
              FROM applications.applications a
              JOIN applications.forms f ON f.event_id = a.event_id
             WHERE f.id = @formId
               AND f.kind = 'application'
               AND a.submitted_at IS NOT NULL
             ORDER BY a.email
             LIMIT @limit
            """;

        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("formId", segment.FormId);
        cmd.Parameters.AddWithValue("limit", Segment.MaxRecipients + 1);

        return await ReadAsync(cmd, ct);
    }

    /// <summary>
    /// The jsonb predicate that decides whether one answer matches.
    /// </summary>
    /// <remarks>
    /// <b>Equality and membership, and nothing else.</b> The four alternatives
    /// are one rule read against the four shapes an answer is actually stored
    /// in, not four features:
    /// <list type="bullet">
    /// <item><c>@&gt; @exact</c> — the answer <em>is</em> this value. A choice,
    /// a short answer, a date.</item>
    /// <item><c>@&gt; @chosen</c> — this value is <em>one of</em> those
    /// selected. jsonb containment on an array means "contains this element",
    /// which is exactly what a checkbox question asks.</item>
    /// <item><c>@&gt; @alternate</c> — the same value as a JSON boolean or
    /// number. This is not laxity: <c>applications.responses</c> is written
    /// through a normaliser that types the value, and
    /// <c>form_submissions.answers</c> holds what the browser posted, where a
    /// number input posts <c>"21"</c> and a checkbox posts <c>true</c>. One
    /// stored answer in two encodings is one answer, and a segment that
    /// matched only one of them would quietly halve the audience.</item>
    /// <item><c>lower(-&gt;&gt;) = lower()</c> — the short-answer case, which
    /// is the only one that is case-insensitive. Somebody typing
    /// <c>Hardware</c> and somebody typing <c>hardware</c> gave the same
    /// answer, and an organizer picking a value cannot know which they typed.
    /// </item>
    /// </list>
    /// <para>
    /// <b>Long free text is deliberately not here.</b> There is no
    /// <c>LIKE '%...%'</c> branch, and the reason is not that it is hard: it
    /// is that substring matching over a paragraph is a search feature wearing
    /// a segment's clothes. "Everyone who typed <em>hardware</em> somewhere in
    /// a paragraph" is a far vaguer audience than it sounds, it cannot be read
    /// back as a sentence a month later, and it cannot use either GIN index —
    /// <c>jsonb_ops</c> indexes whole values, so a containment lookup is an
    /// index scan and a substring test is a recheck of every row. A paragraph
    /// question can still be targeted by this, but only by typing the answer
    /// back verbatim, which nobody will do by accident. The picker does not
    /// offer them — see <see cref="AnswerQuestions"/>, which says so in a
    /// sentence an organizer reads.
    /// </para>
    /// <para>
    /// The column name is interpolated and the question and value are bound.
    /// Every call site is in this file and passes a literal; nothing a caller
    /// of the resolver sends reaches the string.
    /// </para>
    /// </remarks>
    private static string Matches(string column) => $"""
        (    {column} @> @exact
          OR {column} @> @chosen
          OR (@alternate IS NOT NULL AND {column} @> @alternate)
          OR lower({column} ->> @question) = lower(@value))
        """;

    /// <summary>
    /// Everyone who answered one question on one form with one value.
    /// </summary>
    /// <remarks>
    /// Two places an answer can live, so two branches over one select list.
    /// An application form's answers are in
    /// <c>applications.applications.responses</c>, reached through the form's
    /// event because an application carries a form version and not a form id —
    /// the same reasoning <see cref="RespondentsAsync"/> gives. Every other
    /// kind of form writes <c>applications.form_submissions.answers</c>, and
    /// those are reached back to an applicant through the person who gave
    /// them.
    /// <para>
    /// A form has one kind, so only one branch can match — but the branches
    /// are an <c>OR</c> over one row of <c>applications</c> rather than a
    /// union, so a row that somehow satisfied both is still one recipient.
    /// <c>a.event_id = f.event_id</c> is what makes that true across events as
    /// well: without it, somebody who has applied two years running would
    /// resolve twice from one answer.
    /// </para>
    /// <para>
    /// <b>Anonymous answers are excluded, by name.</b>
    /// <c>0027_anonymous_form_answers.sql</c> made <c>person_id</c> nullable so
    /// an answer with nobody attached could be kept, and those rows have no
    /// person and therefore no address. <c>s.person_id IS NOT NULL</c> is
    /// implied by the join below and written anyway, because it is the rule
    /// rather than a consequence of how the join happens to be spelled today.
    /// They are counted instead — see <see cref="UnreachableAsync"/> — because
    /// a recipient count that is quietly smaller than the response count on
    /// the form screen reads as a bug.
    /// </para>
    /// </remarks>
    private async Task<ResolvedSegment> AnsweredAsync(
        Segment.FormAnswer segment, CancellationToken ct)
    {
        var sql = $"""
            SELECT {Selected}
              FROM applications.applications a
              JOIN applications.forms f ON f.id = @formId
             WHERE a.event_id = f.event_id
               AND ((f.kind = 'application'
                     AND a.submitted_at IS NOT NULL
                     AND {Matches("a.responses")})
                 OR EXISTS (SELECT 1
                              FROM applications.form_submissions s
                             WHERE s.form_id = f.id
                               AND s.person_id IS NOT NULL
                               AND s.person_id = a.person_id
                               AND {Matches("s.answers")}))
             ORDER BY a.email
             LIMIT @limit
            """;

        await using var cmd = dataSource.CreateCommand(sql);
        Criterion(cmd, segment);
        cmd.Parameters.AddWithValue("limit", Segment.MaxRecipients + 1);

        var resolved = await ReadAsync(cmd, ct);
        if (resolved.Overflowed)
        {
            // Nothing is being shown, so there is nothing to explain. The
            // caller refuses on this before it reads a count.
            return resolved;
        }

        return resolved with { Unreachable = await UnreachableAsync(segment, ct) };
    }

    /// <summary>
    /// How many matching answers there is nobody to mail about.
    /// </summary>
    /// <remarks>
    /// Every answer that matched and has no applicant behind it on the form's
    /// event: an anonymous submission, or one from somebody who signed in to a
    /// survey without ever applying. Both are real and both are invisible to
    /// the query above, and the number exists so the screen can say why forty
    /// responses are thirty-one recipients instead of leaving an organizer to
    /// guess.
    /// <para>
    /// A second round trip rather than a window function on the first. The two
    /// questions are counted over different tables — people, then answers —
    /// and folding them together would mean the recipient query carried a
    /// subquery that does not affect which rows it returns, which is the kind
    /// of thing that gets deleted by somebody reading it as dead weight.
    /// </para>
    /// <para>
    /// Zero for an application form, with no branch needed: its answers live
    /// on the application, so it has no <c>form_submissions</c> rows for this
    /// to count.
    /// </para>
    /// </remarks>
    private async Task<int> UnreachableAsync(Segment.FormAnswer segment, CancellationToken ct)
    {
        var sql = $"""
            SELECT count(*)
              FROM applications.form_submissions s
              JOIN applications.forms f ON f.id = s.form_id
             WHERE s.form_id = @formId
               AND {Matches("s.answers")}
               AND NOT EXISTS (SELECT 1
                                 FROM applications.applications a
                                WHERE a.event_id = f.event_id
                                  AND a.person_id = s.person_id)
            """;

        await using var cmd = dataSource.CreateCommand(sql);
        Criterion(cmd, segment);

        return (int)(long)(await cmd.ExecuteScalarAsync(ct))!;
    }

    /// <summary>
    /// Every parameter both statements read, from one criterion.
    /// </summary>
    /// <remarks>
    /// The documents are built here rather than with
    /// <c>jsonb_build_object</c> in the statement, so what reaches
    /// <c>@&gt;</c> is a plain parameter. A GIN index can serve a containment
    /// test against a parameter; whether it can serve one against a function
    /// call depends on how that function is declared, and an index this
    /// feature added a migration for should not be optional on a detail like
    /// that.
    /// <para>
    /// <see cref="JsonSerializer"/> rather than string concatenation, because
    /// a question key is a form author's text and a value can contain a quote.
    /// </para>
    /// </remarks>
    private static void Criterion(NpgsqlCommand cmd, Segment.FormAnswer segment)
    {
        cmd.Parameters.AddWithValue("formId", segment.FormId);

        // Typed rather than inferred, because both are operands of an
        // overloaded thing: lower() also takes a range, and -> takes an
        // integer as well as a key. A parameter that arrives without a type
        // makes those ambiguous rather than wrong, which fails at the
        // statement and not at the value.
        cmd.Parameters.Add(new NpgsqlParameter("question", NpgsqlDbType.Text)
        {
            Value = segment.Question,
        });
        cmd.Parameters.Add(new NpgsqlParameter("value", NpgsqlDbType.Text)
        {
            Value = segment.Value,
        });

        Json(cmd, "exact", Document(segment.Question, segment.Value));
        Json(cmd, "chosen", Document(segment.Question, new[] { segment.Value }));
        Json(cmd, "alternate", Alternate(segment.Question, segment.Value));
    }

    /// <summary>The same value as a JSON boolean or number, where it is one.</summary>
    /// <remarks>
    /// Null when the value is neither, which is the common case: a choice is a
    /// string in both tables and needs no second encoding. The parse is
    /// invariant-culture on purpose — the value came out of a form's option
    /// list or a number input, neither of which is localised.
    /// </remarks>
    private static string? Alternate(string question, string value)
    {
        if (bool.TryParse(value, out var yes))
        {
            return Document(question, yes);
        }

        return decimal.TryParse(
            value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number)
            ? Document(question, number)
            : null;
    }

    private static string Document(string question, object? value) =>
        JsonSerializer.Serialize(
            new Dictionary<string, object?>(StringComparer.Ordinal) { [question] = value });

    private static void Json(NpgsqlCommand cmd, string name, string? document) =>
        cmd.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Jsonb)
        {
            Value = (object?)document ?? DBNull.Value,
        });

    /// <summary>
    /// The addresses somebody typed, and nothing looked up about them.
    /// </summary>
    /// <remarks>
    /// No database round trip on purpose. Matching these against
    /// <c>identity.people</c> to fill in a person id would mean reading
    /// another module's table, and matching them against applicants would
    /// defeat the point — this segment exists for the people who are not in
    /// the applicant pool.
    /// <para>
    /// The consequence is that these messages carry no person id, so they do
    /// not appear in the "what have we sent you" history that is keyed on one.
    /// That is the correct answer for a sponsor contact and a small loss for
    /// an applicant somebody happened to paste in.
    /// </para>
    /// <para>
    /// An address fills in the one column
    /// <see cref="ApplicantColumn.OnAddressLists"/> marks and no other, so a
    /// template greeting a sponsor by name is refused before the send rather
    /// than mailed with the greeting still standing in it.
    /// </para>
    /// </remarks>
    private static ResolvedSegment Addresses(Segment.Addresses segment) =>
        new(segment.Emails.Select(email => new SegmentMember(
                null,
                email,
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    [ApplicantColumns.Address.Column] = email,
                })).ToList(),
            Overflowed: false);

    /// <summary>
    /// Reads the rows into members, one column of
    /// <see cref="ApplicantColumns.Mergeable"/> at a time.
    /// </summary>
    /// <remarks>
    /// By ordinal rather than by name, because <see cref="Selected"/> put them
    /// in this order and reading them back by name would be a second agreement
    /// to keep. The value is taken as the column holds it — string, int or
    /// bool — and how each of those reads in a sentence is decided in the mail
    /// rather than here.
    /// </remarks>
    private static async Task<ResolvedSegment> ReadAsync(NpgsqlCommand cmd, CancellationToken ct)
    {
        var members = new List<SegmentMember>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var fields = new Dictionary<string, object?>(StringComparer.Ordinal);
            for (var i = 0; i < ApplicantColumns.Mergeable.Count; i++)
            {
                // Offset by one: person_id is the first thing Selected asks for.
                var ordinal = i + 1;
                fields[ApplicantColumns.Mergeable[i].Column] =
                    await reader.IsDBNullAsync(ordinal, ct) ? null : reader.GetValue(ordinal);
            }

            members.Add(new SegmentMember(
                await reader.IsDBNullAsync(0, ct) ? null : reader.GetGuid(0),

                // NOT NULL on the column, so this cast cannot be the thing that
                // fails; the divergence test is what keeps the column there.
                (string)fields[ApplicantColumns.Address.Column]!,
                fields));
        }

        if (members.Count > Segment.MaxRecipients)
        {
            return new ResolvedSegment([], Overflowed: true);
        }

        return new ResolvedSegment(members, Overflowed: false);
    }
}
