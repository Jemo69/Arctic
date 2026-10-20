using Azure;
using Microsoft.AspNetCore.Http.Features;
using MorganHacks.Applications.Services;
using MorganHacks.Directory;
using MorganHacks.Directory.Contracts;
using MorganHacks.Directory.Data;
using MorganHacks.Directory.Domain;
using MorganHacks.Identity.Services;
using MorganHacks.Directory.Images;
namespace MorganHacks.Api;

public static partial class DirectoryEndpoints
{
    public const int PhotoMaxBytes = DirectoryPhotoProcessor.MaxBytes;
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
    private static async Task<IResult> UploadPhoto(HttpContext http, IApplicantPortalStore applicants, IDirectoryApplicantSource source, DirectoryStore store, IPhotoStorage storage, CancellationToken ct)
    {
        var e = await Event(http, applicants, source, ct); if (e is null) return Refused();
        if ((await store.Settings(e.Value, ct)).ArchivedAt is not null) throw new DirectoryConflict("The directory has closed. Profiles are read-only.");
        if (!http.Request.HasFormContentType) return Results.BadRequest(new { error = "Choose one image file." });
        try
        {
            var form = await http.Request.ReadFormAsync(new FormOptions { MultipartBodyLengthLimit = PhotoMaxBytes + 65536 }, ct);
            if (form.Files.Count != 1) return Results.BadRequest(new { error = "Choose one image file." });
            var file = form.Files[0];
            if (file.Length > PhotoMaxBytes) return Results.StatusCode(413);
            using var input = new MemoryStream();
            await file.CopyToAsync(input, ct);
            var bytes = await DirectoryPhotoProcessor.ProcessAsync(input.ToArray(), ct);
            var id = Guid.NewGuid();
            var key = $"directory/photos/{e.Value}/{id}.webp";
            await store.AddPhoto(id, http.PersonId(), e.Value, key, bytes, storage, ct);
            return Results.Ok(new { photoId = id, previewUrl = PhotoUrl(id) });
        }
        catch (InvalidDataException)
        { return Results.BadRequest(new { error = "Choose a valid JPEG, PNG or WebP up to 5 MB, with each side between 200 and 8000 pixels." }); }
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
