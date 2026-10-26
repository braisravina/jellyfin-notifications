using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.EmailDigest.Subscription;
using Jellyfin.Plugin.EmailDigest.Verification;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.EmailDigest.Tests.UserApi;

/// <summary>
/// The user-facing API through the real pipeline: who may call it, whose data it can touch, and how it
/// refuses everything else. See <see cref="PipelineHost"/> for what this does and does not prove.
/// </summary>
public sealed class UserApiAuthorizationTests : IAsyncDisposable
{
    private static readonly SubscriptionOverview Overview = new(true, "en", "ana@example.com", true, null, null, null, true, true, true, null, null, null, null);

    private readonly Mock<ISubscriptionService> _service = new();
    private readonly PipelineHost _host;
    private readonly HttpClient _client;

    public UserApiAuthorizationTests()
    {
        _service.Setup(s => s.GetOverviewAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(Overview);
        _service.Setup(s => s.SetNotificationsAsync(It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>())).ReturnsAsync(SubscriptionChange.Saved);
        _service.Setup(s => s.SetLanguageAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync(SubscriptionChange.Saved);
        _service.Setup(s => s.CancelPendingAddressAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _service.Setup(s => s.RequestAddressAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync(new AddressRequestResult(AddressRequestStatus.Sent, DateTimeOffset.UnixEpoch));
        _host = new PipelineHost(_service.Object);
        _client = _host.Client();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _host.DisposeAsync();
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, Guid? user = null, string role = "User", bool apiKey = false, string? body = null, string contentType = "application/json")
    {
        return _client.SendAsync(PipelineHost.Request(method, path, user, role, apiKey, body, contentType));
    }

    private void NothingWasCalled()
    {
        _service.VerifyNoOtherCalls();
    }

    public static TheoryData<string, string, string?> MeRoutes() => new()
    {
        { "GET", "/EmailDigest/Me", null },
        { "PUT", "/EmailDigest/Me/Notifications", "{\"enabled\":true}" },
        { "PUT", "/EmailDigest/Me/Language", "{\"language\":\"es\"}" },
        { "POST", "/EmailDigest/Me/Address", "{\"address\":\"a@example.com\"}" },
        { "DELETE", "/EmailDigest/Me/Address/Pending", null }
    };

    // Authentication and identity

    [Theory]
    [MemberData(nameof(MeRoutes))]
    public async Task Anonymous_IsRefused_OnEveryUserRoute_AndNothingIsCalled(string method, string path, string? body)
    {
        var response = await SendAsync(new HttpMethod(method), path, body: body);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        NothingWasCalled();
    }

    [Theory]
    [MemberData(nameof(MeRoutes))]
    public async Task ACookieAlone_IsNotAuthentication(string method, string path, string? body)
    {
        var request = PipelineHost.Request(new HttpMethod(method), path, body: body);
        request.Headers.Add("Cookie", "Jellyfin-UserId=" + Guid.NewGuid().ToString("N") + "; X-Test-Role=Administrator");

        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.SendAsync(request)).StatusCode);
        NothingWasCalled();
    }

    [Fact]
    public async Task SignedInUser_ActsOnlyAsThemselves()
    {
        var ana = Guid.NewGuid();

        var response = await SendAsync(HttpMethod.Get, "/EmailDigest/Me", ana);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        _service.Verify(s => s.GetOverviewAsync(ana, It.IsAny<CancellationToken>()), Times.Once);
        NothingWasCalled();
    }

    [Fact]
    public async Task AUserIdInTheQuery_TheBody_OrAHeader_IsNeverUsed()
    {
        var ana = Guid.NewGuid();
        var bob = Guid.NewGuid();

        await SendAsync(HttpMethod.Get, "/EmailDigest/Me?userId=" + bob + "&UserId=" + bob.ToString("N") + "&id=" + bob, ana);
        await SendAsync(HttpMethod.Put, "/EmailDigest/Me/Notifications?userId=" + bob, ana, body: "{\"enabled\":false,\"userId\":\"" + bob + "\",\"UserId\":\"" + bob + "\"}");
        var spoof = PipelineHost.Request(HttpMethod.Put, "/EmailDigest/Me/Language", ana, body: "{\"language\":\"gl\",\"userId\":\"" + bob + "\"}");
        spoof.Headers.Add("X-Emby-UserId", bob.ToString());
        spoof.Headers.Add("X-User-Id", bob.ToString());
        await _client.SendAsync(spoof);

        _service.Verify(s => s.GetOverviewAsync(ana, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        _service.Verify(s => s.SetNotificationsAsync(ana, false, It.IsAny<CancellationToken>()), Times.Once);
        _service.Verify(s => s.SetLanguageAsync(ana, "gl", It.IsAny<CancellationToken>()), Times.Once);
        _service.Verify(s => s.GetOverviewAsync(bob, It.IsAny<CancellationToken>()), Times.Never);
        _service.Verify(s => s.SetNotificationsAsync(bob, It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        _service.Verify(s => s.SetLanguageAsync(bob, It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TwoUsersInARow_EachGetOnlyTheirOwn()
    {
        var ana = Guid.NewGuid();
        var bob = Guid.NewGuid();

        await SendAsync(HttpMethod.Put, "/EmailDigest/Me/Notifications", ana, body: "{\"enabled\":true}");
        await SendAsync(HttpMethod.Put, "/EmailDigest/Me/Notifications", bob, body: "{\"enabled\":false}");

        _service.Verify(s => s.SetNotificationsAsync(ana, true, It.IsAny<CancellationToken>()), Times.Once);
        _service.Verify(s => s.SetNotificationsAsync(bob, false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AdministratorUser_ManagesOnlyTheirOwnNotifications_NeverSomeoneElses()
    {
        var admin = Guid.NewGuid();
        var other = Guid.NewGuid();

        await SendAsync(HttpMethod.Put, "/EmailDigest/Me/Notifications?userId=" + other, admin, "Administrator", body: "{\"enabled\":false,\"userId\":\"" + other + "\"}");

        _service.Verify(s => s.SetNotificationsAsync(admin, false, It.IsAny<CancellationToken>()), Times.Once);
        _service.Verify(s => s.SetNotificationsAsync(other, It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [MemberData(nameof(MeRoutes))]
    public async Task AnApiKey_IsNotAPerson_AndIsRefusedEverywhere(string method, string path, string? body)
    {
        var response = await SendAsync(new HttpMethod(method), path, user: null, role: "Administrator", apiKey: true, body: body);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        NothingWasCalled();
    }

    [Fact]
    public async Task AnAdministratorApiKey_WithAUserIdSmuggledIn_IsStillRefused()
    {
        var request = PipelineHost.Request(HttpMethod.Get, "/EmailDigest/Me?userId=" + Guid.NewGuid(), Guid.NewGuid(), "Administrator", apiKey: true);

        Assert.Equal(HttpStatusCode.Forbidden, (await _client.SendAsync(request)).StatusCode);
        NothingWasCalled();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-guid")]
    [InlineData("00000000000000000000000000000000")]
    public async Task APrincipalWithoutAUsableUserId_IsRefused(string userId)
    {
        var request = PipelineHost.Request(HttpMethod.Get, "/EmailDigest/Me", Guid.NewGuid());
        request.Headers.Remove("X-Test-UserId");
        request.Headers.Add("X-Test-UserId", userId);

        Assert.Equal(HttpStatusCode.Forbidden, (await _client.SendAsync(request)).StatusCode);
        NothingWasCalled();
    }

    [Fact]
    public async Task AUnavailableUser_DisabledOrRemoved_IsRefused()
    {
        _service.Setup(s => s.GetOverviewAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((SubscriptionOverview?)null);

        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(HttpMethod.Get, "/EmailDigest/Me", Guid.NewGuid())).StatusCode);
    }

    // CSRF defences

    [Theory]
    [InlineData("application/x-www-form-urlencoded")]
    [InlineData("text/plain")]
    public async Task ChangesThatAreNotJson_AreRefused_SoACrossSiteFormCannotSendThem(string contentType)
    {
        var user = Guid.NewGuid();

        foreach (var (method, path) in new[] { ("PUT", "/EmailDigest/Me/Notifications"), ("PUT", "/EmailDigest/Me/Language"), ("POST", "/EmailDigest/Me/Address") })
        {
            var response = await SendAsync(new HttpMethod(method), path, user, body: "enabled=true&language=es&address=a@example.com", contentType: contentType);
            Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        }

        NothingWasCalled();
    }

    [Fact]
    public async Task NoChangeIsPossibleWithAGet()
    {
        var user = Guid.NewGuid();

        foreach (var path in new[] { "/EmailDigest/Me/Notifications?enabled=false", "/EmailDigest/Me/Language?language=es", "/EmailDigest/Me/Address?address=a@example.com", "/EmailDigest/Me/Address/Pending" })
        {
            Assert.Equal(HttpStatusCode.MethodNotAllowed, (await SendAsync(HttpMethod.Get, path, user)).StatusCode);
        }

        NothingWasCalled();
    }

    // Behaviour and responses

    [Fact]
    public async Task EveryResponse_IsNotCached_NotSniffed_AndWithholdsTheReferrer()
    {
        var response = await SendAsync(HttpMethod.Get, "/EmailDigest/Me", Guid.NewGuid());

        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
    }

    [Fact]
    public async Task ChangesWithAMissingOrInvalidValue_AreRefused()
    {
        var user = Guid.NewGuid();
        _service.Setup(s => s.SetLanguageAsync(user, "fr", It.IsAny<CancellationToken>())).ReturnsAsync(SubscriptionChange.InvalidValue);

        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(HttpMethod.Put, "/EmailDigest/Me/Notifications", user, body: "{}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(HttpMethod.Put, "/EmailDigest/Me/Notifications", user, body: "{\"enabled\":\"maybe\"}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(HttpMethod.Put, "/EmailDigest/Me/Language", user, body: "{\"language\":\"fr\"}")).StatusCode);
    }

    [Theory]
    [InlineData(AddressRequestStatus.Sent, 202)]
    [InlineData(AddressRequestStatus.SentUncertain, 202)]
    [InlineData(AddressRequestStatus.InvalidAddress, 400)]
    [InlineData(AddressRequestStatus.NotAvailable, 409)]
    [InlineData(AddressRequestStatus.AlreadyVerified, 409)]
    [InlineData(AddressRequestStatus.SendFailed, 502)]
    [InlineData(AddressRequestStatus.UserUnavailable, 403)]
    [InlineData(AddressRequestStatus.StorageError, 503)]
    public async Task AddressRequest_MapsEachResultToAStatus(AddressRequestStatus status, int expected)
    {
        _service.Setup(s => s.RequestAddressAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync(new AddressRequestResult(status));

        var response = await SendAsync(HttpMethod.Post, "/EmailDigest/Me/Address", Guid.NewGuid(), body: "{\"address\":\"a@example.com\"}");

        Assert.Equal(expected, (int)response.StatusCode);
    }

    [Fact]
    public async Task AddressRequest_RateLimited_Is429_WithRetryAfter()
    {
        _service.Setup(s => s.RequestAddressAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AddressRequestResult(AddressRequestStatus.RateLimited, null, RateLimitKind.Cooldown, TimeSpan.FromSeconds(41.2)));

        var response = await SendAsync(HttpMethod.Post, "/EmailDigest/Me/Address", Guid.NewGuid(), body: "{\"address\":\"a@example.com\"}");

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal("42", response.Headers.GetValues("Retry-After").Single());
        Assert.DoesNotContain("example.com", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ManyChangesInAMinute_AreThrottledPerUser_ButReadingIsNot()
    {
        var spammer = Guid.NewGuid();
        var other = Guid.NewGuid();

        for (var i = 0; i < 30; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Put, "/EmailDigest/Me/Notifications", spammer, body: "{\"enabled\":true}")).StatusCode);
        }

        var blocked = await SendAsync(HttpMethod.Put, "/EmailDigest/Me/Notifications", spammer, body: "{\"enabled\":true}");
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.True(blocked.Headers.Contains("Retry-After"));
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Get, "/EmailDigest/Me", spammer)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Put, "/EmailDigest/Me/Notifications", other, body: "{\"enabled\":true}")).StatusCode);

        _host.Time.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Put, "/EmailDigest/Me/Notifications", spammer, body: "{\"enabled\":true}")).StatusCode);
    }

    [Fact]
    public async Task AnUnexpectedFailure_ReturnsAGenericBody_NeverTheMessage()
    {
        _service.Setup(s => s.GetOverviewAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("secret ana@example.com details"));

        var response = await SendAsync(HttpMethod.Get, "/EmailDigest/Me", Guid.NewGuid());

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("secret", body, StringComparison.Ordinal);
        Assert.DoesNotContain("example.com", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CancelPending_ReturnsTheOwnOverview()
    {
        var user = Guid.NewGuid();

        var response = await SendAsync(HttpMethod.Delete, "/EmailDigest/Me/Address/Pending", user);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        _service.Verify(s => s.CancelPendingAddressAsync(user, It.IsAny<CancellationToken>()), Times.Once);
    }
}
