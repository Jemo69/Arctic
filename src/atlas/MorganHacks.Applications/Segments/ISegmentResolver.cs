namespace MorganHacks.Applications.Segments;

/// <summary>
/// One person a segment resolved to.
/// </summary>
/// <remarks>
/// <see cref="PersonId"/> is null whenever the address is not one this system
/// knows — which is the normal case for <see cref="Segment.Addresses"/>, where
/// the recipient is a mentor or a sponsor rather than an applicant.
/// <para>
/// <see cref="Fields"/> is what a template may fill itself in from, keyed by
/// column name and holding the value as the column holds it — a
/// <see cref="string"/>, an <see cref="int"/> or a <see cref="bool"/>, or null
/// where the applicant left it blank. It rides along because rendering happens
/// once, at queue time, against the values that were true then. Deciding how
/// each of them reads is the mail's job and not this one's.
/// </para>
/// <para>
/// Still not a way to read an application: the only columns in here are the
/// ones <see cref="ApplicantColumns.Mergeable"/> names, and the resolver does
/// not select the others.
/// </para>
/// <para>
/// <see cref="Email"/> is also in <see cref="Fields"/>, and is a property of
/// its own because it is what the message is addressed to, deduped on and
/// suppressed by — none of which is a merge concern, and all of which would
/// otherwise be a dictionary lookup that can miss.
/// </para>
/// </remarks>
public sealed record SegmentMember(
    Guid? PersonId, string Email, IReadOnlyDictionary<string, object?> Fields);

/// <summary>
/// What a segment resolves to, whether it was too big to, and what it had to
/// leave out.
/// </summary>
/// <remarks>
/// <see cref="Overflowed"/> rather than a truncated list, because sending to
/// the first ten thousand of a segment somebody expected to be four hundred is
/// worse than refusing. The caller refuses.
/// <para>
/// <see cref="Unreachable"/> is the count of matching answers there is nobody
/// to mail about, and it exists because a number with no explanation beside it
/// reads as a bug. An organizer looking at a form screen that says forty
/// responses and a campaign screen that says thirty-one recipients will assume
/// something is broken; the nine are anonymous answers, which
/// <c>0027_anonymous_form_answers.sql</c> deliberately allows and which have no
/// person and therefore no address. The alternative — counting them as
/// recipients — is a number that promises mail nobody can receive.
/// </para>
/// <para>
/// Zero for every segment except <see cref="Segment.FormAnswer"/>, because it
/// is the only one that counts answers rather than people. It is not an error
/// and nothing refuses on it: the send is correct, and this is what lets a
/// screen say so.
/// </para>
/// </remarks>
public sealed record ResolvedSegment(
    IReadOnlyList<SegmentMember> Members, bool Overflowed, int Unreachable = 0);

/// <summary>
/// Turns a stored segment into the people it currently means.
/// </summary>
/// <remarks>
/// Lives in Applications because that is where <c>applications.*</c> lives and
/// a module owns its own tables. lark stores the segment document and never
/// looks inside it; this is the only thing that knows what is in there.
/// <para>
/// Deliberately re-run every time rather than cached. That is the whole reason
/// the resolved list is frozen into <c>notify.messages</c> at send: this
/// answers "who does that mean right now", and right now is a different set of
/// people every day of registration week.
/// </para>
/// </remarks>
public interface ISegmentResolver
{
    Task<ResolvedSegment> ResolveAsync(Segment segment, CancellationToken ct = default);

    /// <summary>
    /// One application as a merge source, by id.
    /// </summary>
    /// <remarks>
    /// Here rather than in the mail, because the only honest way to fill
    /// <c>{{firstName}}</c> for one person is to read the same columns a
    /// segment reads, in the same way, and decide nothing extra. A triggered
    /// email could get a member by resolving
    /// <see cref="Segment.InStatus"/> over the whole event and picking the
    /// matching row out of it, which is correct and absurd: four hundred rows
    /// and a dictionary lookup to greet one applicant, four hundred times on
    /// the evening decisions go out.
    /// <para>
    /// It is on this interface rather than on <c>IApplicantStore</c> for the
    /// reason <see cref="SegmentMember"/> gives about not being a way to read
    /// an application: the columns are exactly
    /// <see cref="ApplicantColumns.Mergeable"/> and nothing else, and keeping
    /// that promise in one class is what makes it a promise. A method
    /// somewhere else returning the same record would be a second list of
    /// columns to keep in step, and the way that fails is a withheld column
    /// becoming mailable because somebody typed it out.
    /// </para>
    /// <para>
    /// Null for an id that names no application, which a caller acting on a
    /// row it has just written should never see — and which is still worth
    /// being an answer rather than an exception, because the alternative is a
    /// failed send turning into a failed decision.
    /// </para>
    /// </remarks>
    Task<SegmentMember?> MemberOfAsync(Guid applicationId, CancellationToken ct = default);
}
