using System.Globalization;
using System.Text;
using MorganHacks.Applications.Domain;
using MorganHacks.Applications.Segments;
using MorganHacks.Applications.Services;
using MorganHacks.Lark.Data.Data;
using MorganHacks.Lark.Data.Domain;

namespace MorganHacks.Api;

/// <summary>
/// The <c>{{placeholders}}</c> a broadcast can fill in, and what fills them.
/// </summary>
/// <remarks>
/// One list, read four ways. <see cref="Values"/> builds what
/// <see cref="TemplateRenderer"/> is handed for one recipient;
/// <see cref="Fillable"/> says which of them a segment carries for everybody;
/// <see cref="Unfilled"/> says which of them one person has nothing for; and
/// the two placeholder endpoints hand the names to the template editor so it
/// can offer them.
/// <para>
/// One list rather than four, because the failure of four is silent. An editor
/// offering a placeholder the renderer has never heard of produces a template
/// that reads fine, passes review, and refuses at send — so the person who
/// wrote it finds out from the approver who could not send it, which is the
/// one place in this system where being told late costs somebody else's
/// afternoon.
/// </para>
/// <para>
/// The list itself is not written here. It is
/// <see cref="ApplicantColumns.Mergeable"/> — the columns
/// <c>applications.applications</c> actually has — so a placeholder exists
/// because a column does, and a name nobody can fill cannot be offered because
/// there is nothing to derive it from. What this file adds is the two things
/// that are the mail's business rather than the table's: what a column is
/// called in a template, and how its value reads in a sentence.
/// </para>
/// <para>
/// Derived from a declared list rather than from the live schema. Reading
/// <c>information_schema</c> on every request would be a query on the editor's
/// keystroke and, worse, would let a migration change this API without anybody
/// deciding to. The declaration is checked against the schema by a test
/// instead, so the two cannot drift and the failure lands in CI rather than in
/// a send.
/// </para>
/// </remarks>
public static class MergeFields
{
    /// <summary>
    /// The headings the editor groups placeholders under.
    /// </summary>
    /// <remarks>
    /// Words an organizer writing an email would use, not words from this side
    /// of the screen. <c>Links</c> rather than <c>System</c> for the same
    /// reason the names under it are <c>{{link.portal}}</c> rather than
    /// <c>{{system.portalUrl}}</c>: the person reading the menu is looking for
    /// a link to the portal.
    /// </remarks>
    public static class Groups
    {
        public const string Applicant = "About the person";
        public const string Event = "The event";
        public const string Links = "Links";
        public const string Saved = "Saved values";
    }

    /// <summary>
    /// One placeholder: what to type, what it says, and where it comes from.
    /// </summary>
    /// <remarks>
    /// <see cref="Column"/> is null for anything that is not an answer on an
    /// application. That is the whole difference between the two kinds today —
    /// a column-backed field is looked up per recipient, and everything else
    /// is the same for everybody in the send — and it is why
    /// <see cref="Values"/> takes the configuration as well as the member.
    /// <para>
    /// For a column-backed field <see cref="Name"/> is derived rather than
    /// declared — see <see cref="NameFor"/> — so there is no second spelling
    /// of a column to keep in step with the first. A namespaced name has
    /// nothing to derive from and is written out, which is why the ones below
    /// sit next to the value that fills them.
    /// </para>
    /// </remarks>
    public sealed record MergeField(
        string Name,
        string Group,
        string Description,
        bool OnAddressLists,
        ApplicantColumn? Column = null);

    /// <summary>
    /// Our own addresses, which are the same for everybody in a send.
    /// </summary>
    /// <remarks>
    /// These exist because the alternative is somebody typing
    /// <c>https://www.morganhacks.com/portal</c> into a template, which is a
    /// row in a database — so the same template on staging links to
    /// production. The preview looks right, the test send looks right, and the
    /// only way to find out is to click through and notice which site you
    /// landed on.
    /// <para>
    /// <see cref="MergeField.OnAddressLists"/> is true for all of them: they
    /// do not depend on who is receiving the mail, so a typed list of
    /// addresses can fill them as well as a segment can.
    /// </para>
    /// <para>
    /// The sign-in link is deliberately not here. It is per-recipient and
    /// transactional-only, so offering it in a broadcast editor would be
    /// offering a name that cannot be filled — and saying so needs the
    /// catalogue to carry an audience, which is its own piece of work.
    /// <c>{{link}}</c> keeps working exactly as it does now in the meantime.
    /// </para>
    /// </remarks>
    private static readonly (string Name, string Description, Func<IConfiguration, string> Value)[] Links =
    [
        ("link.portal", "The hacker portal, where somebody checks their application.",
            Origins.Portal),
        ("link.forms", "The site the public forms are served from.",
            Origins.Forms),
        ("link.console", "The organizer console. For mail to the team, not to applicants.",
            Origins.Console),
    ];

