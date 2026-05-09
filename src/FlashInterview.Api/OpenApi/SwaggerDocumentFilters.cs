using FlashInterview.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;
using System.Text.Json.Nodes;

namespace FlashInterview.Api.OpenApi;

internal sealed class AdminApiKeyOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var authorizeAttributes = context.MethodInfo
            .GetCustomAttributes(true)
            .OfType<AuthorizeAttribute>()
            .Concat(context.MethodInfo.DeclaringType?.GetCustomAttributes(true).OfType<AuthorizeAttribute>() ?? []);

        if (!authorizeAttributes.Any(attribute => attribute.Policy == "AdminApiKey"))
        {
            return;
        }

        operation.Security =
        [
            new OpenApiSecurityRequirement
            {
                [
                    new OpenApiSecuritySchemeReference(
                        AdminApiKeyAuthenticationHandler.SchemeName,
                        context.Document,
                        externalResource: null)
                ] = []
            }
        ];
    }
}

internal sealed class RequestExampleOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (operation.RequestBody?.Content is not { } content ||
            !content.TryGetValue("application/json", out var jsonMediaType) ||
            jsonMediaType is null)
        {
            return;
        }

        jsonMediaType.Example = (context.ApiDescription.HttpMethod, context.ApiDescription.RelativePath) switch
        {
            ("POST", "api/sensitive-words") => JsonNode.Parse(
                """
                {
                  "value": "SELECT * FROM",
                  "category": "sql",
                  "isActive": true
                }
                """),
            ("PUT", "api/sensitive-words/{id}") => JsonNode.Parse(
                """
                {
                  "value": "DROP TABLE",
                  "category": "sql",
                  "isActive": false
                }
                """),
            ("POST", "api/messages/mask") => JsonNode.Parse(
                """
                {
                  "message": "Please review this SELECT * FROM example."
                }
                """),
            _ => jsonMediaType.Example
        };
    }
}

internal sealed class RequestParameterDescriptionOperationFilter : IOperationFilter
{
    private static readonly IReadOnlyDictionary<string, string> Descriptions = new Dictionary<string, string>
    {
        ["id"] = "Sensitive-word identifier.",
        ["q"] = "Optional case-insensitive search text matched against the stored value.",
        ["category"] = "Optional category filter, such as sql.",
        ["isActive"] = "Optional active-state filter.",
        ["page"] = "One-based page number.",
        ["pageSize"] = "Maximum number of records to return."
    };

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (operation.Parameters is null)
        {
            return;
        }

        foreach (var parameter in operation.Parameters)
        {
            if (parameter.Name is not null && Descriptions.TryGetValue(parameter.Name, out var description))
            {
                parameter.Description = description;
            }
        }
    }
}

internal sealed class HealthChecksDocumentFilter : IDocumentFilter
{
    public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
    {
        swaggerDoc.Paths ??= new OpenApiPaths();
        swaggerDoc.Paths["/healthz"] = CreateHealthPath("Liveness health check.");
        swaggerDoc.Paths["/readyz"] = CreateHealthPath("Readiness health check.");
    }

    private static OpenApiPathItem CreateHealthPath(string description)
    {
        return new OpenApiPathItem
        {
            Operations = new Dictionary<HttpMethod, OpenApiOperation>
            {
                [HttpMethod.Get] = new()
                {
                    Summary = description,
                    Description = "Returns service health status for deployment and container orchestration probes.",
                    Responses = new OpenApiResponses
                    {
                        ["200"] = new OpenApiResponse { Description = "The service is healthy." },
                        ["503"] = new OpenApiResponse { Description = "The service is unhealthy." }
                    }
                }
            }
        };
    }
}
