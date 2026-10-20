using DbUp;
using MorganHacks.Seed;
using Npgsql;
using Testcontainers.PostgreSql;

public sealed class DirectorySeedTests
{
    [Fact]
    public async Task Seeds_processed_photos_and_preserves_existing_photos_on_rerun()
    {
        await using var db = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await db.StartAsync();
        var migration = DeployChanges.To.PostgresqlDatabase(db.GetConnectionString())
            .WithScriptsEmbeddedInAssembly(typeof(MorganHacks.Migrations.MigrationsAssemblyMarker).Assembly)
            .WithTransactionPerScript().Build().PerformUpgrade();
        Assert.True(migration.Successful, migration.Error?.Message);
        await using var source = NpgsqlDataSource.Create(db.GetConnectionString());
        var storage = new Photos();
        var seed = new DirectorySeedRunner(source, storage);
        await seed.RunAsync();
        await using var profiles = source.CreateCommand("SELECT count(*) FROM directory.profiles WHERE event_id=@event");
        profiles.Parameters.AddWithValue("event", DirectorySeedRunner.EventId);
        Assert.Equal(30L, (long)(await profiles.ExecuteScalarAsync())!);
        await using var photos = source.CreateCommand("SELECT count(*) FROM directory.photos");
        Assert.Equal(10, storage.Count);
        Assert.Equal(10L, (long)(await photos.ExecuteScalarAsync())!);
        // Represent an existing stored photo without touching blob storage.
        var photo = Guid.NewGuid();
        await using var attach = source.CreateCommand("""
            INSERT INTO directory.photos(id,person_id,event_id,storage_key,width,height,bytes,status)
            SELECT @photo,person_id,event_id,'fixture/preserved.webp',400,400,100,'attached'
            FROM directory.profiles WHERE person_id=(SELECT id FROM identity.people WHERE email='mock-directory-01@example.com');
            UPDATE directory.profiles SET photo_id=@photo,headline='Keep my edits'
            WHERE person_id=(SELECT id FROM identity.people WHERE email='mock-directory-01@example.com');
            """);
        attach.Parameters.AddWithValue("photo", photo);
        await attach.ExecuteNonQueryAsync();
        await seed.RunAsync();
        Assert.Equal(30L, (long)(await profiles.ExecuteScalarAsync())!);
        Assert.Equal(10, storage.Count);
        Assert.Equal(11L, (long)(await photos.ExecuteScalarAsync())!);
        await using var preserved = source.CreateCommand("SELECT photo_id FROM directory.profiles WHERE headline='Keep my edits'");
        Assert.Equal(photo, (Guid)(await preserved.ExecuteScalarAsync())!);
    }
    private sealed class Photos : MorganHacks.Directory.IPhotoStorage
    {
        public int Count { get; private set; }
        public Task Put(string key, byte[] bytes, CancellationToken ct)
        {
            using var bitmap = SkiaSharp.SKBitmap.Decode(bytes);
            Assert.Equal(400, bitmap.Width);
            Assert.Equal(400, bitmap.Height);
            Assert.Equal("WEBP", System.Text.Encoding.ASCII.GetString(bytes, 8, 4));
            Count++;
            return Task.CompletedTask;
        }
        public Task<Stream> Open(string key, CancellationToken ct) => throw new NotSupportedException();
        public Task DeletePending(string key, CancellationToken ct) => Task.CompletedTask;
    }
}
