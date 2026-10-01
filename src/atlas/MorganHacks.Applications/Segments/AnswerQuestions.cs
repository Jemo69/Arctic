using MorganHacks.Applications.Forms;

namespace MorganHacks.Applications.Segments;

/// <summary>
/// One question, and whether an audience can be chosen by the answer to it.
/// </summary>
/// <remarks>
/// <see cref="Unmatchable"/> is null for a question a
/// <see cref="Segment.FormAnswer"/> can be built on, and a sentence otherwise.
/// A sentence rather than a flag, and the question is still listed, because the
/// organizer looking for the hardware-track question needs to find out that the
/// one they are looking at is the wrong kind — a list that silently omitted it
/// would read as the question having been deleted.
/// <para>
/// <see cref="Values"/> is what the picker offers to choose between, and it is
/// the question's own declared options. It is never a list of what people
/// answered: see <see cref="AnswerQuestions"/> for why that distinction is a
/// permission boundary rather than a convenience.
/// </para>
/// </remarks>
public sealed record AnswerQuestion(
    string Key,
    string Label,
    FieldType Type,
    IReadOnlyList<FieldOption> Values,
    string? Unmatchable);

/// <summary>
/// The questions on a form that an answer-based audience can be built on.
/// </summary>
/// <remarks>
/// Here rather than in the API because it is a statement about what
/// <see cref="PostgresSegmentResolver"/> can do with a stored answer, and that
/// is this module's business. The endpoint turns it into JSON and decides
/// nothing.
/// <para>
/// <b>Why the values are the form's and never the answers'.</b> A picker that
/// offered the distinct values people actually typed would be a list of what
/// several hundred people wrote about themselves, which is
/// <c>applications.view_responses</c> — a permission comms deliberately does
/// not hold, for the reason <c>0014_view_responses.sql</c> gives at length.
/// The options a choice question declares are a property of the form, readable
/// by anybody who can see the form, and they are what somebody picking "wants
/// a hardware track" is looking for anyway. For a short answer or a number
/// there is nothing to offer and the organizer types the value, which is the
/// honest outcome rather than a missing feature.
/// </para>
/// <para>
/// <b>Which version's questions.</b> The published one, which is what is being
/// answered now. A question that existed in version two and is gone from
/// version five still has answers sitting in older submissions, and those
/// people are quietly excluded by not being offered the question — so the
/// screen says which version it is listing rather than leaving that implied.
/// Offering every question that ever existed is the more honest list and a much
/// longer one, and it is not what this does.
/// </para>
/// </remarks>
public static class AnswerQuestions
{
    /// <summary>
    /// Every question on a published form, in the order it is asked.
    /// </summary>
    /// <remarks>
    /// Page breaks are left out entirely. A <see cref="FieldType.Section"/> is
    /// a heading rather than a question, nothing is ever stored under its key,
    /// and listing one with a sentence explaining that nobody answers it would
    /// be explaining the form builder on the campaign screen.
    /// </remarks>
    public static IReadOnlyList<AnswerQuestion> On(
        bool isApplicationForm, IReadOnlyList<FormField> fields) =>
        [.. fields.Where(field => field.Type != FieldType.Section)
                  .Select(field => new AnswerQuestion(
                      field.Key,
                      field.Label,
                      field.Type,
                      field.Options,
                      Unmatchable(isApplicationForm, field)))];

    /// <summary>
    /// Whether this question's answer is kept with the rest of the form's
    /// answers, which is the only place anything reading them back will look.
    /// </summary>
    /// <remarks>
    /// A fact about where the value ended up, and the one place that decides
    /// it. Two things ask: <see cref="Unmatchable"/>, which turns a no into a
    /// sentence for the audience picker, and the mail's placeholder catalogue,
    /// which turns a no into a name it simply does not offer. Narrowing rather
    /// than annotating is <c>MergeFields.For</c>'s rule and the reason this
    /// hands back a bool rather than prose.
    /// <list type="bullet">
    /// <item>An upload stores <em>where the file is</em> and not an answer.
    /// There is no value to match on and none to put in a sentence —
    /// <c>applications.resume_key</c> is withheld from the mail by name for
    /// exactly that reason, and an upload id is the same string under a
    /// different column.</item>
    /// <item>An answer promoted to a column is not in the jsonb at all:
    /// <c>PostgresSubmissionStore</c> writes it to the column instead and not
    /// to both. Only on an application form — every other kind keeps all of
    /// its answers in <c>form_submissions.answers</c> whatever the question
    /// says about a column.</item>
    /// </list>
    /// <para>
    /// <see cref="FieldType.Paragraph"/> is deliberately absent. A long answer
    /// is in the answer set like every other one; it is kept out of
    /// <em>segments</em> by a judgement about what makes a sensible audience,
    /// which is not a fact about storage and is not this function's business.
    /// An email echoing back what somebody wrote is the case it is for.
    /// </para>
    /// <para>
    /// <see cref="FieldType.Section"/> is absent too, because
    /// <see cref="On"/> has already dropped it before anything asks.
    /// </para>
    /// </remarks>
    public static bool InTheAnswerSet(bool isApplicationForm, FormField field) =>
        field.Type != FieldType.File
        && !(isApplicationForm
             && field.Storage == AnswerStorage.Column
             && AnswerColumns.TryKindOf(field.Column, out _));

    /// <summary>
    /// Why this question's answer cannot pick an audience, or null when it can.
    /// </summary>
    /// <remarks>
    /// Three reasons, and each of them is about where the answer ended up or
    /// what shape it is in rather than about how much work the matching would
    /// be. Two of the three are <see cref="InTheAnswerSet"/>, which is where
    /// they belong: the mail's catalogue asks the same question for its own
    /// purposes, and two copies of "where does this answer live" would agree
    /// until one of them was edited.
    /// </remarks>
    private static string? Unmatchable(bool isApplicationForm, FormField field)
    {
        // The one the plan names, and the only exclusion that is a choice
        // rather than a consequence. Matching a paragraph exactly is possible
        // and useless; matching part of one is a search, and a search over
        // what people wrote about themselves is a different feature with a
        // different permission and a different screen. "Everyone who typed
        // hardware somewhere in a paragraph" is also a much vaguer audience
        // than it sounds -- it catches "I am not interested in hardware".
        if (field.Type == FieldType.Paragraph)
        {
            return "Long answers cannot pick an audience. Finding everybody who "
                   + "mentioned a word in a paragraph is a search rather than an "
                   + "audience, and it would catch the people who said the "
                   + "opposite.";
        }

        if (InTheAnswerSet(isApplicationForm, field))
        {
            return null;
        }

        // The sentence rather than the fact, because the two answers read
        // differently to the organizer in front of the picker: one is "there
        // is nothing here to match", the other is "it is somewhere else, and
        // here is what to use instead".
        return field.Type == FieldType.File
            ? "An upload has no answer to match. What is stored is where the "
              + "file is, which is not something to choose recipients by."
            : "This answer is kept in a column of its own rather than with "
              + "the rest of the answers, so it cannot be matched here. "
              + "Choose the applicants by status instead.";
    }
}
