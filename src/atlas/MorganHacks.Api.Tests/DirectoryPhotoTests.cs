using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using MorganHacks.Directory;
using MorganHacks.Directory.Contracts;
using MorganHacks.Directory.Data;
using MorganHacks.Identity.Services;
namespace MorganHacks.Api.Tests;

public partial class DirectoryEndpointTests
{
    [Fact]
    public async Task Foreign_photo_cannot_be_attached_and_only_old_pending_photos_are_cleaned()
    {
        var a = await Hacker(); var b = await Hacker(eventId: a.Event); await Join(a.Cookie); await Join(b.Cookie);
        var id = Guid.NewGuid(); await Store.AddPhoto(id, b.Person, b.Event, "fixture/foreign", [1, 2, 3], photos, default);
        var p = (await Store.Mine(a.Person, a.Event, default))!;
        Assert.Equal(HttpStatusCode.Conflict, (await Send("PUT", "/portal/directory/profile", a.Cookie, p.Fields with { PhotoId = id })).StatusCode);
        var bp = (await Store.Mine(b.Person, b.Event, default))!; await Store.Save(b.Person, b.Event, bp.Fields with { PhotoId = id }, default);
        var old = Guid.NewGuid(); await Store.AddPhoto(old, a.Person, a.Event, "fixture/pending", [1], photos, default);
        await using (var age = db.DataSource.CreateCommand("UPDATE directory.photos SET created_at=now()-interval '25 hours' WHERE id=ANY(@ids)")) { age.Parameters.AddWithValue("ids", new[] { id, old }); await age.ExecuteNonQueryAsync(); }
        Assert.Equal(1, await Store.Cleanup(photos, default)); Assert.NotNull(await Store.FindPhoto(id, default)); Assert.Null(await Store.FindPhoto(old, default));
        await Store.Moderate(b.Event, a.Person, bp.Id, "hide", "reason", null, default);
        Assert.Equal(HttpStatusCode.NotFound, (await Send("GET", $"/portal/directory/photos/{id}", a.Cookie)).StatusCode);
        var org = await db.AddPersonAsync($"photo-org-{Guid.NewGuid():N}@example.com"); Assert.Equal(HttpStatusCode.OK, (await Send("GET", $"/portal/directory/photos/{id}", await Cookie(org))).StatusCode);
        await Store.Moderate(b.Event, org, bp.Id, "remove-photo", null, null, default);
        Assert.Equal(HttpStatusCode.NotFound, (await Send("GET", $"/portal/directory/photos/{id}", b.Cookie)).StatusCode);
    }
}

public partial class DirectoryEndpointTests
{
    [Fact]
    public async Task Uploads_are_unavailable_without_writing_or_replacing_photos()
    {
        var hacker = await Hacker();
        await Join(hacker.Cookie);
        var original = Guid.NewGuid();
        await Store.AddPhoto(original, hacker.Person, hacker.Event, "fixture/existing", [1, 2, 3], photos, default);
        var profile = (await Store.Mine(hacker.Person, hacker.Event, default))!;
        await Store.Save(hacker.Person, hacker.Event, profile.Fields with { PhotoId = original }, default);
        var request = new HttpRequestMessage(HttpMethod.Post, "/portal/directory/profile/photo");
        request.Headers.Add("Cookie", hacker.Cookie);
        request.Content = new MultipartFormDataContent { { new ByteArrayContent([137, 80, 78, 71]), "file", "photo.png" } };
        var response = await app.CreateClient(new() { HandleCookies = false }).SendAsync(request);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Photo uploads are temporarily unavailable. You can still save your profile.", (await Body(response)).GetProperty("error").GetString());
        Assert.Equal(original, (await Store.Mine(hacker.Person, hacker.Event, default))!.Fields.PhotoId);
        await using var count = db.DataSource.CreateCommand("SELECT count(*) FROM directory.photos WHERE person_id=@person");
        count.Parameters.AddWithValue("person", hacker.Person);
        Assert.Equal(1L, (long)(await count.ExecuteScalarAsync())!);
        Assert.Equal(HttpStatusCode.OK, (await Send("GET", $"/portal/directory/photos/{original}", hacker.Cookie)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send("DELETE", "/portal/directory/profile/photo", hacker.Cookie)).StatusCode);
        Assert.Null((await Store.Mine(hacker.Person, hacker.Event, default))!.Fields.PhotoId);
    }

    [Fact]
    public async Task Disabled_uploads_still_enforce_eligibility_and_archive_rules()
    {
        var ineligible = await Hacker("accepted");
        Assert.Equal(HttpStatusCode.Forbidden, (await Send("POST", "/portal/directory/profile/photo", ineligible.Cookie)).StatusCode);
        var hacker = await Hacker();
        await Join(hacker.Cookie);
        await using var archive = db.DataSource.CreateCommand("UPDATE directory.settings SET archived_at=now() WHERE event_id=@event");
        archive.Parameters.AddWithValue("event", hacker.Event);
        await archive.ExecuteNonQueryAsync();
        Assert.Equal(HttpStatusCode.Conflict, (await Send("POST", "/portal/directory/profile/photo", hacker.Cookie)).StatusCode);
    }
}
