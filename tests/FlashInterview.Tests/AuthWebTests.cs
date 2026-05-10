extern alias FlashInterviewWeb;

using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Http;
using FlashInterview.Application.Auth;
using FlashInterview.Application.SensitiveWords;
using FlashInterviewWeb::FlashInterview.Web.Auth;
using FlashInterviewWeb::FlashInterview.Web.Clients;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace FlashInterview.Tests;

public sealed class AuthWebTests
{
    private const string AdminApiKey = "web-auth-admin-key";

    [Fact]
    public async Task Admin_RedirectsUnauthenticatedUsersToLogin()
    {
        await using var factory = new AuthWebFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.GetAsync("/Admin");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account/Login", response.Headers.Location?.AbsolutePath);
    }

    [Fact]
    public async Task PostingValidCredentials_SignsInLocalCookieAndAllowsAdmin()
    {
        await using var factory = new AuthWebFactory();
        using var client = CreateHttpsClient(factory);
        var token = await GetAntiForgeryTokenAsync(client, "/Account/Login");

        using var response = await client.PostAsync(
            "/Account/Login",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Email"] = "admin@example.test",
                ["Password"] = "Correct_password123!",
                ["ReturnUrl"] = "/Admin",
                ["__RequestVerificationToken"] = token
            }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Admin", response.Headers.Location?.OriginalString);

        using var adminResponse = await client.GetAsync("/Admin");
        Assert.Equal(HttpStatusCode.OK, adminResponse.StatusCode);
        Assert.Contains(
            AdminApiKey,
            factory.AuthHandler.Requests
                .Where(request => request.RequestUri?.AbsolutePath == "/api/auth/login")
                .SelectMany(request => request.Headers.TryGetValues("X-Admin-Api-Key", out var values) ? values : []));
    }

