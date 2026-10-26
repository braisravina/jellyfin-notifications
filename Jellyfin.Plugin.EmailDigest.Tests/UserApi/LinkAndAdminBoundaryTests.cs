using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.EmailDigest.Admin;
using Jellyfin.Plugin.EmailDigest.Subscription;
using Jellyfin.Plugin.EmailDigest.UserApi;
using Jellyfin.Plugin.EmailDigest.Verification;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.EmailDigest.Tests.UserApi;

public sealed class LinkAndAdminBoundaryTests : IAsyncDisposable
{
    private readonly Mock<ISubscriptionService> _service = new();
    private readonly PipelineHost _host;
    private readonly HttpClient _client;

    public LinkAndAdminBoundaryTests()
    {
        _host = new PipelineHost(_service.Object);
        _client = _host.Client();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _host.DisposeAsync();
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, Guid? user = null, string role = "User", string? body = null, string contentType = "application/json")
    {
        return _client.SendAsync(PipelineHost.Request(method, path, user, role, false, body, contentType));
    }

    // Anonymous link actions

    [Fact]
    public async Task Verify_NeedsNoSignIn_AndConfirmsWithTheTokenFromTheQuery()
    {
        _service.Setup(s => s.ConfirmAddressAsync("tok", It.IsAny<CancellationToken>())).ReturnsAsync(ConfirmOutcome.Confirmed);

        var response = await SendAsync(HttpMethod.Post, "/EmailDigest/Verify?token=tok");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("{\"result\":\"confirmed\"}", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(ConfirmOutcome.Invalid, 400)]
    [InlineData(ConfirmOutcome.NotApplied, 409)]
    public async Task Verify_RefusalsCarryNoDetail(ConfirmOutcome outcome, int status)
    {
        _service.Setup(s => s.ConfirmAddressAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync(outcome);

        var response = await SendAsync(HttpMethod.Post, "/EmailDigest/Verify?token=x");

        Assert.Equal(status, (int)response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(body is "{\"error\":\"invalid-link\"}" or "{\"error\":\"not-applied\"}");
    }

    [Fact]
    public async Task Verify_TheTokenIsOnlyTakenFromTheQuery_NotTheBodyOrHeaders()
    {
        _service.Setup(s => s.ConfirmAddressAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync(ConfirmOutcome.Invalid);
        var request = PipelineHost.Request(HttpMethod.Post, "/EmailDigest/Verify", body: "{\"token\":\"from-body\"}");
        request.Headers.Add("X-Token", "from-header");

        await _client.SendAsync(request);

        _service.Verify(s => s.ConfirmAddressAsync(null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Unsubscribe_OneClickFormPost_FromAMailClient_Works()
    {
        _service.Setup(s => s.UnsubscribeAsync("tok.sig", It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var response = await SendAsync(HttpMethod.Post, "/EmailDigest/Unsubscribe?token=tok.sig", body: "List-Unsubscribe=One-Click", contentType: "application/x-www-form-urlencoded");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("{\"result\":\"unsubscribed\"}", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Unsubscribe_InvalidToken_IsAGenericRefusal()
    {
        _service.Setup(s => s.UnsubscribeAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var response = await SendAsync(HttpMethod.Post, "/EmailDigest/Unsubscribe?token=forged");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("{\"error\":\"invalid-link\"}", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Unsubscribe_Response_ContainsNoAccountInformation()
    {
        _service.Setup(s => s.UnsubscribeAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var response = await SendAsync(HttpMethod.Post, "/EmailDigest/Unsubscribe?token=t");

        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("@", body, StringComparison.Ordinal);
        Assert.DoesNotContain("language", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("email", body, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("/EmailDigest/Verify?token=t")]
    [InlineData("/EmailDigest/Unsubscribe?token=t")]
    public async Task ALinkIsNeverActedOnByAGet_ItOnlyShowsAPageWithAButton(string path)
    {
        var response = await SendAsync(HttpMethod.Get, path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("text/html", response.Content.Headers.ContentType?.ToString(), StringComparison.Ordinal);
        _service.Verify(s => s.ConfirmAddressAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        _service.Verify(s => s.UnsubscribeAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LinkResponses_AreNotCached_AndWithholdTheReferrer()
    {
        var response = await SendAsync(HttpMethod.Post, "/EmailDigest/Unsubscribe?token=t");

        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
    }

    // Administrator boundary

    public static TheoryData<string, string> AdminRoutes() => new()
    {
        { "GET", "/EmailDigest/Admin/Settings" },
        { "PUT", "/EmailDigest/Admin/Settings" },
        { "PUT", "/EmailDigest/Admin/Password" },
        { "DELETE", "/EmailDigest/Admin/Password" },
        { "POST", "/EmailDigest/Admin/TestConnection" },
        { "POST", "/EmailDigest/Admin/TestEmail" },
        { "GET", "/EmailDigest/Admin/Strings" },
        { "GET", "/EmailDigest/Admin/Libraries" },
        { "PUT", "/EmailDigest/Admin/Libraries" },
        { "GET", "/EmailDigest/Admin/Status" }
    };

    [Fact]
    public void TheAdminRouteListCoversEveryAdminAction()
    {
        var actions = typeof(EmailDigestAdminController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Count(m => m.GetCustomAttributes<HttpMethodAttribute>().Any());

        Assert.Equal(actions, AdminRoutes().Count);
    }

    [Theory]
    [MemberData(nameof(AdminRoutes))]
    public async Task Admin_Anonymous_IsUnauthorized(string method, string path)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(new HttpMethod(method), path, body: "{}")).StatusCode);
    }

    [Theory]
    [MemberData(nameof(AdminRoutes))]
    public async Task Admin_NormalUser_IsForbidden_OnEveryRoute(string method, string path)
    {
        var response = await SendAsync(new HttpMethod(method), path, Guid.NewGuid(), "User", "{}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("/EmailDigest/Admin/Settings")]
    [InlineData("/EmailDigest/Admin/Status")]
    [InlineData("/EmailDigest/Admin/Strings")]
    public async Task Admin_Administrator_IsAllowed(string path)
    {
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Get, path, Guid.NewGuid(), "Administrator")).StatusCode);
    }

    [Fact]
    public async Task AdministratorsOwnNotifications_AreNotTheAdminApi_AndTheTwoAreSeparate()
    {
        var admin = Guid.NewGuid();
        _service.Setup(s => s.GetOverviewAsync(admin, It.IsAny<CancellationToken>())).ReturnsAsync(new SubscriptionOverview(false, "en", string.Empty, false, null, null, null, false, false, false, null, null, null, null));

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Get, "/EmailDigest/Me", admin, "Administrator")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(HttpMethod.Get, "/EmailDigest/Admin/Settings", admin, "User")).StatusCode);
    }

    // Structure: pins the whole surface so a new endpoint cannot appear unnoticed

    private static readonly Type[] Controllers = typeof(EmailDigestUserController).Assembly.GetTypes().Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract).ToArray();

    private static MethodInfo[] Actions(Type controller) => controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any()).ToArray();

    [Fact]
    public void ThePluginHasExactlyTheseControllers()
    {
        Assert.Equal(
            new[] { nameof(EmailDigestAdminController), nameof(EmailDigestLinkController), nameof(EmailDigestPageController), nameof(EmailDigestUserController) },
            Controllers.Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void OnlyTheLinkAndPageControllersAreAnonymous_TheLinkOnesWithTwoPostsTheOthersWithFourGets()
    {
        foreach (var controller in Controllers)
        {
            var anonymous = controller.GetCustomAttributes<AllowAnonymousAttribute>(true).Any()
                            || Actions(controller).Any(a => a.GetCustomAttributes<AllowAnonymousAttribute>(true).Any());
            Assert.Equal(controller == typeof(EmailDigestLinkController) || controller == typeof(EmailDigestPageController), anonymous);
        }

        var links = Actions(typeof(EmailDigestLinkController));
        Assert.Equal(2, links.Length);
        Assert.All(links, a => Assert.IsType<HttpPostAttribute>(Assert.Single(a.GetCustomAttributes<HttpMethodAttribute>())));

        var pages = Actions(typeof(EmailDigestPageController));
        Assert.Equal(4, pages.Length);
        Assert.All(pages, a => Assert.IsType<HttpGetAttribute>(Assert.Single(a.GetCustomAttributes<HttpMethodAttribute>())));
    }

    [Fact]
    public void UserAndAdminControllers_AreAuthorized_WithoutAnyAnonymousEscapeHatch()
    {
        var user = typeof(EmailDigestUserController).GetCustomAttribute<AuthorizeAttribute>();
        var admin = typeof(EmailDigestAdminController).GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(user);
        Assert.True(string.IsNullOrEmpty(user!.Policy));
        Assert.Equal(Policies.RequiresElevation, admin!.Policy);
        Assert.All(Actions(typeof(EmailDigestUserController)).Concat(Actions(typeof(EmailDigestAdminController))), a =>
        {
            Assert.Empty(a.GetCustomAttributes<AllowAnonymousAttribute>(true));
            Assert.Empty(a.GetCustomAttributes<AuthorizeAttribute>(true));
        });
    }

    [Fact]
    public void NoUserEndpoint_TakesAUserIdFromTheCaller()
    {
        foreach (var action in Actions(typeof(EmailDigestUserController)))
        {
            Assert.DoesNotContain("{", action.GetCustomAttributes<HttpMethodAttribute>().Single().Template ?? string.Empty, StringComparison.Ordinal);
            Assert.All(action.GetParameters(), p => Assert.DoesNotContain("user", p.Name ?? string.Empty, StringComparison.OrdinalIgnoreCase));
        }

        var requests = new[] { typeof(NotificationsRequest), typeof(LanguageRequest), typeof(AddressRequest) };
        Assert.All(requests, t => Assert.DoesNotContain(t.GetProperties(), p => p.Name.Contains("User", StringComparison.OrdinalIgnoreCase) || p.Name.Contains("Id", StringComparison.Ordinal)));
    }

    [Fact]
    public void EveryGetIsReadOnly_ByName()
    {
        var gets = Controllers.SelectMany(Actions).Where(a => a.GetCustomAttributes<HttpGetAttribute>().Any()).Select(a => a.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();

        Assert.Equal(new[] { "GetLibraries", "GetOverview", "GetSettings", "GetStatus", "GetStrings", "GetStrings", "PreferencesPage", "UnsubscribePage", "VerifyPage" }, gets);
    }

    [Fact]
    public void EveryChangingActionWithABody_AcceptsOnlyJson_ExceptTheLinkPosts()
    {
        foreach (var controller in new[] { typeof(EmailDigestUserController), typeof(EmailDigestAdminController) })
        {
            foreach (var action in Actions(controller))
            {
                var hasBody = action.GetParameters().Any(p => p.GetCustomAttributes<FromBodyAttribute>().Any());
                if (hasBody)
                {
                    Assert.Contains(action.GetCustomAttributes<ConsumesAttribute>(), c => c.ContentTypes.Contains("application/json"));
                }
            }
        }
    }
}
