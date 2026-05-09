using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FlashInterview.Application.SensitiveWords;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FlashInterview.Tests;

public sealed class ApiSurfaceTests
{
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
    public async Task MaskEndpoint_ReturnsBadRequestWhenMessageIsMissing()
    {
        using var factory = new FlashInterviewApiFactory(new FakeSensitiveWordRepository());
        using var client = factory.CreateHttpsClient();

        using var response = await client.PostAsJsonAsync("/api/messages/mask", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
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

        using var factory = new FlashInterviewApiFactory(repository);
        using var client = factory.CreateHttpsClient();

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

    private sealed class FlashInterviewApiFactory(ISensitiveWordRepository repository) : WebApplicationFactory<Program>
    {
        public HttpClient CreateHttpsClient()
        {
            var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            client.BaseAddress = new Uri("https://localhost");
            return client;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ISensitiveWordRepository>();
                services.AddSingleton(repository);
            });
        }
    }

    private sealed class FakeSensitiveWordRepository : ISensitiveWordRepository
    {
        public IReadOnlyList<SensitiveWordCandidate> ActiveCandidates { get; init; } = [];

        public PagedResponse<SensitiveWordDto> ListResult { get; init; } =
            new([], 1, 50, 0);

        public SensitiveWordQuery? LastQuery { get; private set; }

        public Task<SensitiveWordDto> CreateAsync(CreateSensitiveWordRequest request, CancellationToken cancellationToken)
        {
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
            return Task.FromResult<SensitiveWordDto?>(null);
        }

        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
        {
            return Task.FromResult(false);
        }

        public Task<IReadOnlyList<SensitiveWordCandidate>> ListActiveCandidatesAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(ActiveCandidates);
        }
    }
}