    /// <summary>
    /// The season itself: its name, its dates, how many are coming.
    /// </summary>
    /// <remarks>
    /// Written out rather than derived from the columns of
    /// <c>applications.events</c>, which is the opposite of what
    /// <see cref="ApplicantColumns"/> does and is a deliberate difference. That
    /// catalogue is derived because it is wide, changes with migrations, and
    /// its whole risk is a column quietly becoming mailable. This one is seven
    /// fields on a table with one row a year, and every one of them needs a
    /// sentence and a format that the column type cannot supply —
    /// <c>registration_closes_at</c> is a <c>timestamptz</c> like three others
    /// and the only one an applicant is ever shown.
    /// <para>
    /// Dates read through <see cref="EventZone"/>, which is the reason these
    /// exist at all now and did not before. PR #71 withheld
    /// <c>rsvp_deadline</c> from the applicant catalogue in as many words —
    /// "nothing here knows the event's timezone, so a midnight deadline
    /// rendered in UTC lands on the wrong calendar day for exactly the people
    /// it matters to". The zone is known now, and the sentence these render is
    /// the one the console and the public form already show.
    /// </para>
    /// <para>
    /// A date nobody has set yet returns null, which keeps it out of the
    /// dictionary, which leaves the placeholder standing and lets the
    /// campaign's coverage check refuse the send. That is the case worth
    /// getting right: an email promising a deadline the team has not agreed is
    /// worse than one that cannot be sent.
    /// </para>
    /// </remarks>
    private static readonly (string Name, string Description, Func<EventDetail, string?> Value)[] Season =
    [
        ("event.name", "The event's name, as the console spells it.",
            e => string.IsNullOrWhiteSpace(e.Name) ? null : e.Name),

        ("event.startsAt", "When the event starts.",
            e => Moment(e.StartsAt)),
        ("event.endsAt", "When the event ends.",
            e => Moment(e.EndsAt)),

        ("event.registrationOpensAt", "When applications open.",
            e => Moment(e.RegistrationOpensAt)),
        ("event.registrationClosesAt", "The application deadline.",
            e => Moment(e.RegistrationClosesAt)),
        ("event.decisionsAnnouncedAt", "When applicants hear back.",
            e => Moment(e.DecisionsAnnouncedAt)),

        ("event.capacity", "How many people the event can take.",
            e => e.Capacity?.ToString(CultureInfo.InvariantCulture)),
    ];

    /// <summary>A date as a person reads it, or null for one nobody has set.</summary>
    private static string? Moment(DateTimeOffset? instant) =>
        instant is { } set ? EventZone.Readable(set) : null;

    /// <summary>The prefix every saved value is offered under.</summary>
    /// <remarks>
    /// The reason a saved name may not contain a dot, enforced by the check
    /// constraint in <c>0045</c>: with the prefix reserved and the name unable
    /// to hold one, a saved value cannot collide with a built-in name however
    /// either set grows.
    /// </remarks>
    public const string SavedPrefix = "saved.";

    /// <summary>
    /// The catalogue, plus whatever somebody has saved.
    /// </summary>
    /// <remarks>
    /// <see cref="All"/> cannot be a static list any more: saved values are
    /// rows, so the catalogue is only complete once they have been read. Every
    /// caller that offers names to an author or checks a name against them
    /// goes through here; <see cref="All"/> remains what the declaration
    /// alone knows, which is what the schema test compares.
    /// </remarks>
    public static IReadOnlyList<MergeField> Including(IEnumerable<SavedValue> saved) =>
    [
        .. All,
        .. saved.Select(value => new MergeField(
            SavedPrefix + value.Name,
            Groups.Saved,
            value.Description ?? "Saved by an organizer.",
            OnAddressLists: true)),
    ];

    /// <summary>Every placeholder the declaration alone knows.</summary>
    public static readonly IReadOnlyList<MergeField> All =
    [
        .. ApplicantColumns.Mergeable.Select(column => new MergeField(
            NameFor(column.Column),
            Groups.Applicant,
            column.Description,
            column.OnAddressLists,
            column)),

        .. Season.Select(field => new MergeField(
            field.Name, Groups.Event, field.Description, OnAddressLists: true)),

        .. Links.Select(link => new MergeField(
            link.Name, Groups.Links, link.Description, OnAddressLists: true)),
    ];

    /// <summary>
    /// The one place a column name becomes a placeholder name.
    /// </summary>
    /// <remarks>
    /// <c>first_name</c> is <c>{{firstName}}</c> because this says so, and
    /// there is nowhere else that could say otherwise. A declared name beside
    /// each column would be a second thing to get right, and the way it goes
    /// wrong is a placeholder the editor offers under one spelling and the
    /// send looks up under another.
    /// </remarks>
    public static string NameFor(string column)
    {
        var parts = column.Split('_', StringSplitOptions.RemoveEmptyEntries);
        var name = new StringBuilder(parts[0]);

        foreach (var part in parts.Skip(1))
        {
            name.Append(char.ToUpperInvariant(part[0])).Append(part, 1, part.Length - 1);
        }

        return name.ToString();
    }

