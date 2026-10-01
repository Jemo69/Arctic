using Npgsql;

namespace MorganHacks.Lark.Data.Data;

/// <summary>One value an organizer saved, and what it is for.</summary>
public sealed record SavedValue(
    string Name,
    string Value,
    string? Description,
    DateTimeOffset UpdatedAt);

/// <summary>
/// The values a template can fill itself in from that nothing derives.
/// </summary>
/// <remarks>
/// Everything else in the placeholder catalogue comes from somewhere — a
/// column of <c>applications.applications</c>, a row of
/// <c>applications.events</c>, a setting. These are the ones that come from
/// somebody typing them, which is why they are a table rather than a
/// declaration, and why the screen that edits them matters as much as this
/// does: a value nobody can find is a value somebody retypes into a template.
/// <para>
/// In <c>Lark.Data</c> beside the templates they are read with, rather than in
/// atlas beside the endpoint that writes them. The renderer's neighbours are
/// here and this is one of them.
/// </para>
/// </remarks>
public sealed class SavedValueStore(NpgsqlDataSource dataSource)
{
    /// <summary>Every saved value, by name.</summary>
    /// <remarks>
    /// Ordered by name because it is read onto a screen and into a picker, and
    /// a list that reorders between two loads is one nobody can scan.
    /// </remarks>
    public async Task<IReadOnlyList<SavedValue>> ListAsync(CancellationToken ct = default)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT name, value, description, updated_at
              FROM notify.saved_values
             ORDER BY name
            """);

        await using var reader = await command.ExecuteReaderAsync(ct);

        var values = new List<SavedValue>();
        while (await reader.ReadAsync(ct))
        {
            values.Add(new SavedValue(
                reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetFieldValue<DateTimeOffset>(3)));
        }

        return values;
    }

    /// <summary>
    /// Writes one, whether or not it was there.
    /// </summary>
    /// <remarks>
    /// An upsert rather than separate create and update, because the screen
    /// has one form and the difference is not one an author is thinking about.
    /// The name is the key, so renaming is deleting and saving again — which
    /// is honest: every template referring to the old name stops resolving,
    /// and the campaign checks are what say so.
    /// </remarks>
    /// <exception cref="PostgresException">
    /// SQLSTATE 23514 when the name breaks the check constraint. Left to the
    /// caller because the answer is a sentence about that name.
    /// </exception>
    public async Task SaveAsync(
        string name, string value, string? description, Guid updatedBy,
        CancellationToken ct = default)
    {
        await using var command = dataSource.CreateCommand("""
            INSERT INTO notify.saved_values (name, value, description, updated_by)
            VALUES (@name, @value, @description, @updatedBy)
            ON CONFLICT (name) DO UPDATE
               SET value = excluded.value,
                   description = excluded.description,
                   updated_by = excluded.updated_by
            """);

        command.Parameters.AddWithValue("name", name);
        command.Parameters.AddWithValue("value", value);
        command.Parameters.AddWithValue("description", (object?)description ?? DBNull.Value);
        command.Parameters.AddWithValue("updatedBy", updatedBy);

        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Removes one. False when there was nothing by that name.</summary>
    /// <remarks>
    /// Deleting does not go looking for templates that use it, and that is
    /// deliberate. The name stops resolving, so every template referring to it
    /// starts failing the campaign's unfillable check — loudly, before a send,
    /// which is where this system already reports that class of mistake.
    /// Refusing the delete instead would mean a value nobody can remove
    /// because of a draft nobody remembers writing.
    /// </remarks>
    public async Task<bool> DeleteAsync(string name, CancellationToken ct = default)
    {
        await using var command = dataSource.CreateCommand(
            "DELETE FROM notify.saved_values WHERE name = @name");

        command.Parameters.AddWithValue("name", name);

        return await command.ExecuteNonQueryAsync(ct) > 0;
    }
}
