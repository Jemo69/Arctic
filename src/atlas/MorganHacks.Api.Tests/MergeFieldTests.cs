using Microsoft.Extensions.Configuration;
using MorganHacks.Applications.Domain;
using MorganHacks.Applications.Segments;

namespace MorganHacks.Api.Tests;

/// <summary>
/// The placeholder catalogue against the table it claims to describe.
/// </summary>
/// <remarks>
/// This is the test that lets <see cref="ApplicantColumns"/> be a list in code
/// rather than a read of <c>information_schema</c> on every request. The
/// declaration is the fast, reviewable, decided-once thing; this is what stops
/// it becoming a lie. A column added to <c>applications.applications</c> fails
/// here until somebody says, in a sentence, whether a broadcast may fill
/// itself in from it — which is a build failure with the column named, rather
/// than an API that changed shape because a migration ran.
/// </remarks>
public class MergeFieldSchemaTests(ApplicationsDatabase db)
    : IClassFixture<ApplicationsDatabase>
{
    /// <summary>What Postgres calls the type behind each declared kind.</summary>
    /// <remarks>
    /// <c>information_schema.columns.data_type</c>'s spellings, which is why
    /// <see cref="ColumnKind"/> is named after the SQL type rather than after
    /// how the value reads.
    /// </remarks>
    private static string SqlTypeOf(ColumnKind kind) => kind switch
    {
        ColumnKind.Text => "text",
        ColumnKind.Integer => "integer",
        ColumnKind.Boolean => "boolean",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <summary>Every column the real table has, and its type.</summary>
    private async Task<IReadOnlyDictionary<string, string>> ColumnsAsync()
    {
        await using var cmd = db.DataSource.CreateCommand("""
            SELECT column_name, data_type
              FROM information_schema.columns
             WHERE table_schema = 'applications' AND table_name = 'applications'
            """);

        var columns = new Dictionary<string, string>(StringComparer.Ordinal);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns[reader.GetString(0)] = reader.GetString(1);
        }

        return columns;
    }

    [Fact]
    public async Task Every_column_is_either_offered_or_withheld_on_purpose()
    {
        // Both directions, and both matter. A column in the table and not in
        // the catalogue is one nobody decided about, so it is silently
        // unofferable; a column in the catalogue and not in the table is a
        // placeholder an editor offers and a send cannot fill, which is the
        // failure the catalogue exists to remove.
        var actual = (await ColumnsAsync()).Keys;

        Assert.Equal(
            actual.OrderBy(column => column, StringComparer.Ordinal),
            ApplicantColumns.Declared.OrderBy(column => column, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Every_offered_column_holds_the_type_it_is_declared_to()
    {
        // What makes the per-type rendering safe. MergeFields renders by
        // declared kind and throws if the value is not that type; this is what
        // turns that throw into a build failure instead of a refused send.
        var columns = await ColumnsAsync();

        foreach (var column in ApplicantColumns.Mergeable)
        {
            Assert.Equal(SqlTypeOf(column.Kind), columns[column.Column]);
        }
    }

    [Fact]
    public async Task A_placeholder_reads_a_column_that_is_really_there()
    {
        // The same fact from the placeholder's side, because the name is
        // derived and the derivation is the thing that could be wrong:
        // {{graduationYear}} is only fillable if graduation_year exists.
        var columns = await ColumnsAsync();

        // Only the column-backed ones. A namespaced field like {{link.portal}}
        // has no column to be checked against and is filled from configuration
        // instead, which is what MergeFieldValueTests covers.
        foreach (var field in MergeFields.All.Where(field => field.Column is not null))
        {
            Assert.Contains(field.Column!.Column, columns.Keys);
            Assert.Equal(field.Name, MergeFields.NameFor(field.Column.Column));
        }
    }
}

/// <summary>
/// How each type of value reads once it is in a message.
/// </summary>
/// <remarks>
/// No database. These are about the rendering rather than about the schema,
/// and the schema half is <see cref="MergeFieldSchemaTests"/> next door.
/// </remarks>
public class MergeFieldValueTests
{
    /// <summary>
    /// Configuration with none of the origins set.
    /// </summary>
    /// <remarks>
    /// The link placeholders fall back to their localhost defaults, which is
    /// what a developer sees. These tests are about the applicant values, and
    /// an empty configuration keeps them from depending on a setting.
    /// </remarks>
    private static readonly IConfiguration NoOrigins =
        new ConfigurationBuilder().Build();

    private static SegmentMember Member(params (string Column, object? Value)[] answers) =>
        new(null,
            "someone@example.invalid",
            answers.ToDictionary(
                answer => answer.Column, answer => answer.Value, StringComparer.Ordinal));

    [Fact]
    public void Every_placeholder_is_offered_with_something_beside_it()
    {
        // A name with nothing beside it is a name somebody has to guess at,
        // and the description is what the editor's autocomplete shows.
        Assert.NotEmpty(MergeFields.All);
        Assert.All(MergeFields.All, field =>
            Assert.False(string.IsNullOrWhiteSpace(field.Description)));
    }

    [Fact]
    public void A_withheld_column_says_why()
    {
        // The sentence is the useful half. A catalogue that only recorded "no"
        // would leave the next person adding a column with nothing to reason
        // from, which is how a column ends up offered by accident.
        Assert.All(ApplicantColumns.Withheld.Values, reason =>
            Assert.False(string.IsNullOrWhiteSpace(reason)));
    }

    [Fact]
    public void A_column_name_becomes_a_placeholder_name_one_way()
    {
        Assert.Equal("firstName", MergeFields.NameFor("first_name"));
        Assert.Equal("levelOfStudy", MergeFields.NameFor("level_of_study"));
        Assert.Equal("email", MergeFields.NameFor("email"));
    }

    [Fact]
    public void A_boolean_reads_as_yes_or_no()
    {
        // Not "True", which is what .NET would hand back and what no email has
        // ever contained.
        Assert.Equal(
            "yes", MergeFields.Values(Member(("first_time_hacker", true)), NoOrigins)["firstTimeHacker"]);

        Assert.Equal(
            "no", MergeFields.Values(Member(("first_time_hacker", false)), NoOrigins)["firstTimeHacker"]);
    }

    [Fact]
    public void A_year_reads_as_digits_and_nothing_else()
    {
        // 2027, never 2,027. A separator here is a number that looks like a
        // price in the middle of a sentence about school.
        Assert.Equal(
            "2027", MergeFields.Values(Member(("graduation_year", 2027)), NoOrigins)["graduationYear"]);
    }

    [Fact]
    public void Text_arrives_as_it_was_typed()
    {
        Assert.Equal(
            "Morgan State University",
            MergeFields.Values(Member(("school", "Morgan State University")), NoOrigins)["school"]);
    }

    [Fact]
    public void Nothing_at_all_is_reported_as_a_gap_rather_than_filled_in_empty()
    {
        // The case the whole gap check is for, now across every type: a null
        // integer, a null boolean, text nobody typed into, and a column the
        // segment did not carry are the same thing to a reader, and all four
        // must leave the placeholder standing so the preview can count it.
        var member = Member(
            ("email", "someone@example.invalid"),
            ("first_name", null),
            ("school", "   "),
            ("graduation_year", null),
            ("first_time_hacker", null));

        var values = MergeFields.Values(member, NoOrigins);

        Assert.Equal("someone@example.invalid", values["email"]);
        Assert.False(values.ContainsKey("firstName"));
        Assert.False(values.ContainsKey("school"));
        Assert.False(values.ContainsKey("graduationYear"));
        Assert.False(values.ContainsKey("firstTimeHacker"));

        // Including shirtSize, which this member has no entry for at all.
        Assert.Equal(
            ["firstName", "graduationYear", "school", "shirtSize"],
            MergeFields.Unfilled(
                new HashSet<string>(StringComparer.Ordinal)
                {
                    "email", "firstName", "school", "graduationYear", "shirtSize",
                },
                member,
                NoOrigins));
    }

    [Fact]
    public void A_placeholder_for_a_column_that_does_not_exist_is_not_offered_or_fillable()
    {
        // resume_key is a real column and deliberately withheld; nickname is
        // not a column at all. Neither is offered, and neither is fillable, so
        // a template using one is refused before the send.
        var applicants = new Segment.InStatus(Guid.NewGuid(), [ApplicationStatus.Accepted]);
        var fillable = MergeFields.Fillable(applicants);
        var offered = MergeFields.All.Select(field => field.Name).ToList();

        foreach (var name in new[] { "resumeKey", "responses", "phone", "status", "nickname" })
        {
            Assert.DoesNotContain(name, offered);
            Assert.DoesNotContain(name, fillable);
        }
    }

    [Fact]
    public void A_list_of_addresses_can_fill_nothing_that_depends_on_the_person()
    {
        // The rule is not "only the address" — it is that a typed list carries
        // no answers, so nothing derived from an application can be filled.
        // Our own links are the same sentence for everybody in the send and
        // are unaffected by who is receiving it, so they stay offered.
        var fillable = MergeFields.Fillable(
            new Segment.Addresses(["someone@example.invalid"]));

        Assert.Contains("email", fillable);
        Assert.Contains("link.portal", fillable);

        foreach (var personal in new[]
                 { "firstName", "lastName", "school", "graduationYear", "shirtSize" })
        {
            Assert.DoesNotContain(personal, fillable);
        }
    }

    [Fact]
    public void A_link_placeholder_is_filled_for_everybody_in_the_send()
    {
        // Not per-recipient, which is the point: it comes from configuration
        // rather than from the member, so a typed address list fills it too.
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PublicBaseUrl"] = "https://portal.example.test/",
                ["FormsBaseUrl"] = "https://forms.example.test",
                ["ConsoleBaseUrl"] = "https://admin.example.test",
            })
            .Build();

        var values = MergeFields.Values(Member(("email", "a@example.invalid")), config);

        // The trailing slash is trimmed, because these are joined to paths.
        Assert.Equal("https://portal.example.test", values["link.portal"]);
        Assert.Equal("https://forms.example.test", values["link.forms"]);
        Assert.Equal("https://admin.example.test", values["link.console"]);
    }

    [Fact]
    public void A_link_placeholder_says_what_it_is()
    {
        // Same contract as every other placeholder: the editor shows this
        // beside the name, and a name with nothing beside it is one somebody
        // has to guess at.
        var links = MergeFields.All.Where(field => field.Group == MergeFields.Groups.Links);

        Assert.NotEmpty(links);
        Assert.All(links, field =>
        {
            Assert.StartsWith("link.", field.Name);
            Assert.False(string.IsNullOrWhiteSpace(field.Description));
            Assert.Null(field.Column);
        });
    }
}
