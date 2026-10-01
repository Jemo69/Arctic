namespace MorganHacks.Lark.Data.Domain;

/// <summary>
/// What has to happen before a triggered email goes out.
/// </summary>
/// <remarks>
/// Two, because these are the two an organizer asks for — "when somebody fills
/// this in, send them this" and "when we accept somebody, send them this" —
/// and because each points at a different thing, which is why
/// <c>0049</c> holds a check constraint rather than one nullable target
/// column.
/// </remarks>
public enum TriggerOccasion
{
    /// <summary>Somebody completed one named form.</summary>
    FormSubmitted,

    /// <summary>An application reached one named status.</summary>
    StatusReached,
}

public static class TriggerOccasions
{
    /// <summary>
    /// The stored spelling.
    /// </summary>
    /// <remarks>
    /// Explicit rather than derived from the enum name, for the reason
    /// <c>ApplicationStatuses.ToWire</c> gives next door: renaming a C# member
    /// should never silently rewrite what a column means in rows that already
    /// exist.
    /// </remarks>
    public static string ToWire(this TriggerOccasion occasion) => occasion switch
    {
        TriggerOccasion.FormSubmitted => "form_submitted",
        TriggerOccasion.StatusReached => "status_reached",
        _ => throw new ArgumentOutOfRangeException(nameof(occasion), occasion, null),
    };

    /// <summary>Reads a stored occasion back, or throws.</summary>
    /// <remarks>
    /// Throws rather than defaulting, for the reason the status parser does: an
    /// occasion we cannot name is one we cannot decide on, and quietly reading
    /// an unknown one as <see cref="TriggerOccasion.FormSubmitted"/> would mean
    /// firing an automation on something nobody configured.
    /// </remarks>
    public static TriggerOccasion Parse(string wire) => wire switch
    {
        "form_submitted" => TriggerOccasion.FormSubmitted,
        "status_reached" => TriggerOccasion.StatusReached,
        _ => throw new ArgumentException($"Unknown trigger occasion '{wire}'.", nameof(wire)),
    };

    /// <summary>Whether a string is one of the two, without throwing.</summary>
    public static bool TryParse(string? wire, out TriggerOccasion occasion)
    {
        switch (wire)
        {
            case "form_submitted":
                occasion = TriggerOccasion.FormSubmitted;
                return true;
            case "status_reached":
                occasion = TriggerOccasion.StatusReached;
                return true;
            default:
                occasion = default;
                return false;
        }
    }
}

/// <summary>
/// One binding: when this happens on this event, send the email currently live
/// under this key.
/// </summary>
/// <remarks>
/// <see cref="TemplateKey"/> rather than a template id, and <c>0049</c> carries
/// the argument at length. The short version: templates are copy-on-write, so
/// an id names the version that was live the day somebody set the automation
/// up — and an automation that keeps sending the wording nobody has re-read
/// since, or that stops firing because a typo was fixed, is worse than one
/// that follows the name.
/// <para>
/// Exactly one of <see cref="FormId"/> and <see cref="Status"/> is set, which
/// the database enforces rather than this record. A record cannot refuse to be
/// constructed by a hand-written UPDATE during the event, and that is the write
/// worth guarding against.
/// </para>
/// <para>
/// <see cref="Status"/> is the stored spelling rather than an
/// <c>ApplicationStatus</c>, because this project does not reference
/// Applications and should not start: lark owns <c>notify.*</c> and knows
/// nothing about what a status means. Atlas does the parsing on the way in and
/// the way out.
/// </para>
/// </remarks>
public sealed record EmailTrigger(
    Guid Id,
    Guid EventId,
    TriggerOccasion Occasion,
    Guid? FormId,
    string? Status,
    string TemplateKey,
    bool Enabled,
    DateTimeOffset UpdatedAt);

/// <summary>
/// One binding as the screen that lists them needs it.
/// </summary>
/// <remarks>
/// The two extra facts are the ones that tell the three states a row can be in
/// apart, and from the outside all three look like a binding that is set up.
/// <para>
/// <see cref="TemplateLive"/> is false when the key names no live template,
/// which happens because <c>0049</c> binds by key rather than by id so that
/// editing a template does not break the automation — and the other side of
/// that choice is that deleting one leaves the automation pointing at nothing.
/// Loud on the screen rather than discovered when a decision goes out
/// unaccompanied.
/// </para>
/// <para>
/// <see cref="Sent"/> is how many messages it has actually queued. An
/// automation set up with the wrong status on it and one that is working are
/// indistinguishable until somebody can see that one of them has never fired.
/// </para>
/// </remarks>
public sealed record TriggerListing(EmailTrigger Trigger, bool TemplateLive, int Sent);
