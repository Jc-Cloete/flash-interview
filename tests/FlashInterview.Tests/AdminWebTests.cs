extern alias FlashInterviewWeb;

using System.Net;
using System.Text;
using FlashInterview.Application.SensitiveWords;
using FlashInterviewWeb::FlashInterview.Web.Clients;
using FlashInterviewWeb::FlashInterview.Web.Controllers;
using FlashInterviewWeb::FlashInterview.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace FlashInterview.Tests;

public sealed class AdminWebTests
{
    private const string AdminApiKey = "web-admin-key";

    [Fact]
    public async Task ApiClient_ListAsync_SendsFilterQueryString()
    {
        using var handler = new RecordingHandler(
            _ => JsonResponse(
                """
                {"items":[],"page":1,"pageSize":50,"total":0}
                """));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.example.test/") };
        var client = CreateClient(httpClient);

        await client.ListAsync(new SensitiveWordQuery("select *", "sql keyword", true), CancellationToken.None);

        Assert.Equal(
            "https://api.example.test/api/sensitive-words?q=select%20*&category=sql%20keyword&isActive=true&page=1&pageSize=50",
            handler.Requests.Single().RequestUri?.AbsoluteUri);
        Assert.Equal(AdminApiKey, handler.Requests.Single().Headers.GetValues("X-Admin-Api-Key").Single());
    }

    [Fact]
    public async Task ApiClient_UpdateAsync_SendsPutRequest()
    {
        var id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        using var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.example.test/") };
        var client = CreateClient(httpClient);

        await client.UpdateAsync(id, new UpdateSensitiveWordRequest("DROP", "sql", false), CancellationToken.None);

        var request = handler.Requests.Single();
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal($"https://api.example.test/api/sensitive-words/{id}", request.RequestUri?.AbsoluteUri);
        Assert.Equal(AdminApiKey, request.Headers.GetValues("X-Admin-Api-Key").Single());
    }

    [Theory]
    [InlineData("create")]
    [InlineData("delete")]
    public async Task ApiClient_AdminWriteRequests_SendConfiguredApiKey(string operation)
    {
        using var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.example.test/") };
        var client = CreateClient(httpClient);

        if (operation == "create")
        {
            await client.CreateAsync(new CreateSensitiveWordRequest("DROP", "sql"), CancellationToken.None);
        }
        else
        {
            await client.DeleteAsync(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), CancellationToken.None);
        }

        Assert.Equal(AdminApiKey, handler.Requests.Single().Headers.GetValues("X-Admin-Api-Key").Single());
    }

    [Fact]
    public async Task ApiClient_MaskAsync_DoesNotSendAdminApiKey()
    {
        using var handler = new RecordingHandler(
            _ => JsonResponse(
                """
                {"originalMessage":"DROP","maskedMessage":"****","matches":[]}
                """));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.example.test/") };
        var client = CreateClient(httpClient);

        await client.MaskAsync(new MaskMessageRequest("DROP"), CancellationToken.None);

        Assert.False(handler.Requests.Single().Headers.Contains("X-Admin-Api-Key"));
    }

    [Fact]
    public async Task ApiClient_CreateAsync_ThrowsValidationExceptionWithProblemDetails()
    {
        using var handler = new RecordingHandler(
            _ => JsonResponse(
                """
                {
                  "type":"https://tools.ietf.org/html/rfc9110#section-15.5.1",
                  "title":"One or more validation errors occurred.",
                  "status":400,
                  "errors":{"Value":["Sensitive word already exists."]}
                }
                """,
                HttpStatusCode.BadRequest));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.example.test/") };
        var client = CreateClient(httpClient);

        var exception = await Assert.ThrowsAsync<ApiValidationException>(
            () => client.CreateAsync(new CreateSensitiveWordRequest("DROP", "sql"), CancellationToken.None));

        Assert.Equal("Sensitive word already exists.", exception.Errors["Value"].Single());
    }

    [Fact]
    public async Task AdminController_Create_AddsApiValidationErrorsToModelState()
    {
        using var handler = new RecordingHandler(
            request => request.Method == HttpMethod.Get
                ? JsonResponse("""{"items":[],"page":1,"pageSize":50,"total":0}""")
                : JsonResponse(
                    """
                    {"title":"Validation failed","status":400,"errors":{"Value":["Duplicate value."]}}
                    """,
                    HttpStatusCode.BadRequest));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.example.test/") };
        var controller = new AdminController(CreateClient(httpClient));

        var result = await controller.Create(
            new CreateSensitiveWordRequest("DROP", "sql"),
            null,
            null,
            null,
            CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("Index", view.ViewName);
        Assert.IsType<AdminViewModel>(view.Model);
        Assert.False(controller.ModelState.IsValid);
        Assert.Equal("Duplicate value.", controller.ModelState["Value"]?.Errors.Single().ErrorMessage);
    }

    [Fact]
    public void AdminIndexView_ContainsFilterUpdateDeactivateAndDeleteForms()
    {
        var viewPath = Path.Combine(
            TestPaths.RepositoryRoot,
            "src",
            "FlashInterview.Web",
            "Views",
            "Admin",
            "Index.cshtml");

        var content = File.ReadAllText(viewPath);

        Assert.Contains("asp-action=\"Index\"", content, StringComparison.Ordinal);
        Assert.Contains("name=\"q\"", content, StringComparison.Ordinal);
        Assert.Contains("name=\"category\"", content, StringComparison.Ordinal);
        Assert.Contains("name=\"isActive\"", content, StringComparison.Ordinal);
        Assert.Contains("asp-action=\"Update\"", content, StringComparison.Ordinal);
        Assert.Contains("asp-action=\"Deactivate\"", content, StringComparison.Ordinal);
        Assert.Contains("asp-action=\"Delete\"", content, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatIndexView_ContainsSeedExampleMessages()
    {
        var viewPath = Path.Combine(
            TestPaths.RepositoryRoot,
            "src",
            "FlashInterview.Web",
            "Views",
            "Chat",
            "Index.cshtml");

        var content = File.ReadAllText(viewPath);

        Assert.Contains("SELECT * FROM users", content, StringComparison.Ordinal);
        Assert.Contains("drop table users", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-example-message", content, StringComparison.Ordinal);
    }

    private static HttpResponseMessage JsonResponse(string json, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private static SensitiveWordsApiClient CreateClient(HttpClient httpClient)
    {
        return new SensitiveWordsApiClient(
            httpClient,
            Options.Create(new SensitiveWordsApiOptions
            {
                BaseUrl = "https://api.example.test/",
                AdminApiKey = AdminApiKey
            }));
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }
}
