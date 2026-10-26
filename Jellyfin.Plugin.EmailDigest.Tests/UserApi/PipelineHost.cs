using System;
using System.IO;
using System.Net.Http;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Jellyfin.Plugin.EmailDigest.Admin;
using Jellyfin.Plugin.EmailDigest.Configuration;
using Jellyfin.Plugin.EmailDigest.Delivery;
using Jellyfin.Plugin.EmailDigest.Services;
using Jellyfin.Plugin.EmailDigest.Subscription;
using Jellyfin.Plugin.EmailDigest.Tests.Delivery;
using Jellyfin.Plugin.EmailDigest.UserApi;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Jellyfin.Plugin.EmailDigest.Tests.UserApi;

/// <summary>
/// The plugin's real controllers behind the real ASP.NET Core pipeline (routing, authentication,
/// authorization, model binding, filters), with a stand-in for Jellyfin's token authentication.
/// </summary>
/// <remarks>
/// What this proves: the attributes on the plugin's controllers produce the intended behaviour for an
/// anonymous caller, a normal user, an administrator and an API key, given claims shaped exactly as
/// Jellyfin's <c>CustomAuthenticationHandler</c> builds them (read from its 10.11.8 source) and policies
/// defined as Jellyfin defines them. What it cannot prove: that Jellyfin's own handler produces those
/// claims from a real token, rejects a disabled user's token, or that its default policy lets a normal user
/// through. Those are checked against a running server (docs/phase7-live-verification.md).
/// </remarks>
internal sealed class PipelineHost : IAsyncDisposable
{
    public const string Scheme = "TestJellyfin";

    private readonly TestServer _server;
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "EmailDigestPipeline_" + Guid.NewGuid());

    public PipelineHost(ISubscriptionService service)
    {
        Directory.CreateDirectory(_folder);
        var time = new RunnerHarness.ManualTime(new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero));
        Time = time;
        var builder = new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddControllers().AddApplicationPart(typeof(EmailDigestUserController).Assembly);
                services.AddSingleton(service);
                services.AddSingleton<ILocalizationService>(new LocalizationService());
                services.AddSingleton(new UserRequestThrottle(time));

                var config = new RunnerHarness.FixedConfig(new PluginConfiguration());
                services.AddSingleton<ISmtpConfigurationAccessor>(config);
                services.AddSingleton(new SmtpAdminService(config, new SmtpPasswordStore(_folder, () => null), new RunnerHarness.FakeTransport(), new LocalizationService(), time, NullLogger<SmtpAdminService>.Instance));
                services.AddSingleton(new LibraryAdminService(new Mock<ILibraryManager>().Object, config, NullLogger<LibraryAdminService>.Instance));
                services.AddSingleton(new StatusAdminService(config, new RunSummaryStore(_folder), new UserPreferenceRepository(_folder)));

                services.AddAuthentication(Scheme).AddScheme<AuthenticationSchemeOptions, StandInAuthenticationHandler>(Scheme, _ => { });
                services.AddAuthorization(options =>
                {
                    // Jellyfin 10.11.8 (ApiServiceCollectionExtensions): the default policy needs a
                    // signed-in user; RequiresElevation needs the Administrator role claim.
                    options.DefaultPolicy = new AuthorizationPolicyBuilder(Scheme).RequireAuthenticatedUser().Build();
                    options.AddPolicy(Policies.RequiresElevation, policy => policy.AddAuthenticationSchemes(Scheme).RequireClaim(ClaimTypes.Role, "Administrator"));
                });
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseEndpoints(endpoints => endpoints.MapControllers());
            });
        _server = new TestServer(builder);
    }

    public RunnerHarness.ManualTime Time { get; }

    public HttpClient Client() => _server.CreateClient();

    public static HttpRequestMessage Request(HttpMethod method, string path, Guid? user = null, string role = "User", bool apiKey = false, string? body = null, string contentType = "application/json")
    {
        var request = new HttpRequestMessage(method, path);
        if (user is not null || apiKey)
        {
            request.Headers.Add(StandInAuthenticationHandler.UserHeader, user?.ToString("N") ?? string.Empty);
            request.Headers.Add(StandInAuthenticationHandler.RoleHeader, role);
            request.Headers.Add(StandInAuthenticationHandler.ApiKeyHeader, apiKey ? "True" : "False");
        }

        if (body is not null)
        {
            request.Content = new StringContent(body, System.Text.Encoding.UTF8, contentType);
        }

        return request;
    }

    public async ValueTask DisposeAsync()
    {
        _server.Dispose();
        await Task.Yield();
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    /// <summary>
    /// Builds a principal from test-only headers, shaped like Jellyfin's handler output. It reads nothing
    /// else: in particular it ignores cookies, query strings and bodies, as Jellyfin's does.
    /// </summary>
    private sealed class StandInAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string UserHeader = "X-Test-UserId";
        public const string RoleHeader = "X-Test-Role";
        public const string ApiKeyHeader = "X-Test-ApiKey";

        public StandInAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(RoleHeader, out var role))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = new[]
            {
                new Claim(ClaimTypes.Name, "stand-in"),
                new Claim(ClaimTypes.Role, role.ToString()),
                new Claim("Jellyfin-UserId", Request.Headers[UserHeader].ToString()),
                new Claim("Jellyfin-IsApiKey", Request.Headers[ApiKeyHeader].ToString())
            };
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