    /// <summary>
    /// The merge values a segment can supply for one recipient.
    /// </summary>
    /// <remarks>
    /// A field with nothing behind it is left out rather than written in
    /// empty, so <see cref="TemplateRenderer"/> leaves the placeholder
    /// standing and <see cref="Unfilled"/> can see that it did. That is what
    /// makes the gap countable: a row autosaved before somebody typed their
    /// name has an email and no first name, and the template greeting them
    /// would reach them as "Hi {{firstName}},".
    /// </remarks>
    public static Dictionary<string, string> Values(
        SegmentMember member,
        IConfiguration config,
        EventDetail? season = null,
        IReadOnlyList<SavedValue>? saved = null)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        // Everything that is the same for the whole send first, so that if a
        // column-backed name ever collided with one the person's own answer
        // would win. It cannot collide today — NameFor produces no dots — and
        // writing it in this order means it stays harmless if that changes.
        foreach (var link in Links)
        {
            values[link.Name] = link.Value(config);
        }

        foreach (var value in saved ?? [])
        {
            values[SavedPrefix + value.Name] = value.Value;
        }

        // Null when there is no event yet, which a fresh database has. Every
        // event placeholder is then left standing and the coverage check
        // refuses the send, which is the right answer: a broadcast that names
        // a season nobody has created is not one to guess at.
        if (season is not null)
        {
            foreach (var field in Season)
            {
                if (field.Value(season) is { } value)
                {
                    values[field.Name] = value;
                }
            }
        }

        foreach (var field in All)
        {
            if (field.Column is not { } column)
            {
                continue;
            }

            if (member.Fields.TryGetValue(column.Column, out var stored)
                && Reads(stored, column) is { } value)
            {
                values[field.Name] = value;
            }
        }

        return values;
    }

    /// <summary>
    /// How one column's value reads inside a sentence, or null for nothing.
    /// </summary>
    /// <remarks>
    /// Per type, because the defaults are wrong in ways somebody only notices
    /// after four hundred copies have left: a <c>boolean</c> stringifies as
    /// "True", which is a word no email has ever contained, and a year through
    /// a thousands separator is "2,027".
    /// <para>
    /// Null is the case that matters, and it covers three things that are the
    /// same thing to a reader: the column is null, the column is text nobody
    /// typed into, or the segment did not carry the column at all. All of them
    /// return null, which keeps the value out of the dictionary, which is what
    /// <see cref="Unfilled"/> counts and what the preview reports before
    /// anybody can send it.
    /// </para>
    /// <para>
    /// The throw is unreachable while the divergence test passes: it fires
    /// only if a column's declared kind stops matching the type the table
    /// hands back, and that test fails first, in CI, with the column named.
    /// Throwing rather than rendering something is deliberate all the same —
    /// the alternative is guessing at a value that goes out under our name.
    /// </para>
    /// </remarks>
    private static string? Reads(object? stored, ApplicantColumn column) =>
        (column.Kind, stored) switch
        {
            (_, null) => null,
            (ColumnKind.Text, string text) => string.IsNullOrWhiteSpace(text) ? null : text,
            (ColumnKind.Integer, int number) => number.ToString(CultureInfo.InvariantCulture),
            (ColumnKind.Boolean, bool yes) => yes ? "yes" : "no",
            _ => throw new InvalidOperationException(
                $"applications.applications.{column.Column} is declared "
                + $"{column.Kind} and came back as "
                + $"{stored.GetType().Name}."),
        };

    /// <summary>The placeholders a segment can fill for everybody in it.</summary>
    public static IReadOnlySet<string> Fillable(
        Segment segment, IReadOnlyList<SavedValue>? saved = null) =>
        new HashSet<string>(
            For(segment, saved).Select(field => field.Name), StringComparer.Ordinal);

    /// <summary>The fields a segment can fill, described for the editor.</summary>
    /// <remarks>
    /// Narrowed rather than annotated. A list that offered <c>{{firstName}}</c>
    /// beside a note saying this segment cannot fill it is a list somebody
    /// clicks anyway.
    /// </remarks>
    public static IEnumerable<MergeField> For(
        Segment segment, IReadOnlyList<SavedValue>? saved = null)
    {
        var catalogue = saved is null ? All : Including(saved);

        return segment is Segment.Addresses
            ? catalogue.Where(field => field.OnAddressLists)
            : catalogue;
    }

    /// <summary>
    /// Which of the placeholders a template asks for this recipient has
    /// nothing to fill.
    /// </summary>
    /// <remarks>
    /// Sorted, because these are read out on a screen and by an assertion, and
    /// both want the same order twice.
    /// </remarks>
    public static IReadOnlyList<string> Unfilled(
        IReadOnlySet<string> wanted,
        SegmentMember member,
        IConfiguration config,
        EventDetail? season = null,
        IReadOnlyList<SavedValue>? saved = null)
    {
        var values = Values(member, config, season, saved);

        return wanted.Where(placeholder => !values.ContainsKey(placeholder))
                     .OrderBy(placeholder => placeholder, StringComparer.Ordinal)
                     .ToList();
    }
}
