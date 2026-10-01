namespace MorganHacks.Applications.Domain;

/// <summary>
/// The zone every date said to an applicant is said in.
/// </summary>
/// <remarks>
/// An <c>rsvp_deadline</c> is an instant, and nobody thinks in instants.
/// Somebody setting "confirm by January 15th at 11:59pm" means an evening in
/// the event's city; that same instant written in UTC is the sixteenth at five
/// in the morning. Formatted without a zone, a deadline the team set on the
/// fifteenth is shown to the applicant as the sixteenth — a day out, for
/// exactly the people the date was for.
/// <para>
/// The event's zone rather than the reader's, matching what the console and
/// the public form already do. A deadline that agrees with the flyer is one
/// the two can be checked against each other, and a fixed zone renders the
/// same on the server as it does on a phone in Denver.
/// </para>
/// <para>
/// Named through <see cref="TimeZoneInfo"/> rather than a fixed offset, so the
/// standard/daylight switch is handled rather than assumed. The project has
/// been caught by that before: the 2026 deadline was written up as EST in a
/// month that was on EDT.
/// </para>
/// </remarks>
public static class EventZone
{
    /// <summary>The IANA id. Matches the two copies in the console.</summary>
    public const string Id = "America/New_York";

    /// <summary>
    /// The zone, or UTC on a host that has never heard of it.
    /// </summary>
    /// <remarks>
    /// Falling back rather than throwing, because the throw would be at static
    /// initialisation on a slim image with no tzdata — which takes out every
    /// route in the API, including the ones that say no date at all. A date an
    /// hour or five out is a bad screen; a portal that will not start is a
    /// worse one, and the difference is visible in the abbreviation either way.
    /// </remarks>
    private static readonly TimeZoneInfo Zone =
        TimeZoneInfo.TryFindSystemTimeZoneById(Id, out var found) ? found : TimeZoneInfo.Utc;

    /// <summary>The same instant, read off the clock in the event's city.</summary>
    public static DateTimeOffset Local(DateTimeOffset instant) =>
        TimeZoneInfo.ConvertTime(instant, Zone);

    /// <summary>
    /// What to call the zone at this instant: <c>EST</c> or <c>EDT</c>.
    /// </summary>
    /// <remarks>
    /// The pair belongs to <see cref="Id"/> and has to change with it, which is
    /// why it is here rather than at the call site — a second copy would be a
    /// second thing to remember when the event moves city, and the failure is
    /// silent because a wrong abbreviation still renders.
    /// <para>
    /// Derived from <see cref="TimeZoneInfo.IsDaylightSavingTime(DateTimeOffset)"/>
    /// rather than the month, because that is the whole reason this class
    /// exists: the 2026 deadline was written up as EST in a month that was on
    /// EDT. On a host with no tzdata <see cref="Zone"/> is UTC and this reads
    /// EST, which is wrong by an hour and says so — which is the point of
    /// printing it at all.
    /// </para>
    /// </remarks>
    public static string Abbreviation(DateTimeOffset instant) =>
        Zone.IsDaylightSavingTime(instant) ? "EDT" : "EST";

    /// <summary>
    /// A date somebody can act on: "January 15, 2027 at 11:59 PM EST".
    /// </summary>
    /// <remarks>
    /// The same sentence <c>libs/ui/zone.ts</c> renders on the console and the
    /// public form, so a deadline in an email and the same deadline on a
    /// screen can be read against each other without anybody having to
    /// translate. If one of the two changes, both have to.
    /// <para>
    /// Invariant culture, because the month names are English copy rather than
    /// a preference — a server whose locale decided to say "janvier" would be
    /// sending a different email from the one that was approved.
    /// </para>
    /// <para>
    /// The abbreviation is not decoration. Half the confusion these dates
    /// cause is somebody in another state reading a time and assuming it is
    /// theirs, and three characters prevent it.
    /// </para>
    /// </remarks>
    public static string Readable(DateTimeOffset instant) =>
        string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{Local(instant):MMMM d, yyyy} at {Local(instant):h:mm tt} {Abbreviation(instant)}");
}
