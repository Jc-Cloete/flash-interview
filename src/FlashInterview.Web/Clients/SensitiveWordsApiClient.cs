using System.Net.Http.Json;
using FlashInterview.Application.SensitiveWords;

namespace FlashInterview.Web.Clients;

public sealed class SensitiveWordsApiClient(HttpClient httpClient)
{
    public async Task<PagedResponse<SensitiveWordDto>?> ListAsync(CancellationToken cancellationToken)
    {
        return await httpClient.GetFromJsonAsync<PagedResponse<SensitiveWordDto>>("api/sensitive-words", cancellationToken);
    }

    public async Task CreateAsync(CreateSensitiveWordRequest request, CancellationToken cancellationToken)
    {
        var response = await httpClient.PostAsJsonAsync("api/sensitive-words", request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var response = await httpClient.DeleteAsync($"api/sensitive-words/{id}", cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<MaskMessageResponse?> MaskAsync(MaskMessageRequest request, CancellationToken cancellationToken)
    {
        var response = await httpClient.PostAsJsonAsync("api/messages/mask", request, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<MaskMessageResponse>(cancellationToken);
    }
}
