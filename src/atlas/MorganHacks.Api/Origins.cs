namespace MorganHacks.Api;

/// <summary>
/// Where each of the three front ends lives.
/// </summary>
/// <remarks>
/// Three origins and not one, which is the whole reason this file is worth
/// having. They were read in two places under two visibilities — the portal
/// and the forms site privately in <see cref="AuthEndpoints"/>, the console
/// privately in <see cref="PeopleEndpoints"/> — and a fourth reader was about
/// to add a third copy. A setting read in several places is a setting that
/// eventually gets a different fallback in one of them, and the way that shows
/// up is one email linking somewhere the others do not.
/// <para>
/// The defaults are the ports <c>deploy/local/dev.sh</c> starts each app on,
/// so a developer sending mail locally gets links that work rather than links
/// to nothing. In a deployed environment all three come from Bicep. Note that
/// <c>PUBLIC_BASE_URL</c> is unset on production today — see
/// <c>docs/runbooks/first-production-deploy.md</c> — so
/// <see cref="Portal"/> there is the localhost default, which is wrong and
/// silent.
/// </para>
/// </remarks>
public static class Origins
{
    /// <summary>
    /// The hacker portal, which is what an emailed sign-in link points at.
    /// </summary>
    /// <remarks>
    /// Before this was threaded through Bicep it silently defaulted below, and
    /// every emailed link pointed at a machine nobody was running.
    /// </remarks>
    public static string Portal(IConfiguration config) =>
        (config["PublicBaseUrl"] ?? "http://localhost:3000").TrimEnd('/');

    /// <summary>
    /// The public forms site, which is not where the portal is.
    /// </summary>
    /// <remarks>
    /// Its own origin, and that is the reason it is not
    /// <see cref="Portal"/>. The session cookie is host-only, so a link that
    /// lands on the portal sets a cookie the forms site will never be sent —
    /// somebody would click "sign in" in their inbox, arrive at a form, and be
    /// asked to sign in again by a browser holding a perfectly good session
    /// for a different hostname.
    /// </remarks>
    public static string Forms(IConfiguration config) =>
        (config["FormsBaseUrl"] ?? "http://localhost:3002").TrimEnd('/');

    /// <summary>
    /// The organizer console, for an email that has to link to it.
    /// </summary>
    /// <remarks>
    /// Its own setting rather than <see cref="Portal"/>, which is the hacker
    /// portal and a different origin entirely. Deriving it from the Google
    /// redirect URI would be closer to true — that really is the console — but
    /// it carries a callback path, and a welcome email that links somebody
    /// into an OAuth callback is worse than one that links nowhere.
    /// </remarks>
    public static string Console(IConfiguration config) =>
        (config["ConsoleBaseUrl"] ?? "http://localhost:3001").TrimEnd('/');
}
