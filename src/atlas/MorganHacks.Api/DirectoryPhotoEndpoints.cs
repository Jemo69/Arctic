using Azure;
using Microsoft.AspNetCore.Http.Features;
using MorganHacks.Applications.Services;
using MorganHacks.Directory;
using MorganHacks.Directory.Contracts;
using MorganHacks.Directory.Data;
using MorganHacks.Directory.Domain;
using MorganHacks.Identity.Services;
namespace MorganHacks.Api;

public static partial class DirectoryEndpoints
{
    public const int PhotoMaxBytes = 5 * 1024 * 1024;
    static partial void MapDirectoryPhotos(RouteGroupBuilder portal)
    {
        portal.MapPost("/profile/photo", UploadPhoto).AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext; var feature = http.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (feature is { IsReadOnly: false }) feature.MaxRequestBodySize = PhotoMaxBytes + 65536;
            if (http.Request.ContentLength > PhotoMaxBytes + 65536) return Results.StatusCode(413);
            return await next(context);
        });
        portal.MapDelete("/profile/photo", DetachPhoto);
        portal.MapGet("/photos/{photoId:guid}", ReadPhoto);
    }
    private static async Task<IResult> UploadPhoto(HttpContext http, IApplicantPortalStore applicants, IDirectoryApplicantSource source, DirectoryStore store, CancellationToken ct)
    {
        var e = await Event(http, applicants, source, ct); if (e is null) return Refused();
        if ((await store.Settings(e.Value, ct)).ArchivedAt is not null) throw new DirectoryConflict("The directory has closed. Profiles are read-only.");
        // Do not read or store uploads until the replacement processor validates
        // and re-encodes them. Existing photo reads and removals remain available.
        return Results.Json(new { error = "Photo uploads are temporarily unavailable. You can still save your profile." }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    private static async Task<IResult> DetachPhoto(HttpContext http, IApplicantPortalStore applicants, IDirectoryApplicantSource source, DirectoryStore store, CancellationToken ct)
    {
        var e = await Event(http, applicants, source, ct); if (e is null) return Refused(); var p = await store.Mine(http.PersonId(), e.Value, ct);
        if (p is null) return Results.NotFound(); await store.Save(http.PersonId(), e.Value, p.Fields with { PhotoId = null }, ct); return Results.Ok(Own(await store.Mine(http.PersonId(), e.Value, ct)));
    }
    private static async Task<IResult> ReadPhoto(Guid photoId, HttpContext http, IApplicantPortalStore applicants, IDirectoryApplicantSource source, DirectoryStore store, IIdentityStore identities, IPhotoStorage storage, CancellationToken ct)
    {
        var photo = await store.FindPhoto(photoId, ct); if (photo is null) return Results.NotFound();
        var organizer = (await identities.FindPersonAsync(http.PersonId(), ct))?.Kind == "organizer";
        var allowed = organizer || (photo.PersonId == http.PersonId() && photo.Status != "removed");
        if (!allowed && photo.Status == "attached")
        {
            var e = await Event(http, applicants, source, ct); var p = await store.Mine(photo.PersonId, photo.EventId, ct);
            allowed = e is not null && p is not null && p.Fields.PhotoId == photoId && await Visible(p, e.Value, source, store, ct);
        }
        if (!allowed) return Results.NotFound();
        try { var stream = await storage.Open(photo.StorageKey, ct); http.Response.Headers.CacheControl = "private, max-age=300"; http.Response.Headers.XContentTypeOptions = "nosniff"; return Results.Stream(stream, "image/webp"); }
        catch (RequestFailedException e) when (e.Status == 404) { return Results.NotFound(); }
    }
}
