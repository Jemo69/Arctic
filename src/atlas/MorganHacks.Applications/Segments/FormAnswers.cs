using System.Text.Json;

namespace MorganHacks.Applications.Segments;

/// <summary>
/// The questions on the form a template names, and what each recipient
/// answered them.
/// </summary>
/// <remarks>
/// One type for both halves because they never travel apart.
/// <see cref="Questions"/> is what the editor is offered and
/// <see cref="Given"/> is what the send fills in, and the whole point of
/// <c>MergeFields</c> is that those two are the same list read twice — a
/// question offered under one spelling and looked up under another is the
/// failure that file exists to remove.
/// <para>
/// <b>Keyed by address, and read once per send.</b> A campaign is several
/// hundred recipients and the answers are one query, so what rides through
/// rendering is the whole send's worth, looked up per person with a dictionary
/// hit. The address is the key because it is the one thing every recipient
/// has: <see cref="SegmentMember.PersonId"/> is null for anybody who applied
/// without signing in and for every <see cref="Segment.Addresses"/> recipient,
/// and an application's answers hang off the application rather than off a
/// person.
/// </para>
/// <para>
/// <b>Somebody who never answered is absent rather than empty.</b>
/// <see cref="Of"/> hands back nothing for them, which keeps their
/// placeholders out of the merge dictionary, which leaves them standing in the
/// body and lets the campaign's coverage check count them and refuse the send.
/// That is the same treatment a null applicant column gets and for the same
/// reason: an email that says "you told us your shirt size is" and then stops
/// is worse than one that will not go out.
/// </para>
/// <para>
/// <b>A rendered answer leaves <c>applications.*</c>.</b> Rendering happens at
/// queue time and freezes the result into <c>notify.messages</c> — a second
/// copy, in another schema, with different readers and a different retention.
/// <c>ApplicantColumns.Withheld</c> refuses <c>dietary_needs</c> on exactly
/// that ground and <c>Redaction.SensitiveKeys</c> says those values do not
/// leave <c>applications.*</c>. This feature crosses that line deliberately,
/// because an organizer confirming back what somebody chose is the thing it is
/// for, and the owner decided that every question on a bound form may be
/// echoed rather than that some subset may. Worth knowing before reaching for
/// it: the questions a form asks are the author's to choose, so a form that
/// asks something sensitive and a template that merges it put that answer in
/// notify's retention with nothing in between.
/// </para>
/// </remarks>
public sealed record FormAnswers(
    IReadOnlyList<AnswerQuestion> Questions,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, JsonElement>> Given)
{
    /// <summary>What somebody who answered nothing has.</summary>
    private static readonly IReadOnlyDictionary<string, JsonElement> Silence =
        new Dictionary<string, JsonElement>(StringComparer.Ordinal);

    /// <summary>No form named, so no questions and nobody's answers.</summary>
    public static readonly FormAnswers None = Asked([]);

    /// <summary>
    /// The questions and nobody's answers.
    /// </summary>
    /// <remarks>
    /// What the two placeholder endpoints want. They are drawing a menu for
    /// somebody who is typing, and which names exist is a property of the form
    /// rather than of who has answered it — so they must not pay for a read of
    /// several hundred people's answers to list six questions.
    /// </remarks>
    public static FormAnswers Asked(IReadOnlyList<AnswerQuestion> questions) =>
        new(questions, EmptyGiven());

    /// <summary>What one recipient answered. Empty for somebody who did not.</summary>
    /// <remarks>
    /// Case-insensitively, because a stored address and a typed one differ by
    /// case often enough that the rest of the send already treats them as one
    /// — see the suppression check and the render sample.
    /// </remarks>
    public IReadOnlyDictionary<string, JsonElement> Of(string email) =>
        Given.TryGetValue(email, out var answers) ? answers : Silence;

    /// <summary>
    /// The shape a lookup fills in, so every caller agrees on the comparer.
    /// </summary>
    /// <remarks>
    /// The comparer is the part worth not leaving to a call site. A dictionary
    /// built with the default one looks identical and quietly finds nobody
    /// whose address is stored in a different case from the one the segment
    /// handed back.
    /// </remarks>
    public static Dictionary<string, IReadOnlyDictionary<string, JsonElement>> EmptyGiven() =>
        new(StringComparer.OrdinalIgnoreCase);
}
