using MorganHacks.Lark.Data.Domain;
using Npgsql;

namespace MorganHacks.Lark.Data.Data;

/// <summary>
/// The bindings that say which email follows which event, and the ledger of
/// which ones have already gone out.
/// </summary>
/// <remarks>
/// Both tables in one class on purpose. They are read together on every
/// occasion — find the binding, then claim the occurrence — and the claim is
/// only correct because it happens in the same transaction as the message
/// insert. Splitting them would put the two halves of one invariant in two
/// files, and the half that gets forgotten is always the one that only matters
/// under concurrency.
/// <para>
/// In <c>Lark.Data</c> beside <see cref="MessageQueue"/> rather than in atlas
/// beside the endpoint that writes it, for the reason
/// <c>QueuedEmailSender</c> gives: lark owns <c>notify.*</c>, and if lark ever
/// moves to its own database only these classes change.
/// </para>
/// <para>
/// Nothing here knows what a status means. The status is the stored spelling,
/// handed in and handed back, because this project does not reference
/// Applications — the parsing and the decision about which statuses may be
/// bound belong where <c>ApplicationStatus</c> lives.
/// </para>
/// </remarks>
public sealed class TriggerStore(NpgsqlDataSource dataSource, MessageQueue queue)
{
    /// <summary>
    /// The select list every read here shares, in the order
    /// <see cref="Read"/> expects.
    /// </summary>
    /// <remarks>
    /// Built from one field list rather than written out per statement,
    /// because <see cref="Read"/> reads by ordinal and three statements
    /// agreeing on an order is three chances for one of them to drift. Take
    /// an alias so the statements that join can qualify them; <c>""</c> for
    /// the ones that do not.
    /// </remarks>
    private static string Columns(string alias = "") => string.Join(
        ", ",
        new[]
        {
            "id", "event_id", "occasion", "form_id", "status", "template_key",
            "enabled", "updated_at",
        }.Select(column => alias is "" ? column : $"{alias}.{column}"));

