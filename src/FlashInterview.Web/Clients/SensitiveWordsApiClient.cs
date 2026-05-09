using System.Net.Http.Json;
using System.Text.Json;
using FlashInterview.Application.SensitiveWords;
using Microsoft.AspNetCore.WebUtilities;

namespace FlashInterview.Web.Clients;

public sealed class SensitiveWordsApiClient(HttpClient httpClient)
{
    public async Task<PagedResponse<SensitiveWordDto>?> ListAsync(CancellationToken cancellationToken)
    {
        return await ListAsync(new SensitiveWordQuery(null, null, null), cancellationToken);
    }

    public async Task<PagedResponse<SensitiveWordDto>?> ListAsync(
        SensitiveWordQuery query,
        CancellationToken cancellationToken)
    {
        var queryValues = new Dictionary<string, string?>();

        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            queryValues["q"] = query.Q;
        }

        if (!string.IsNullOrWhiteSpace(query.Category))
        {
            queryValues["category"] = query.Category;
        }

        if (query.IsActive is not null)
        {
            queryValues["isActive"] = query.IsActive.Value.ToString().ToLowerInvariant();
        }

        queryValues["page"] = query.Page.ToString();
        queryValues["pageSize"] = query.PageSize.ToString();

        var uri = QueryHelpers.AddQueryString("api/sensitive-words", queryValues);
        return await httpClient.GetFromJsonAsync<PagedResponse<SensitiveWordDto>>(uri, cancellationToken);
    }

    public async Task CreateAsync(CreateSensitiveWordRequest request, CancellationToken cancellationToken)
    {
        var response = await httpClient.PostAsJsonAsync("api/sensitive-words", request, cancellationToken);
        await EnsureSuccessOrThrowValidationAsync(response, cancellationToken);
    }

    public async Task UpdateAsync(Guid id, UpdateSensitiveWordRequest request, CancellationToken cancellationToken)
    {
        var response = await httpClient.PutAsJsonAsync($"api/sensitive-words/{id}", request, cancellationToken);
        await EnsureSuccessOrThrowValidationAsync(response, cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var response = await httpClient.DeleteAsync($"api/sensitive-words/{id}", cancellationToken);
        await EnsureSuccessOrThrowValidationAsync(response, cancellationToken);
    }

    public async Task<MaskMessageResponse?> MaskAsync(MaskMessageRequest request, CancellationToken cancellationToken)
    {
        var response = await httpClient.PostAsJsonAsync("api/messages/mask", request, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<MaskMessageResponse>(cancellationToken);
    }

    private static async Task EnsureSuccessOrThrowValidationAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        if ((int)response.StatusCode == StatusCodes.Status400BadRequest)
        {
            var errors = await ReadValidationErrorsAsync(response, cancellationToken);
            if (errors.Count > 0)
            {
                throw new ApiValidationException(errors);
            }
        }

        response.EnsureSuccessStatusCode();
    }

    private static async Task<Dictionary<string, string[]>> ReadValidationErrorsAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (!document.RootElement.TryGetProperty("errors", out var errorsElement)
            || errorsElement.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in errorsElement.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var messages = property.Value
                .EnumerateArray()
                .Where(element => element.ValueKind == JsonValueKind.String)
                .Select(element => element.GetString())
                .Where(message => !string.IsNullOrWhiteSpace(message))
                .Cast<string>()
                .ToArray();

            if (messages.Length > 0)
            {
                errors[property.Name] = messages;
            }
        }

        return errors;
    }
}
