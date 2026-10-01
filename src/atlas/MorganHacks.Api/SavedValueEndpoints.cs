using System.Text.RegularExpressions;
using MorganHacks.Identity.Domain;
using MorganHacks.Lark.Data.Data;
using MorganHacks.Observability;

namespace MorganHacks.Api;

/// <summary>
/// The values an organizer saves to reuse: the Discord invite, the venue, the
/// wifi password on the day.
/// </summary>
/// <remarks>
/// Behind <c>email.manage_templates</c> rather than a permission of its own.
/// Changing what <c>{{saved.discordInvite}}</c> means changes every template
/// that uses it, which is the same reach as editing the templates themselves —
/// and a permission per screen is how a grant page becomes a list nobody
/// reads.
/// <para>
/// Not on the sensitive list in <see cref="Permission"/>. These move no PII
/// out of the system and change nobody's access, which is what that list is
/// for.
/// </para>
/// </remarks>
public static partial class SavedValueEndpoints
{
    /// <summary>
    /// The same rule the database enforces, checked here for the sentence.
    /// </summary>
    /// <remarks>
    /// The constraint in <c>0045</c> is the one that actually holds — this is
    /// in front of it so an author gets "names are letters and numbers" rather
    /// than a 500 from a constraint violation. Two spellings of one rule, and
    /// the database's is the one that decides; a test would be the way to keep
    /// them together if this ever grows a second condition.
    /// </remarks>
    [GeneratedRegex(@"^[a-z][a-zA-Z0-9]*$")]
    private static partial Regex Name { get; }

    private const int MaxNameLength = 40;
    private const int MaxValueLength = 2000;
    private const int MaxDescriptionLength = 200;

    public sealed record SavedValueRequest(string? Value, string? Description);

    public static IEndpointRouteBuilder MapSavedValues(this IEndpointRouteBuilder app)
    {
        var saved = app.MapGroup("/admin/saved-values");

        saved.MapGet("", List)
             .RequirePermission(Permission.EmailManageTemplates);
        saved.MapPut("/{name}", Save)
             .RequirePermission(Permission.EmailManageTemplates);
        saved.MapDelete("/{name}", Delete)
             .RequirePermission(Permission.EmailManageTemplates);

        return app;
    }

    private static async Task<IResult> List(SavedValueStore store, CancellationToken ct) =>
        Results.Ok(new { values = await store.ListAsync(ct) });

    /// <summary>
    /// Writes one, whether or not it was there.
    /// </summary>
    /// <remarks>
    /// A PUT on the name rather than a POST to the collection, because the
    /// name is the identity and saving the same name twice is one value rather
    /// than two.
    /// </remarks>
    private static async Task<IResult> Save(
        string name,
        SavedValueRequest? body,
        HttpContext http,
        SavedValueStore store,
        ILogger<SavedValueStore> log,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > MaxNameLength || !Name.IsMatch(name))
        {
            return Results.BadRequest(new
            {
                error = "A name starts with a lower-case letter and holds only "
                    + "letters and numbers. No dots: those separate a group from "
                    + "a name, and a saved value that contained one could stand "
                    + "in for a built-in placeholder.",
            });
        }

        if (body?.Value is not { Length: > 0 } value || value.Length > MaxValueLength)
        {
            return Results.BadRequest(new
            {
                error = $"A value is between 1 and {MaxValueLength} characters.",
            });
        }

        if (body.Description is { Length: > MaxDescriptionLength })
        {
            return Results.BadRequest(new
            {
                error = $"A description is at most {MaxDescriptionLength} characters.",
            });
        }

        await store.SaveAsync(
            name,
            value,
            string.IsNullOrWhiteSpace(body.Description) ? null : body.Description.Trim(),
            http.PersonId(),
            ct);

        // The name but never the value. These are typed by organizers and are
        // usually a URL, but "usually" is the wrong standard for a log line —
        // a wifi password saved here would otherwise be in the aggregator.
        log.LogInformation("A saved value was written. {name}", name);

        return Results.Ok(new { values = await store.ListAsync(ct) });
    }

    private static async Task<IResult> Delete(
        string name, SavedValueStore store, ILogger<SavedValueStore> log, CancellationToken ct)
    {
        if (!await store.DeleteAsync(name, ct))
        {
            return Results.NotFound(new { error = "No saved value by that name." });
        }

        log.LogInformation("A saved value was removed. {name}", name);

        return Results.Ok(new { values = await store.ListAsync(ct) });
    }
}