    /// <summary>
    /// Every binding on one season, whether or not it is switched on.
    /// </summary>
    /// <remarks>
    /// Disabled ones included, because the screen that lists these is also the
    /// screen somebody switches one back on from — and a list that hid them
    /// would make "turn it off for an hour while I fix the wording" into
    /// "delete it and set it up again", which is how a binding comes back
    /// pointing at the wrong template.
    /// <para>
    /// Ordered so the reading is stable: forms first, then statuses, then by
    /// what they point at. A list that reorders between two loads is one
    /// nobody can scan.
    /// </para>
    /// <para>
    /// The template's liveness and the send count come back with the rows
    /// rather than from two more queries. Both are things the screen has to
    /// say about every row — an automation pointing at a deleted template, and
    /// one that was set up and has never fired, are the two states that look
    /// identical otherwise — and a query per row for either would be a
    /// waterfall on the one screen that exists to be scanned.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<TriggerListing>> ListAsync(
        Guid eventId, CancellationToken ct = default)
    {
        // The LEFT JOIN rather than an EXISTS, because the live-key index is
        // the partial unique one 0017 created and this is exactly the lookup
        // it serves: at most one row per key with superseded_at IS NULL.
        await using var cmd = dataSource.CreateCommand($"""
            SELECT {Columns("t")},
                   live.id IS NOT NULL,
                   (SELECT count(*) FROM notify.email_trigger_sends s
                     WHERE s.trigger_id = t.id)
              FROM notify.email_triggers t
              LEFT JOIN notify.templates live
                     ON live.key = t.template_key AND live.superseded_at IS NULL
             WHERE t.event_id = @eventId
             ORDER BY t.occasion, t.status NULLS FIRST, t.template_key
            """);
        cmd.Parameters.AddWithValue("eventId", eventId);

        var listings = new List<TriggerListing>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            listings.Add(new TriggerListing(
                Read(reader), reader.GetBoolean(8), (int)reader.GetInt64(9)));
        }

        return listings;
    }

    /// <summary>
    /// The binding for the status an application has just reached, if there is
    /// one and it is switched on.
    /// </summary>
    /// <remarks>
    /// Reached through the application rather than taking an event id from the
    /// caller, and that is the point. Every writer that moves a status already
    /// holds an application id and most of them do not hold the event — so an
    /// event parameter would be a lookup at each call site, and a call site
    /// that got it wrong would fire last season's automation. The join cannot
    /// be got wrong.
    /// </remarks>
    public Task<EmailTrigger?> ForStatusAsync(
        Guid applicationId, string status, CancellationToken ct = default) =>
        OneAsync($"""
            SELECT {Columns("t")}
              FROM notify.email_triggers t
              JOIN applications.applications a ON a.event_id = t.event_id
             WHERE a.id = @applicationId
               AND t.occasion = 'status_reached'
               AND t.status = @status
               AND t.enabled
            """,
            cmd =>
            {
                cmd.Parameters.AddWithValue("applicationId", applicationId);
                cmd.Parameters.AddWithValue("status", status);
            },
            ct);

    /// <summary>
    /// The binding for a form somebody has just completed, if there is one and
    /// it is switched on.
    /// </summary>
    /// <remarks>
    /// By form rather than by (event, form), because <c>applications.forms</c>
    /// carries its own event and the unique index allows one binding per form
    /// anyway. Asking for both would be asking the caller to agree with a
    /// column it does not own.
    /// </remarks>
    public Task<EmailTrigger?> ForFormAsync(Guid formId, CancellationToken ct = default) =>
        OneAsync($"""
            SELECT {Columns()}
              FROM notify.email_triggers
             WHERE occasion = 'form_submitted' AND form_id = @formId AND enabled
            """,
            cmd => cmd.Parameters.AddWithValue("formId", formId),
            ct);

    /// <summary>One binding by id, enabled or not.</summary>
    public Task<EmailTrigger?> FindAsync(Guid id, CancellationToken ct = default) =>
        OneAsync($"SELECT {Columns()} FROM notify.email_triggers WHERE id = @id",
            cmd => cmd.Parameters.AddWithValue("id", id),
            ct);

    /// <summary>
    /// Writes the binding for one occasion, whether or not there was one.
    /// </summary>
    /// <remarks>
    /// An upsert rather than separate create and update, for the reason
    /// <see cref="SavedValueStore.SaveAsync"/> gives: the screen has one form,
    /// and "there is already a trigger on accepted, edit that one instead" is
    /// a refusal about a row the person is looking at.
    /// <para>
    /// The conflict target names the partial index's predicate as well as its
    /// columns, which is what lets Postgres infer a partial unique index. It
    /// also means the statement says out loud which of the two uniqueness
    /// rules it is relying on, rather than leaving a reader to work out how
    /// nulls behave in a composite index.
    /// </para>
    /// <para>
    /// <c>created_by</c> is left alone on an update. It records who set the
    /// automation up; somebody switching it off is not who wrote it, and
    /// overwriting the column would lose the only name attached to the
    /// decision.
    /// </para>
    /// </remarks>
    public async Task<EmailTrigger> SaveAsync(
        Guid eventId,
        TriggerOccasion occasion,
        Guid? formId,
        string? status,
        string templateKey,
        bool enabled,
        Guid? createdBy,
        CancellationToken ct = default)
    {
        var conflict = occasion is TriggerOccasion.FormSubmitted
            ? "(event_id, form_id) WHERE occasion = 'form_submitted'"
            : "(event_id, status) WHERE occasion = 'status_reached'";

        await using var cmd = dataSource.CreateCommand($"""
            INSERT INTO notify.email_triggers
                (event_id, occasion, form_id, status, template_key, enabled, created_by)
            VALUES (@eventId, @occasion, @formId, @status, @templateKey, @enabled, @createdBy)
            ON CONFLICT {conflict} DO UPDATE
               SET template_key = excluded.template_key,
                   enabled = excluded.enabled
            RETURNING {Columns()}
            """);

        cmd.Parameters.AddWithValue("eventId", eventId);
        cmd.Parameters.AddWithValue("occasion", occasion.ToWire());
        cmd.Parameters.AddWithValue("formId", (object?)formId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("status", (object?)status ?? DBNull.Value);
        cmd.Parameters.AddWithValue("templateKey", templateKey);
        cmd.Parameters.AddWithValue("enabled", enabled);
        cmd.Parameters.AddWithValue("createdBy", (object?)createdBy ?? DBNull.Value);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return Read(reader);
    }

    /// <summary>Removes one binding. False when there was nothing by that id.</summary>
    /// <remarks>
    /// The ledger under it goes with it, by <c>ON DELETE CASCADE</c>. That is
    /// the one consequence worth stating: deleting a binding and setting an
    /// identical one up again will mail everybody it has already mailed. The
    /// switch exists so that nobody has to — see
    /// <see cref="SetEnabledAsync"/>.
    /// </remarks>
    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var cmd = dataSource.CreateCommand(
            "DELETE FROM notify.email_triggers WHERE id = @id");
        cmd.Parameters.AddWithValue("id", id);
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    /// <summary>
    /// Switches one on or off, keeping everything else about it.
    /// </summary>
    /// <remarks>
    /// The reason delete is not the only way to stop the mail. Switching off
    /// keeps the ledger, so switching back on does not re-send to the people
    /// already told — which is exactly what somebody pausing an automation
    /// mid-decision-run is assuming.
    /// </remarks>
    public async Task<EmailTrigger?> SetEnabledAsync(
        Guid id, bool enabled, CancellationToken ct = default)
    {
        await using var cmd = dataSource.CreateCommand($"""
            UPDATE notify.email_triggers SET enabled = @enabled
             WHERE id = @id
            RETURNING {Columns()}
            """);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("enabled", enabled);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Read(reader) : null;
    }

    /// <summary>
    /// Queues one triggered message, or nothing at all if this occurrence has
    /// already been mailed.
    /// </summary>
    /// <remarks>
    /// <b>This is where exactly-once lives, and it is a constraint rather than
    /// a check.</b> The message and the claim go in together; the primary key
    /// on <c>(trigger_id, application_id)</c> decides which of two racing
    /// callers wins, and the loser rolls the message it had already inserted
    /// back out. There is no window between asking "have we done this" and
    /// doing it, because nothing asks — the insert is the question.
    /// <para>
    /// Ordered message-then-claim rather than the other way round, which looks
    /// backwards and is not. The claim carries the message id and the column is
    /// <c>NOT NULL</c>, so the message has to exist first; and rolling a
    /// speculative message back costs nothing, while a claim written before a
    /// failed enqueue would be an occurrence marked dealt with that nobody was
    /// ever told about. The ledger means one thing only: a message exists for
    /// this.
    /// </para>
    /// <para>
    /// <c>ON CONFLICT DO NOTHING</c> rather than catching 23505. A raised
    /// constraint violation aborts the transaction, which would have to be
    /// told apart from every other reason a transaction aborts; zero rows
    /// inserted is the same fact without the ambiguity.
    /// </para>
    /// <para>
    /// Null is the ordinary answer rather than an error. A double-tapped
    /// Accept, a retried request and an organizer moving somebody back and
    /// forth all arrive here, and all three are the system working.
    /// </para>
    /// </remarks>
    /// <returns>The queued message's id, or null when it had already been sent.</returns>
    public async Task<Guid?> QueueOnceAsync(
        EmailTrigger trigger,
        Guid applicationId,
        EmailTemplate template,
        string toEmail,
        Guid? personId,
        IReadOnlyDictionary<string, string> values,
        string? correlationId = null,
        CancellationToken ct = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        var messageId = await queue.EnqueueTransactionalAsync(
            connection, transaction, template, toEmail, personId, values, correlationId, ct);

        const string claim = """
            INSERT INTO notify.email_trigger_sends
                (trigger_id, application_id, message_id)
            VALUES (@triggerId, @applicationId, @messageId)
            ON CONFLICT (trigger_id, application_id) DO NOTHING
            """;

        await using (var cmd = new NpgsqlCommand(claim, connection, transaction))
        {
            cmd.Parameters.AddWithValue("triggerId", trigger.Id);
            cmd.Parameters.AddWithValue("applicationId", applicationId);
            cmd.Parameters.AddWithValue("messageId", messageId);

            if (await cmd.ExecuteNonQueryAsync(ct) == 0)
            {
                // Somebody got here first. The message above goes back with
                // the transaction, so nothing was queued and nothing has to be
                // cleaned up afterwards.
                //
                // Rolled back without the token, unlike every other call here.
                // This is the cleanup path and a cancelled request is one of
                // the ways it is reached, so passing the token would mean the
                // rollback itself could throw and leave the message behind —
                // which is the one outcome this method exists to prevent.
                await transaction.RollbackAsync();
                return null;
            }
        }

        await transaction.CommitAsync(ct);
        return messageId;
    }

    /// <summary>Whether this occurrence has already been mailed.</summary>
    /// <remarks>
    /// For reading, never for deciding. <see cref="QueueOnceAsync"/> is the
    /// only thing that may act on the answer, because by the time a caller has
    /// read this the answer can already have changed — which is the whole
    /// reason the constraint is in the table.
    /// </remarks>
    public async Task<bool> HasSentAsync(
        Guid triggerId, Guid applicationId, CancellationToken ct = default)
    {
        await using var cmd = dataSource.CreateCommand("""
            SELECT 1 FROM notify.email_trigger_sends
             WHERE trigger_id = @triggerId AND application_id = @applicationId
            """);
        cmd.Parameters.AddWithValue("triggerId", triggerId);
        cmd.Parameters.AddWithValue("applicationId", applicationId);
        return await cmd.ExecuteScalarAsync(ct) is not null;
    }

    private async Task<EmailTrigger?> OneAsync(
        string sql, Action<NpgsqlCommand> bind, CancellationToken ct)
    {
        await using var cmd = dataSource.CreateCommand(sql);
        bind(cmd);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Read(reader) : null;
    }

    private static EmailTrigger Read(NpgsqlDataReader reader) => new(
        reader.GetGuid(0),
        reader.GetGuid(1),
        TriggerOccasions.Parse(reader.GetString(2)),
        reader.IsDBNull(3) ? null : reader.GetGuid(3),
        reader.IsDBNull(4) ? null : reader.GetString(4),
        reader.GetString(5),
        reader.GetBoolean(6),
        reader.GetFieldValue<DateTimeOffset>(7));
}
