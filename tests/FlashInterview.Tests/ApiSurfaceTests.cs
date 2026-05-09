using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FlashInterview.Api.SensitiveWords;
using FlashInterview.Application.SensitiveWords;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FlashInterview.Tests;

public sealed class ApiSurfaceTests
{
    private const string AdminApiKey = "test-admin-key";

    [Fact]
    public async Task HealthEndpoint_ReturnsOkWithoutDatabaseCheck()
    {
        using var factory = new FlashInterviewApiFactory(new FakeSensitiveWordRepository());
        using var client = factory.CreateHttpsClient();

        using var response = await client.GetAsync("/healthz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ReadinessEndpoint_ReturnsServiceUnavailableWhenSqlServerIsUnavailable()
    {
        using var factory = new FlashInterviewApiFactory(
            new FakeSensitiveWordRepository(),
            connectionString: "Server=127.0.0.1,1;Database=FlashInterview;User Id=sa;Password=bad;TrustServerCertificate=True;Encrypt=True;Connect Timeout=1");
        using var client = factory.CreateHttpsClient();

        using var response = await client.GetAsync("/readyz");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task MaskEndpoint_MasksMessageUsingActiveRepositoryCandidates()
    {
        var repository = new FakeSensitiveWordRepository
        {
            ActiveCandidates =
            [
                new SensitiveWordCandidate("DROP"),
                new SensitiveWordCandidate("SELECT * FROM")
            ]
        };

        using var factory = new FlashInterviewApiFactory(repository);
        using var client = factory.CreateHttpsClient();

        using var response = await client.PostAsJsonAsync("/api/messages/mask", new MaskMessageRequest("DROP then SELECT * FROM users"));

        response.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = document.RootElement;

        Assert.Equal("DROP then SELECT * FROM users", root.GetProperty("originalMessage").GetString());
        Assert.Equal("**** then ************* users", root.GetProperty("maskedMessage").GetString());
        Assert.Equal("DROP", root.GetProperty("matches")[0].GetProperty("value").GetString());
        Assert.Equal(0, root.GetProperty("matches")[0].GetProperty("start").GetInt32());
        Assert.Equal(4, root.GetProperty("matches")[0].GetProperty("end").GetInt32());
        Assert.Equal("SELECT * FROM", root.GetProperty("matches")[1].GetProperty("value").GetString());
    }

    [Fact]
    public async Task MaskEndpoint_ReusesCachedActiveCandidatesAcrossRepeatedCallsAndRefreshesAfterCreateInvalidation()
    {
        var repository = new FakeSensitiveWordRepository
        {
            ActiveCandidates = [new SensitiveWordCandidate("DROP")]
        };

        using var factory = new FlashInterviewApiFactory(repository, AdminApiKey);
        using var client = factory.CreateHttpsClient();

        using var firstResponse = await client.PostAsJsonAsync("/api/messages/mask", new MaskMessageRequest("DROP SELECT"));

        firstResponse.EnsureSuccessStatusCode();
        Assert.Equal("**** SELECT", await ReadMaskedMessageAsync(firstResponse));

        repository.ActiveCandidates = [new SensitiveWordCandidate("SELECT")];

        using var secondResponse = await client.PostAsJsonAsync("/api/messages/mask", new MaskMessageRequest("DROP SELECT"));

        secondResponse.EnsureSuccessStatusCode();
        Assert.Equal("**** SELECT", await ReadMaskedMessageAsync(secondResponse));
        Assert.Equal(1, repository.ListActiveCandidatesCallCount);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/sensitive-words")
        {
            Content = JsonContent.Create(new CreateSensitiveWordRequest("SELECT", "sql"))
        };
        request.Headers.Add("X-Admin-Api-Key", AdminApiKey);

        using var createResponse = await client.SendAsync(request);

        createResponse.EnsureSuccessStatusCode();

        using var thirdResponse = await client.PostAsJsonAsync("/api/messages/mask", new MaskMessageRequest("DROP SELECT"));

        thirdResponse.EnsureSuccessStatusCode();
        Assert.Equal("DROP ******", await ReadMaskedMessageAsync(thirdResponse));
        Assert.Equal(2, repository.ListActiveCandidatesCallCount);
    }

    [Fact]
    public async Task MaskEndpoint_RefreshesCachedActiveCandidatesAfterUpdateInvalidation()
    {
        var repository = new FakeSensitiveWordRepository
        {
            ActiveCandidates = [new SensitiveWordCandidate("DROP")]
        };

        using var factory = new FlashInterviewApiFactory(repository, AdminApiKey);
        using var client = factory.CreateHttpsClient();

        using var firstResponse = await client.PostAsJsonAsync("/api/messages/mask", new MaskMessageRequest("DROP SELECT"));

        firstResponse.EnsureSuccessStatusCode();
        Assert.Equal("**** SELECT", await ReadMaskedMessageAsync(firstResponse));

        repository.ActiveCandidates = [new SensitiveWordCandidate("SELECT")];
        client.DefaultRequestHeaders.Add("X-Admin-Api-Key", AdminApiKey);

        using var updateResponse = await client.PutAsJsonAsync(
            "/api/sensitive-words/11111111-1111-1111-1111-111111111111",
            new UpdateSensitiveWordRequest("SELECT", "sql", true));

        updateResponse.EnsureSuccessStatusCode();

        using var secondResponse = await client.PostAsJsonAsync("/api/messages/mask", new MaskMessageRequest("DROP SELECT"));

        secondResponse.EnsureSuccessStatusCode();
        Assert.Equal("DROP ******", await ReadMaskedMessageAsync(secondResponse));
        Assert.Equal(2, repository.ListActiveCandidatesCallCount);
    }

    [Fact]
    public async Task MaskEndpoint_RefreshesCachedActiveCandidatesAfterDeleteInvalidation()
    {
        var repository = new FakeSensitiveWordRepository
        {
            ActiveCandidates = [new SensitiveWordCandidate("DROP")],
            DeleteResult = true
        };

        using var factory = new FlashInterviewApiFactory(repository, AdminApiKey);
        using var client = factory.CreateHttpsClient();

        using var firstResponse = await client.PostAsJsonAsync("/api/messages/mask", new MaskMessageRequest("DROP SELECT"));

        firstResponse.EnsureSuccessStatusCode();
        Assert.Equal("**** SELECT", await ReadMaskedMessageAsync(firstResponse));

        repository.ActiveCandidates = [new SensitiveWordCandidate("SELECT")];
        using var request = new HttpRequestMessage(
            HttpMethod.Delete,
            "/api/sensitive-words/11111111-1111-1111-1111-111111111111");
        request.Headers.Add("X-Admin-Api-Key", AdminApiKey);

        using var deleteResponse = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        using var secondResponse = await client.PostAsJsonAsync("/api/messages/mask", new MaskMessageRequest("DROP SELECT"));

        secondResponse.EnsureSuccessStatusCode();
        Assert.Equal("DROP ******", await ReadMaskedMessageAsync(secondResponse));
        Assert.Equal(2, repository.ListActiveCandidatesCallCount);
    }

    [Fact]
    public async Task SensitiveWordMatcherCache_DoesNotPublishRefreshThatStartedBeforeInvalidation()
    {
        var repository = new BlockingActiveCandidatesRepository([new SensitiveWordCandidate("DROP")]);
        using var serviceProvider = new ServiceCollection()
            .AddSingleton<ISensitiveWordRepository>(repository)
            .BuildServiceProvider();
        var cache = new SensitiveWordMatcherCache(serviceProvider.GetRequiredService<IServiceScopeFactory>());

        var inFlightRefresh = cache.GetAsync(CancellationToken.None);
        await repository.WaitForRefreshToReadCandidatesAsync();

        repository.ActiveCandidates = [new SensitiveWordCandidate("SELECT")];
        cache.Invalidate();
        repository.AllowRefreshToComplete();

        var staleMatcherForInFlightRequest = await inFlightRefresh;
        var refreshedMatcher = await cache.GetAsync(CancellationToken.None);

        Assert.Equal("**** SELECT", staleMatcherForInFlightRequest.Mask("DROP SELECT").MaskedMessage);
        Assert.Equal("DROP ******", refreshedMatcher.Mask("DROP SELECT").MaskedMessage);
        Assert.Equal(2, repository.ListActiveCandidatesCallCount);
    }

    [Theory]
    [InlineData("GET", "/api/sensitive-words")]
    [InlineData("GET", "/api/sensitive-words/11111111-1111-1111-1111-111111111111")]
    [InlineData("POST", "/api/sensitive-words")]
    [InlineData("PUT", "/api/sensitive-words/11111111-1111-1111-1111-111111111111")]
    [InlineData("DELETE", "/api/sensitive-words/11111111-1111-1111-1111-111111111111")]
    public async Task SensitiveWordsEndpoints_RejectRequestsWithoutAdminApiKey(string method, string path)
    {
        using var factory = new FlashInterviewApiFactory(new FakeSensitiveWordRepository(), AdminApiKey);
        using var client = factory.CreateHttpsClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), path);

        if (method is "POST")
        {
            request.Content = JsonContent.Create(new CreateSensitiveWordRequest("DROP", "sql"));
        }
        else if (method is "PUT")
        {
            request.Content = JsonContent.Create(new UpdateSensitiveWordRequest("DROP", "sql", true));
        }

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SensitiveWordsCreateEndpoint_AcceptsConfiguredAdminApiKey()
    {
        using var factory = new FlashInterviewApiFactory(new FakeSensitiveWordRepository(), AdminApiKey);
        using var client = factory.CreateHttpsClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/sensitive-words")
        {
            Content = JsonContent.Create(new CreateSensitiveWordRequest("DROP", "sql"))
        };
        request.Headers.Add("X-Admin-Api-Key", AdminApiKey);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task MaskEndpoint_ReturnsTooManyRequestsWhenRateLimitIsExceeded()
    {
        using var factory = new FlashInterviewApiFactory(
            new FakeSensitiveWordRepository(),
            rateLimitPermitLimit: 1,
            rateLimitWindowSeconds: 60);
        using var client = factory.CreateHttpsClient();

        using var firstResponse = await client.PostAsJsonAsync("/api/messages/mask", new MaskMessageRequest("DROP"));
        using var secondResponse = await client.PostAsJsonAsync("/api/messages/mask", new MaskMessageRequest("DROP"));

        firstResponse.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.TooManyRequests, secondResponse.StatusCode);
    }

    [Fact]
    public async Task MaskEndpoint_RateLimitUsesForwardedClientIpWhenConfiguredByProxy()
    {
        using var factory = new FlashInterviewApiFactory(
            new FakeSensitiveWordRepository(),
            rateLimitPermitLimit: 1,
            rateLimitWindowSeconds: 60);
        using var client = factory.CreateHttpsClient();

        using var firstRequest = new HttpRequestMessage(HttpMethod.Post, "/api/messages/mask")
        {
            Content = JsonContent.Create(new MaskMessageRequest("DROP"))
        };
        firstRequest.Headers.Add("X-Forwarded-For", "203.0.113.10");

        using var secondRequest = new HttpRequestMessage(HttpMethod.Post, "/api/messages/mask")
        {
            Content = JsonContent.Create(new MaskMessageRequest("DROP"))
        };
        secondRequest.Headers.Add("X-Forwarded-For", "203.0.113.20");

        using var firstResponse = await client.SendAsync(firstRequest);
        using var secondResponse = await client.SendAsync(secondRequest);

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
    }

    [Fact]
    public async Task MaskEndpoint_ReturnsBadRequestWhenMessageIsMissing()
    {
        using var factory = new FlashInterviewApiFactory(new FakeSensitiveWordRepository());
        using var client = factory.CreateHttpsClient();

        using var response = await client.PostAsJsonAsync("/api/messages/mask", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task MaskEndpoint_AllowsEmptyMessage()
    {
        using var factory = new FlashInterviewApiFactory(new FakeSensitiveWordRepository());
        using var client = factory.CreateHttpsClient();

        using var response = await client.PostAsJsonAsync("/api/messages/mask", new MaskMessageRequest(""));

        response.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = document.RootElement;

        Assert.Equal("", root.GetProperty("originalMessage").GetString());
        Assert.Equal("", root.GetProperty("maskedMessage").GetString());
        Assert.Empty(root.GetProperty("matches").EnumerateArray());
    }

    [Fact]
    public async Task MaskEndpoint_ReturnsBadRequestWhenMessageExceedsMaximumLength()
    {
        using var factory = new FlashInterviewApiFactory(new FakeSensitiveWordRepository());
        using var client = factory.CreateHttpsClient();

        using var response = await client.PostAsJsonAsync("/api/messages/mask", new MaskMessageRequest(new string('a', 10_001)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertValidationErrorAsync(response, "Message");
    }

    [Fact]
    public async Task SensitiveWordsCreateEndpoint_ReturnsFieldErrorWhenValueIsWhitespace()
    {
        using var factory = new FlashInterviewApiFactory(new FakeSensitiveWordRepository(), AdminApiKey);
        using var client = factory.CreateHttpsClient();
        client.DefaultRequestHeaders.Add("X-Admin-Api-Key", AdminApiKey);

        using var response = await client.PostAsJsonAsync("/api/sensitive-words", new CreateSensitiveWordRequest("   ", "sql"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertValidationErrorAsync(response, "Value");
    }

    [Fact]
    public async Task SensitiveWordsUpdateEndpoint_ReturnsFieldErrorWhenValueIsWhitespace()
    {
        using var factory = new FlashInterviewApiFactory(new FakeSensitiveWordRepository(), AdminApiKey);
        using var client = factory.CreateHttpsClient();
        client.DefaultRequestHeaders.Add("X-Admin-Api-Key", AdminApiKey);

        using var response = await client.PutAsJsonAsync(
            "/api/sensitive-words/11111111-1111-1111-1111-111111111111",
            new UpdateSensitiveWordRequest("   ", "sql", true));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertValidationErrorAsync(response, "Value");
    }

    [Fact]
    public async Task SensitiveWordsCreateEndpoint_ReturnsFieldErrorWhenValueIsDuplicate()
    {
        using var factory = new FlashInterviewApiFactory(new FakeSensitiveWordRepository { DuplicateOnCreate = true }, AdminApiKey);
        using var client = factory.CreateHttpsClient();
        client.DefaultRequestHeaders.Add("X-Admin-Api-Key", AdminApiKey);

        using var response = await client.PostAsJsonAsync("/api/sensitive-words", new CreateSensitiveWordRequest(" SELECT ", "sql"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertValidationErrorAsync(response, "Value");
    }

    [Fact]
    public async Task SensitiveWordsUpdateEndpoint_ReturnsFieldErrorWhenValueIsDuplicate()
    {
        using var factory = new FlashInterviewApiFactory(new FakeSensitiveWordRepository { DuplicateOnUpdate = true }, AdminApiKey);
        using var client = factory.CreateHttpsClient();
        client.DefaultRequestHeaders.Add("X-Admin-Api-Key", AdminApiKey);

        using var response = await client.PutAsJsonAsync(
            "/api/sensitive-words/11111111-1111-1111-1111-111111111111",
            new UpdateSensitiveWordRequest(" SELECT ", "sql", true));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertValidationErrorAsync(response, "Value");
    }

    [Fact]
    public async Task SensitiveWordsListEndpoint_BindsFiltersAndReturnsPagedResult()
    {
        var repository = new FakeSensitiveWordRepository
        {
            ListResult = new PagedResponse<SensitiveWordDto>(
                [
                    new SensitiveWordDto(
                        Guid.Parse("11111111-1111-1111-1111-111111111111"),
                        "SELECT",
                        "SELECT",
                        "sql",
                        true,
                        DateTimeOffset.Parse("2026-05-09T00:00:00Z"),
                        DateTimeOffset.Parse("2026-05-09T00:00:00Z"))
                ],
                2,
                3,
                10)
        };

        using var factory = new FlashInterviewApiFactory(repository, AdminApiKey);
        using var client = factory.CreateHttpsClient();
        client.DefaultRequestHeaders.Add("X-Admin-Api-Key", AdminApiKey);

        using var response = await client.GetAsync("/api/sensitive-words?q=sel&category=sql&isActive=true&page=2&pageSize=3");

        response.EnsureSuccessStatusCode();
        Assert.Equal(new SensitiveWordQuery("sel", "sql", true, 2, 3), repository.LastQuery);

        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = document.RootElement;

        Assert.Equal(2, root.GetProperty("page").GetInt32());
        Assert.Equal(3, root.GetProperty("pageSize").GetInt32());
        Assert.Equal(10, root.GetProperty("total").GetInt32());
        Assert.Equal("SELECT", root.GetProperty("items")[0].GetProperty("value").GetString());
    }

    [Fact]
    public async Task SwaggerDocument_IncludesExpectedApiAndHealthEndpoints()
    {
        using var factory = new FlashInterviewApiFactory(new FakeSensitiveWordRepository(), AdminApiKey, environment: "Development");
        using var client = factory.CreateHttpsClient();

        using var response = await client.GetAsync("/swagger/v1/swagger.json");

        response.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var paths = document.RootElement.GetProperty("paths");

        AssertOperation(paths, "/api/sensitive-words", "get", "200", "401");
        AssertOperation(paths, "/api/sensitive-words", "post", "201", "400", "401");
        AssertOperation(paths, "/api/sensitive-words/{id}", "get", "200", "401", "404");
        AssertOperation(paths, "/api/sensitive-words/{id}", "put", "200", "400", "401", "404");
        AssertOperation(paths, "/api/sensitive-words/{id}", "delete", "204", "401", "404");
        AssertOperation(paths, "/api/messages/mask", "post", "200", "400", "413", "429");
        AssertOperation(paths, "/healthz", "get", "200");
        AssertOperation(paths, "/readyz", "get", "200", "503");

        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        Assert.True(schemas.TryGetProperty("CreateSensitiveWordRequest", out _));
        Assert.True(schemas.TryGetProperty("UpdateSensitiveWordRequest", out _));
        Assert.True(schemas.TryGetProperty("MaskMessageRequest", out _));
        Assert.True(schemas.TryGetProperty("MaskMessageResponse", out _));
        Assert.True(schemas.TryGetProperty("SensitiveWordDto", out _));
    }

    [Fact]
    public async Task SwaggerDocument_DescribesAdminApiKeySecurityOnlyOnSensitiveWordOperations()
    {
        using var factory = new FlashInterviewApiFactory(new FakeSensitiveWordRepository(), AdminApiKey, environment: "Development");
        using var client = factory.CreateHttpsClient();

        using var response = await client.GetAsync("/swagger/v1/swagger.json");

        response.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = document.RootElement;
        var adminScheme = root
            .GetProperty("components")
            .GetProperty("securitySchemes")
            .GetProperty("AdminApiKey");

        Assert.Equal("apiKey", adminScheme.GetProperty("type").GetString());
        Assert.Equal("header", adminScheme.GetProperty("in").GetString());
        Assert.Equal("X-Admin-Api-Key", adminScheme.GetProperty("name").GetString());

        var paths = root.GetProperty("paths");
        foreach (var (path, method) in new[]
        {
            ("/api/sensitive-words", "get"),
            ("/api/sensitive-words", "post"),
            ("/api/sensitive-words/{id}", "get"),
            ("/api/sensitive-words/{id}", "put"),
            ("/api/sensitive-words/{id}", "delete")
        })
        {
            var operation = paths.GetProperty(path).GetProperty(method);
            var securityRequirement = Assert.Single(operation.GetProperty("security").EnumerateArray());
            Assert.True(securityRequirement.TryGetProperty("AdminApiKey", out _), $"Expected {method.ToUpperInvariant()} {path} to require AdminApiKey.");
        }

        Assert.False(paths.GetProperty("/api/messages/mask").GetProperty("post").TryGetProperty("security", out _));
    }

    [Fact]
    public async Task SwaggerDocument_IncludesRequestParametersAndExamplesForDocumentedOperations()
    {
        using var factory = new FlashInterviewApiFactory(new FakeSensitiveWordRepository(), AdminApiKey, environment: "Development");
        using var client = factory.CreateHttpsClient();

        using var response = await client.GetAsync("/swagger/v1/swagger.json");

        response.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var paths = document.RootElement.GetProperty("paths");

        var listParameters = paths.GetProperty("/api/sensitive-words").GetProperty("get").GetProperty("parameters");
        AssertParameter(listParameters, "q", "query");
        AssertParameter(listParameters, "category", "query");
        AssertParameter(listParameters, "isActive", "query");
        AssertParameter(listParameters, "page", "query");
        AssertParameter(listParameters, "pageSize", "query");

        var getParameters = paths.GetProperty("/api/sensitive-words/{id}").GetProperty("get").GetProperty("parameters");
        AssertParameter(getParameters, "id", "path");

        AssertJsonExample(paths.GetProperty("/api/sensitive-words").GetProperty("post"));
        AssertJsonExample(paths.GetProperty("/api/sensitive-words/{id}").GetProperty("put"));
        AssertJsonExample(paths.GetProperty("/api/messages/mask").GetProperty("post"));
    }

    private static async Task AssertValidationErrorAsync(HttpResponseMessage response, string fieldName)
    {
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var errors = document.RootElement.GetProperty("errors");

        Assert.True(errors.TryGetProperty(fieldName, out var fieldErrors), $"Expected validation error for '{fieldName}'.");
        Assert.NotEmpty(fieldErrors.EnumerateArray());
    }

    private static async Task<string?> ReadMaskedMessageAsync(HttpResponseMessage response)
    {
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return document.RootElement.GetProperty("maskedMessage").GetString();
    }

    private sealed class FlashInterviewApiFactory(
        ISensitiveWordRepository repository,
        string? adminApiKey = null,
        int? rateLimitPermitLimit = null,
        int? rateLimitWindowSeconds = null,
        string environment = "Production",
        string? connectionString = null) : WebApplicationFactory<Program>
    {
        public HttpClient CreateHttpsClient()
        {
            var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            client.BaseAddress = new Uri("https://localhost");
            return client;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environment);
            builder.ConfigureAppConfiguration((_, configurationBuilder) =>
            {
                var configuration = new Dictionary<string, string?>();

                if (adminApiKey is not null)
                {
                    configuration["Security:AdminApiKey"] = adminApiKey;
                }

                if (rateLimitPermitLimit is not null)
                {
                    configuration["Security:MaskRateLimit:PermitLimit"] = rateLimitPermitLimit.Value.ToString();
                }

                if (rateLimitWindowSeconds is not null)
                {
                    configuration["Security:MaskRateLimit:WindowSeconds"] = rateLimitWindowSeconds.Value.ToString();
                }

                if (connectionString is not null)
                {
                    configuration["ConnectionStrings:DefaultConnection"] = connectionString;
                }

                configurationBuilder.AddInMemoryCollection(configuration);
            });
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ISensitiveWordRepository>();
                services.AddSingleton(repository);
            });
        }
    }

    private static void AssertOperation(JsonElement paths, string path, string method, params string[] responseStatusCodes)
    {
        Assert.True(paths.TryGetProperty(path, out var pathItem), $"Expected Swagger path '{path}'.");
        Assert.True(pathItem.TryGetProperty(method, out var operation), $"Expected Swagger operation '{method.ToUpperInvariant()} {path}'.");

        var responses = operation.GetProperty("responses");
        Assert.Equal(
            responseStatusCodes.Order(StringComparer.Ordinal).ToArray(),
            responses.EnumerateObject().Select(response => response.Name).Order(StringComparer.Ordinal).ToArray());

        foreach (var responseStatusCode in responseStatusCodes)
        {
            Assert.True(
                responses.TryGetProperty(responseStatusCode, out _),
                $"Expected Swagger response {responseStatusCode} on {method.ToUpperInvariant()} {path}.");
        }
    }

    private static void AssertParameter(JsonElement parameters, string name, string location)
    {
        Assert.Contains(parameters.EnumerateArray(), parameter =>
            parameter.GetProperty("name").GetString() == name &&
            parameter.GetProperty("in").GetString() == location &&
            parameter.TryGetProperty("description", out var description) &&
            !string.IsNullOrWhiteSpace(description.GetString()));
    }

    private static void AssertJsonExample(JsonElement operation)
    {
        var jsonContent = operation
            .GetProperty("requestBody")
            .GetProperty("content")
            .GetProperty("application/json");

        Assert.True(jsonContent.TryGetProperty("example", out var example), "Expected an application/json request example.");
        Assert.NotEqual(JsonValueKind.Undefined, example.ValueKind);
    }

    private sealed class FakeSensitiveWordRepository : ISensitiveWordRepository
    {
        public IReadOnlyList<SensitiveWordCandidate> ActiveCandidates { get; set; } = [];

        public PagedResponse<SensitiveWordDto> ListResult { get; init; } =
            new([], 1, 50, 0);

        public SensitiveWordQuery? LastQuery { get; private set; }

        public bool DuplicateOnCreate { get; init; }

        public bool DuplicateOnUpdate { get; init; }

        public bool DeleteResult { get; init; }

        public int ListActiveCandidatesCallCount { get; private set; }

        public Task<SensitiveWordDto> CreateAsync(CreateSensitiveWordRequest request, CancellationToken cancellationToken)
        {
            if (DuplicateOnCreate)
            {
                throw new DuplicateSensitiveWordException(request.Value);
            }

            var now = DateTimeOffset.UtcNow;
            var created = new SensitiveWordDto(
                Guid.NewGuid(),
                request.Value.Trim(),
                SensitiveWordNormalizer.Normalize(request.Value),
                request.Category?.Trim(),
                request.IsActive,
                now,
                now);

            return Task.FromResult(created);
        }

        public Task<PagedResponse<SensitiveWordDto>> ListAsync(SensitiveWordQuery query, CancellationToken cancellationToken)
        {
            LastQuery = query;
            return Task.FromResult(ListResult);
        }

        public Task<SensitiveWordDto?> GetAsync(Guid id, CancellationToken cancellationToken)
        {
            return Task.FromResult<SensitiveWordDto?>(null);
        }

        public Task<SensitiveWordDto?> UpdateAsync(Guid id, UpdateSensitiveWordRequest request, CancellationToken cancellationToken)
        {
            if (DuplicateOnUpdate)
            {
                throw new DuplicateSensitiveWordException(request.Value);
            }

            var now = DateTimeOffset.UtcNow;
            var updated = new SensitiveWordDto(
                id,
                request.Value.Trim(),
                SensitiveWordNormalizer.Normalize(request.Value),
                request.Category?.Trim(),
                request.IsActive,
                now,
                now);

            return Task.FromResult<SensitiveWordDto?>(updated);
        }

        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
        {
            return Task.FromResult(DeleteResult);
        }

        public Task<IReadOnlyList<SensitiveWordCandidate>> ListActiveCandidatesAsync(CancellationToken cancellationToken)
        {
            ListActiveCandidatesCallCount++;
            return Task.FromResult(ActiveCandidates);
        }
    }

    private sealed class BlockingActiveCandidatesRepository(
        IReadOnlyList<SensitiveWordCandidate> activeCandidates) : ISensitiveWordRepository
    {
        private readonly TaskCompletionSource refreshReadCandidates = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource allowRefreshToComplete = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyList<SensitiveWordCandidate> ActiveCandidates { get; set; } = activeCandidates;

        public int ListActiveCandidatesCallCount { get; private set; }

        public Task<SensitiveWordDto> CreateAsync(CreateSensitiveWordRequest request, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<PagedResponse<SensitiveWordDto>> ListAsync(SensitiveWordQuery query, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<SensitiveWordDto?> GetAsync(Guid id, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<SensitiveWordDto?> UpdateAsync(Guid id, UpdateSensitiveWordRequest request, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public async Task<IReadOnlyList<SensitiveWordCandidate>> ListActiveCandidatesAsync(CancellationToken cancellationToken)
        {
            ListActiveCandidatesCallCount++;
            var candidates = ActiveCandidates;

            if (ListActiveCandidatesCallCount == 1)
            {
                refreshReadCandidates.SetResult();
                await allowRefreshToComplete.Task.WaitAsync(cancellationToken);
            }

            return candidates;
        }

        public Task WaitForRefreshToReadCandidatesAsync()
        {
            return refreshReadCandidates.Task;
        }

        public void AllowRefreshToComplete()
        {
            allowRefreshToComplete.SetResult();
        }
    }
}
