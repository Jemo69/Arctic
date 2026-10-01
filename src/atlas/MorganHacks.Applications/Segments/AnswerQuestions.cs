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
    /// Why this question's answer cannot pick an audience, or null when it can.
    /// </summary>
    /// <remarks>
    /// Three reasons, and each of them is about where the answer ended up or
    /// what shape it is in rather than about how much work the matching would
    /// be.
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

        if (field.Type == FieldType.File)
        {
            return "An upload has no answer to match. What is stored is where the "
                   + "file is, which is not something to choose recipients by.";
        }

        // An answer promoted to a column is not in the jsonb the segment
        // reads -- PostgresSubmissionStore writes it to the column instead and
        // not to both -- so the question would offer a value that matches
        // nobody. Only on an application form: every other kind keeps all of
        // its answers in form_submissions.answers whatever the question says
        // about a column.
        if (isApplicationForm
            && field.Storage == AnswerStorage.Column
            && AnswerColumns.TryKindOf(field.Column, out _))
        {
            return "This answer is kept in a column of its own rather than with "
                   + "the rest of the answers, so it cannot be matched here. "
                   + "Choose the applicants by status instead.";
        }

        return null;
    }
}
