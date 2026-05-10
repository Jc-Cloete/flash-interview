using System.Net;
using System.Net.Http.Json;
using FlashInterview.Application.Auth;
using Microsoft.Extensions.Options;

namespace FlashInterview.Web.Clients;

public sealed class AuthApiOptions
{
    public const string SectionName = "AuthApi";

    public string? BaseUrl { get; set; }

    public string? AdminApiKey { get; set; }
}

public sealed class AuthApiClient(
    HttpClient httpClient,
    IOptions<AuthApiOptions> options)
{
    private const string AdminApiKeyHeaderName = "X-Admin-Api-Key";

    private readonly AuthApiOptions options = options.Value;

    public async Task<AuthenticatedUserDto?> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        using var httpRequest = CreateAdminRequest(HttpMethod.Post, "api/auth/login");
        httpRequest.Content = JsonContent.Create(request);

        using var response = await httpClient.SendAsync(httpRequest, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AuthenticatedUserDto>(cancellationToken);
    }

    public async Task<AuthenticatedUserDto?> ExternalSignInAsync(
        ExternalLoginRequest request,
        CancellationToken cancellationToken)
    {
        using var httpRequest = CreateAdminRequest(HttpMethod.Post, "api/auth/external-login/sign-in");
        httpRequest.Content = JsonContent.Create(request);

        using var response = await httpClient.SendAsync(httpRequest, cancellationToken);
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Conflict)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AuthenticatedUserDto>(cancellationToken);
    }

    public async Task<IReadOnlyList<UserListItemDto>> ListUsersAsync(CancellationToken cancellationToken)
    {
        using var httpRequest = CreateAdminRequest(HttpMethod.Get, "api/users");
        using var response = await httpClient.SendAsync(httpRequest, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<IReadOnlyList<UserListItemDto>>(cancellationToken) ?? [];
    }

    public async Task<UserListItemDto?> CreateUserAsync(
        CreateUserRequest request,
        CancellationToken cancellationToken)
    {
        using var httpRequest = CreateAdminRequest(HttpMethod.Post, "api/users");
        httpRequest.Content = JsonContent.Create(request);

        using var response = await httpClient.SendAsync(httpRequest, cancellationToken);
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Conflict)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<UserListItemDto>(cancellationToken);
    }

    public async Task<UserListItemDto?> UpdateAdminRoleAsync(
        string userId,
        UserRoleUpdateRequest request,
        CancellationToken cancellationToken)
    {
        using var httpRequest = CreateAdminRequest(HttpMethod.Put, $"api/users/{Uri.EscapeDataString(userId)}/roles/admin");
        httpRequest.Content = JsonContent.Create(request);

        using var response = await httpClient.SendAsync(httpRequest, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<UserListItemDto>(cancellationToken);
    }

    private HttpRequestMessage CreateAdminRequest(HttpMethod method, string uri)
    {
        var request = new HttpRequestMessage(method, uri);
        if (!string.IsNullOrWhiteSpace(options.AdminApiKey))
        {
            request.Headers.Add(AdminApiKeyHeaderName, options.AdminApiKey);
        }

        return request;
    }
}
