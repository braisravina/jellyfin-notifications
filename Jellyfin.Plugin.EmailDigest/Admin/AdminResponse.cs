namespace Jellyfin.Plugin.EmailDigest.Admin;

/// <summary>
/// An HTTP-shaped result produced by <see cref="SmtpAdminService"/>, kept free of ASP.NET types so
/// the logic is testable without a web host.
/// </summary>
/// <param name="StatusCode">The HTTP status code.</param>
/// <param name="Body">The JSON body, or null.</param>
public sealed record AdminResponse(int StatusCode, object? Body);