    [Fact]
    public async Task Logout_ClearsAdminAccess()
    {
        await using var factory = new AuthWebFactory();
        using var client = CreateHttpsClient(factory);
        await SignInAsync(client);
        using var adminBeforeLogout = await client.GetAsync("/Admin");
        var token = ExtractAntiForgeryToken(await adminBeforeLogout.Content.ReadAsStringAsync());

        using var logoutResponse = await client.PostAsync(
            "/Account/Logout",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token
            }));
        using var adminAfterLogout = await client.GetAsync("/Admin");

        Assert.Equal(HttpStatusCode.Redirect, logoutResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, adminAfterLogout.StatusCode);
        Assert.Equal("/Account/Login", adminAfterLogout.Headers.Location?.AbsolutePath);
    }

    [Fact]
    public async Task Users_RedirectsUnauthenticatedUsersToLogin()
    {
        await using var factory = new AuthWebFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.GetAsync("/Users");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account/Login", response.Headers.Location?.AbsolutePath);
    }

    [Fact]
    public async Task Users_ForbidsAuthenticatedAdminWithoutSuperAdminRole()
    {
        await using var factory = new AuthWebFactory();
        using var client = CreateHttpsClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/Users");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account/AccessDenied", response.Headers.Location?.AbsolutePath);
    }

    [Fact]
    public async Task Users_RendersSuperAdminUserManagementFromAuthApi()
    {
        await using var factory = new AuthWebFactory(authenticatedRoles: [ApplicationRoles.SuperAdmin]);
        using var client = CreateHttpsClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/Users");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("managed@example.test", html);
        Assert.Contains(
            AdminApiKey,
            factory.AuthHandler.Requests
                .Where(request => request.RequestUri?.AbsolutePath == "/api/users")
                .SelectMany(request => request.Headers.TryGetValues("X-Admin-Api-Key", out var values) ? values : []));
    }

    [Fact]
    public async Task Users_CreatePostsLocalUserToAuthApi()
    {
        await using var factory = new AuthWebFactory(authenticatedRoles: [ApplicationRoles.SuperAdmin]);
        using var client = CreateHttpsClient(factory);
        await SignInAsync(client);
        var token = await GetAntiForgeryTokenAsync(client, "/Users/Create");

        using var response = await client.PostAsync(
            "/Users/Create",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Email"] = "created@example.test",
                ["DisplayName"] = "Created User",
                ["Password"] = "Created_password123!",
                ["IsAdmin"] = "true",
                ["__RequestVerificationToken"] = token
            }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Users", response.Headers.Location?.OriginalString);
        var createRequestBody = Assert.Single(
            factory.AuthHandler.RequestBodies,
            body => body.Path == "/api/users").Body;
        Assert.Contains("\"email\":\"created@example.test\"", createRequestBody);
        Assert.Contains("\"displayName\":\"Created User\"", createRequestBody);
        Assert.Contains("\"password\":\"Created_password123!\"", createRequestBody);
        Assert.Contains("\"isAdmin\":true", createRequestBody);
    }

    [Fact]
    public async Task Users_UpdateAdminRoleRejectsMalformedBooleanWithoutRenderingCheckboxException()
    {
        await using var factory = new AuthWebFactory(authenticatedRoles: [ApplicationRoles.SuperAdmin]);
        using var client = CreateHttpsClient(factory);
        await SignInAsync(client);
        var token = await GetAntiForgeryTokenAsync(client, "/Users");

        using var response = await client.PostAsync(
            "/Users/UpdateAdminRole",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Id"] = "managed-user-1",
                ["Email"] = "managed@example.test",
                ["DisplayName"] = "Managed User",
                ["IsLockedOut"] = "false",
                ["IsSuperAdmin"] = "false",
                ["RoleSummary"] = "Admin",
                ["IsAdmin"] = "value",
                ["__RequestVerificationToken"] = token
            }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(
            factory.AuthHandler.Requests,
            request => request.RequestUri?.AbsolutePath == "/api/users/managed-user-1/roles/admin");
    }

    [Fact]
    public async Task Users_UpdateAdminRolePostsBooleanToAuthApi()
    {
        await using var factory = new AuthWebFactory(authenticatedRoles: [ApplicationRoles.SuperAdmin]);
        using var client = CreateHttpsClient(factory);
        await SignInAsync(client);
        var token = await GetAntiForgeryTokenAsync(client, "/Users");

        using var response = await client.PostAsync(
            "/Users/UpdateAdminRole",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Id"] = "managed-user-1",
                ["Email"] = "managed@example.test",
                ["DisplayName"] = "Managed User",
                ["IsLockedOut"] = "false",
                ["IsSuperAdmin"] = "false",
                ["RoleSummary"] = "Admin",
                ["IsAdmin"] = "false",
                ["__RequestVerificationToken"] = token
            }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Users", response.Headers.Location?.OriginalString);
        var roleRequestBody = Assert.Single(
            factory.AuthHandler.RequestBodies,
            body => body.Path == "/api/users/managed-user-1/roles/admin").Body;
        Assert.Contains("\"isAdmin\":false", roleRequestBody);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("google-client-id", null)]
    [InlineData(null, "google-client-secret")]
    public async Task Login_HidesGoogleButtonWhenClientIdOrSecretIsMissing(string? clientId, string? clientSecret)
    {
        await using var factory = new AuthWebFactory(new Dictionary<string, string?>
        {
            ["Authentication:Google:ClientId"] = clientId,
            ["Authentication:Google:ClientSecret"] = clientSecret
        });
        using var client = CreateHttpsClient(factory);

        using var response = await client.GetAsync("/Account/Login");

        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Continue with Google", html);
    }

    [Fact]
    public async Task Login_ShowsGoogleButtonWhenClientIdAndSecretAreConfigured()
    {
        await using var factory = new AuthWebFactory(new Dictionary<string, string?>
        {
            ["Authentication:Google:ClientId"] = "google-client-id",
            ["Authentication:Google:ClientSecret"] = "google-client-secret"
        });
        using var client = CreateHttpsClient(factory);

        using var response = await client.GetAsync("/Account/Login");

        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Continue with Google", html);
    }

    [Fact]
    public async Task ExternalLoginCallback_SignsInGoogleUserThroughAuthApi()
    {
        await using var factory = new AuthWebFactory(new Dictionary<string, string?>
        {
            ["Authentication:Google:ClientId"] = "google-client-id",
            ["Authentication:Google:ClientSecret"] = "google-client-secret"
        });
        using var client = CreateHttpsClient(factory);
        client.DefaultRequestHeaders.Add(
            "Cookie",
            $"__Host-FlashInterview.Web.External={CreateExternalLoginCookie(factory)}");

        using var response = await client.GetAsync("/Account/ExternalLoginCallback?returnUrl=/Admin");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Admin", response.Headers.Location?.OriginalString);
        var signInRequestBody = Assert.Single(
            factory.AuthHandler.RequestBodies,
            body => body.Path == "/api/auth/external-login/sign-in").Body;
        Assert.Contains("\"email\":\"admin@example.test\"", signInRequestBody);
        Assert.Contains("\"provider\":\"Google\"", signInRequestBody);
        Assert.Contains("\"providerKey\":\"google-user-1\"", signInRequestBody);
        Assert.Contains("\"emailVerified\":true", signInRequestBody);
    }

    [Fact]
    public async Task ExternalLoginCallback_AcceptsGoogleVerifiedEmailClaim()
    {
        await using var factory = new AuthWebFactory(
            new Dictionary<string, string?>
            {
                ["Authentication:Google:ClientId"] = "google-client-id",
                ["Authentication:Google:ClientSecret"] = "google-client-secret"
            },
            externalEmailVerifiedClaimType: "verified_email");
        using var client = CreateHttpsClient(factory);
        client.DefaultRequestHeaders.Add(
            "Cookie",
            $"__Host-FlashInterview.Web.External={CreateExternalLoginCookie(factory)}");

        using var response = await client.GetAsync("/Account/ExternalLoginCallback?returnUrl=/Admin");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Admin", response.Headers.Location?.OriginalString);
        var signInRequestBody = Assert.Single(
            factory.AuthHandler.RequestBodies,
            body => body.Path == "/api/auth/external-login/sign-in").Body;
        Assert.Contains("\"emailVerified\":true", signInRequestBody);
    }

    [Fact]
    public async Task ExternalLoginCallback_ReturnsLoginErrorWhenGoogleEmailIsUnverified()
    {
        await using var factory = new AuthWebFactory(
            new Dictionary<string, string?>
            {
                ["Authentication:Google:ClientId"] = "google-client-id",
                ["Authentication:Google:ClientSecret"] = "google-client-secret"
            },
            externalEmailVerified: false);
        using var client = CreateHttpsClient(factory);
        client.DefaultRequestHeaders.Add(
            "Cookie",
            $"__Host-FlashInterview.Web.External={CreateExternalLoginCookie(factory)}");

        using var response = await client.GetAsync("/Account/ExternalLoginCallback?returnUrl=/Admin");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Google must return a verified email address.", html);
        Assert.DoesNotContain(
            factory.AuthHandler.Requests,
            request => request.RequestUri?.AbsolutePath == "/api/auth/external-login/sign-in");
    }

    [Fact]
    public void DevelopmentCookies_AreCompatibleWithLocalHttpOAuthCallbacks()
    {
        using var factory = new AuthWebFactory(environment: "Development");

        var cookieOptions = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>();
        var authCookie = cookieOptions.Get(CookieAuthenticationDefaults.AuthenticationScheme);
        var externalCookie = cookieOptions.Get("FlashInterview.External");

        Assert.Equal("FlashInterview.Web.Auth", authCookie.Cookie.Name);
        Assert.Equal(CookieSecurePolicy.SameAsRequest, authCookie.Cookie.SecurePolicy);
        Assert.Equal("FlashInterview.Web.External", externalCookie.Cookie.Name);
        Assert.Equal(CookieSecurePolicy.SameAsRequest, externalCookie.Cookie.SecurePolicy);
    }

    [Theory]
    [InlineData("email_verified", true, "true")]
    [InlineData("email_verified", false, "false")]
    [InlineData("verified_email", true, "true")]
    public void GoogleEmailVerificationClaims_AddsBooleanVerifiedEmailClaim(
        string jsonKey,
        bool jsonValue,
        string expectedClaimValue)
    {
        using var document = JsonDocument.Parse($$"""{ "{{jsonKey}}": {{jsonValue.ToString().ToLowerInvariant()}} }""");
        var identity = new ClaimsIdentity();

        GoogleEmailVerificationClaims.AddVerifiedEmailClaim(identity, document.RootElement);

        var claim = Assert.Single(identity.Claims, claim => claim.Type == "email_verified");
        Assert.Equal(expectedClaimValue, claim.Value);
        Assert.Equal(ClaimValueTypes.Boolean, claim.ValueType);
    }

    private static HttpClient CreateHttpsClient(AuthWebFactory factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.BaseAddress = new Uri("https://localhost");
        return client;
    }

    private static async Task SignInAsync(HttpClient client)
    {
        var token = await GetAntiForgeryTokenAsync(client, "/Account/Login");
        using var response = await client.PostAsync(
            "/Account/Login",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Email"] = "admin@example.test",
                ["Password"] = "Correct_password123!",
                ["ReturnUrl"] = "/Admin",
                ["__RequestVerificationToken"] = token
            }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    private static async Task<string> GetAntiForgeryTokenAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        response.EnsureSuccessStatusCode();
        return ExtractAntiForgeryToken(await response.Content.ReadAsStringAsync());
    }

    private static string ExtractAntiForgeryToken(string html)
    {
        var match = Regex.Match(
            html,
            "name=\"__RequestVerificationToken\"[^>]*value=\"(?<token>[^\"]+)\"",
            RegexOptions.IgnoreCase);

        Assert.True(match.Success, "Expected an antiforgery token in the rendered page.");
        return WebUtility.HtmlDecode(match.Groups["token"].Value);
    }

    private static string CreateExternalLoginCookie(AuthWebFactory factory)
    {
        var options = factory.Services
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get("FlashInterview.External");
        Assert.NotNull(options.TicketDataFormat);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "google-user-1"),
            new Claim(ClaimTypes.Email, "admin@example.test"),
            new Claim(ClaimTypes.Name, "Admin User"),
            new Claim(factory.ExternalEmailVerifiedClaimType, factory.ExternalEmailVerified ? "true" : "false")
        };
        var properties = new AuthenticationProperties();
        properties.Items["LoginProvider"] = GoogleDefaults.AuthenticationScheme;
        var ticket = new AuthenticationTicket(
            new ClaimsPrincipal(new ClaimsIdentity(claims, GoogleDefaults.AuthenticationScheme)),
            properties,
            "FlashInterview.External");

        return options.TicketDataFormat.Protect(ticket);
    }

    private static HttpResponseMessage JsonResponse(string json, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class AuthWebFactory(
        IReadOnlyDictionary<string, string?>? configurationValues = null,
        string[]? authenticatedRoles = null,
        bool externalEmailVerified = true,
        string externalEmailVerifiedClaimType = "email_verified",
        string environment = "Production")
        : WebApplicationFactory<FlashInterviewWeb::Program>
    {
        private readonly string[] authenticatedRoles = authenticatedRoles ?? [ApplicationRoles.Admin];

        public bool ExternalEmailVerified { get; } = externalEmailVerified;

        public string ExternalEmailVerifiedClaimType { get; } = externalEmailVerifiedClaimType;

        public RecordingHandler AuthHandler { get; } = new(request =>
        {
            if (request.RequestUri?.AbsolutePath == "/api/auth/login")
            {
                return JsonResponse(
                    $$"""
                    {
                      "id":"user-1",
                      "email":"admin@example.test",
                      "displayName":"Admin User",
                      "roles":{{JsonSerializer.Serialize(authenticatedRoles ?? [ApplicationRoles.Admin])}}
                    }
                    """);
            }

            if (request.RequestUri?.AbsolutePath == "/api/auth/external-login/sign-in")
            {
                return JsonResponse(
                    $$"""
                    {
                      "id":"user-1",
                      "email":"admin@example.test",
                      "displayName":"Admin User",
                      "roles":{{JsonSerializer.Serialize(authenticatedRoles ?? [ApplicationRoles.Admin])}}
                    }
                    """);
            }

            if (request.RequestUri?.AbsolutePath == "/api/users" && request.Method == HttpMethod.Post)
            {
                return JsonResponse(
                    """
                    {
                      "id":"created-user-1",
                      "email":"created@example.test",
                      "displayName":"Created User",
                      "isLockedOut":false,
                      "roles":["Admin"]
                    }
                    """,
                    HttpStatusCode.Created);
            }

            if (request.RequestUri?.AbsolutePath == "/api/users")
            {
                return JsonResponse(
                    """
                    [
                      {
                        "id":"managed-user-1",
                        "email":"managed@example.test",
                        "displayName":"Managed User",
                        "isLockedOut":false,
                        "roles":["Admin"]
                      }
                    ]
                    """);
            }

            if (request.RequestUri?.AbsolutePath == "/api/users/managed-user-1/roles/admin")
            {
                return JsonResponse(
                    """
                    {
                      "id":"managed-user-1",
                      "email":"managed@example.test",
                      "displayName":"Managed User",
                      "isLockedOut":false,
                      "roles":["Admin"]
                    }
                    """);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        private RecordingHandler SensitiveWordsHandler { get; } = new(_ => JsonResponse(
            """
            {"items":[],"page":1,"pageSize":50,"total":0}
            """));

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environment);
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                var values = new Dictionary<string, string?>
                {
                    ["SensitiveWordsApi:BaseUrl"] = "https://api.example.test/",
                    ["SensitiveWordsApi:AdminApiKey"] = AdminApiKey,
                    ["AuthApi:BaseUrl"] = "https://api.example.test/",
                    ["AuthApi:AdminApiKey"] = AdminApiKey
                };

                if (configurationValues is not null)
                {
                    foreach (var (key, value) in configurationValues)
                    {
                        values[key] = value;
                    }
                }

                configuration.AddInMemoryCollection(values);
            });
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<AuthApiClient>();
                services.RemoveAll<SensitiveWordsApiClient>();
                services.AddSingleton(_ => new AuthApiClient(
                    new HttpClient(AuthHandler) { BaseAddress = new Uri("https://api.example.test/") },
                    Options.Create(new AuthApiOptions
                    {
                        BaseUrl = "https://api.example.test/",
                        AdminApiKey = AdminApiKey
                    })));
                services.AddSingleton(_ => new SensitiveWordsApiClient(
                    new HttpClient(SensitiveWordsHandler) { BaseAddress = new Uri("https://api.example.test/") },
                    Options.Create(new SensitiveWordsApiOptions
                    {
                        BaseUrl = "https://api.example.test/",
                        AdminApiKey = AdminApiKey
                    })));
            });
        }
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        public List<(string Path, string Body)> RequestBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (request.Content is not null)
            {
                RequestBodies.Add((
                    request.RequestUri?.AbsolutePath ?? "",
                    await request.Content.ReadAsStringAsync(cancellationToken)));
            }

            return respond(request);
        }
    }
}
