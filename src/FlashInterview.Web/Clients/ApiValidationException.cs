namespace FlashInterview.Web.Clients;

public sealed class ApiValidationException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("The API returned validation errors.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
