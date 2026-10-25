using System;
using System.Linq;
using System.Net.Mime;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.EmailDigest.Admin;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.EmailDigest.Tests.Admin;

/// <summary>
/// Structural guarantees of the administrator API. Authorization is declared with attributes that
/// Jellyfin's pipeline enforces, so these tests pin the declarations; the enforcement itself must
/// be confirmed on a live server (see docs).
/// </summary>
public sealed class EmailDigestAdminControllerTests
{
    private static readonly Type ControllerType = typeof(EmailDigestAdminController);

    private static MethodInfo[] Actions()
    {
        return ControllerType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
    }

    [Fact]
    public void Controller_RequiresTheAdministratorPolicy()
    {
        var authorize = ControllerType.GetCustomAttributes<AuthorizeAttribute>(inherit: true).ToList();

        var attribute = Assert.Single(authorize);
        Assert.Equal(Policies.RequiresElevation, attribute.Policy);
        Assert.Equal("RequiresElevation", attribute.Policy);
    }

    [Fact]
    public void Controller_AllowsNoAnonymousAccessAnywhere()
    {
        Assert.Empty(ControllerType.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true));
        Assert.All(Actions(), action => Assert.Empty(action.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true)));
    }

    [Fact]
    public void Controller_OverridesNoActionsAuthorizationWithWeakerPolicy()
    {
        Assert.All(Actions(), action => Assert.Empty(action.GetCustomAttributes<AuthorizeAttribute>(inherit: true)));
    }

    [Fact]
    public void Controller_IsAnApiControllerUnderItsOwnRoute()
    {
        Assert.NotEmpty(ControllerType.GetCustomAttributes<ApiControllerAttribute>());
        Assert.Equal("EmailDigest/Admin", Assert.Single(ControllerType.GetCustomAttributes<RouteAttribute>()).Template);
    }

    [Fact]
    public void EveryPublicActionHasExactlyOneHttpVerb()
    {
        Assert.NotEmpty(Actions());
        Assert.All(Actions(), action => Assert.Single(action.GetCustomAttributes<HttpMethodAttribute>()));
    }

    [Fact]
    public void ActionsThatChangeStateNeverUseGet_AndBodyActionsAcceptOnlyJson()
    {
        foreach (var action in Actions())
        {
            var verb = Assert.Single(action.GetCustomAttributes<HttpMethodAttribute>());
            var changesState = action.Name is "UpdateLibraries" or "UpdateSettings" or "SetPassword" or "ClearPassword" or "TestConnection" or "SendTestEmail";
            if (changesState)
            {
                Assert.DoesNotContain("GET", verb.HttpMethods);
            }

            if (action.GetParameters().Any(p => p.GetCustomAttributes<FromBodyAttribute>().Any()))
            {
                var consumes = Assert.Single(action.GetCustomAttributes<ConsumesAttribute>());
                Assert.Equal(new[] { MediaTypeNames.Application.Json }, consumes.ContentTypes);
            }
        }
    }

    [Fact]
    public void Password_IsOnlyAcceptedInTheBody_NeverInTheUrl()
    {
        var set = ControllerType.GetMethod("SetPassword")!;
        var routeOrQueryParameters = set.GetParameters().Where(p => p.GetCustomAttributes<FromQueryAttribute>().Any() || p.GetCustomAttributes<FromRouteAttribute>().Any());

        Assert.Empty(routeOrQueryParameters);
        Assert.Contains(set.GetParameters(), p => p.GetCustomAttributes<FromBodyAttribute>().Any());

        // No route template carries a value placeholder, so nothing can be passed in the URL.
        Assert.All(Actions(), action => Assert.DoesNotContain("{", Assert.Single(action.GetCustomAttributes<HttpMethodAttribute>()).Template ?? string.Empty, StringComparison.Ordinal));
    }

    [Fact]
    public void ResponsesAreNotCacheable()
    {
        var cache = Assert.Single(ControllerType.GetCustomAttributes<ResponseCacheAttribute>());
        Assert.True(cache.NoStore);
    }

    [Fact]
    public void NoActionExposesTheStoreOrRawPasswordReads()
    {
        Assert.DoesNotContain(Actions(), a => a.Name.Contains("ReadPassword", StringComparison.Ordinal) || a.Name.Contains("GetPassword", StringComparison.Ordinal));
        Assert.DoesNotContain(ControllerType.GetConstructors().SelectMany(c => c.GetParameters()), p => p.ParameterType.Name == "ISmtpPasswordStore");
    }

    [Fact]
    public async Task UnexpectedFailure_ReturnsAGenericBodyWithoutTheExceptionMessage()
    {
        var configuration = new Moq.Mock<ISmtpConfigurationAccessor>();
        configuration.Setup(c => c.GetCopy()).Throws(new InvalidOperationException("boom: password=SUPER-SECRET host=internal"));
        var service = new SmtpAdminService(
            configuration.Object,
            new Moq.Mock<Jellyfin.Plugin.EmailDigest.Services.ISmtpPasswordStore>().Object,
            new Moq.Mock<Jellyfin.Plugin.EmailDigest.Email.IEmailTransport>().Object,
            new Jellyfin.Plugin.EmailDigest.Services.LocalizationService(),
            TimeProvider.System,
            NullLogger<SmtpAdminService>.Instance);
        var controller = new EmailDigestAdminController(service, null!, null!, NullLogger<EmailDigestAdminController>.Instance);

        var result = await controller.GetSettings();

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, objectResult.StatusCode);
        var body = System.Text.Json.JsonSerializer.Serialize(objectResult.Value);
        Assert.DoesNotContain("SUPER-SECRET", body, StringComparison.Ordinal);
        Assert.DoesNotContain("boom", body, StringComparison.Ordinal);
    }
}
