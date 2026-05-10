using FlashInterview.Application.Auth;
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

        if (!authorizeAttributes.Any(attribute => attribute.Policy == AuthorizationPolicies.AdminApiKey))
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

        jsonMediaType.Schema = (context.ApiDescription.HttpMethod, context.ApiDescription.RelativePath) switch
        {
            ("POST", "api/auth/login") => context.SchemaGenerator.GenerateSchema(
                typeof(LoginRequest),
                context.SchemaRepository),
            ("POST", "api/auth/external-login/sign-in") => context.SchemaGenerator.GenerateSchema(
                typeof(ExternalLoginRequest),
                context.SchemaRepository),
            _ => jsonMediaType.Schema
        };

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
            ("POST", "api/auth/login") => JsonNode.Parse(
                """
                {
                  "email": "admin@example.test",
                  "password": "Correct_password123!"
                }
                """),
            ("POST", "api/auth/external-login/sign-in") => JsonNode.Parse(
                """
                {
                  "provider": "Google",
                  "providerKey": "google-user-id",
                  "email": "admin@example.test",
                  "emailVerified": true,
                  "displayName": "Admin User"
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
        swaggerDoc.Paths["/healthz"] = CreateHealthPath(
            "Liveness health check.",
            includeUnhealthyResponse: false);
        swaggerDoc.Paths["/readyz"] = CreateHealthPath(
            "Readiness health check.",
            includeUnhealthyResponse: true);
    }

    private static OpenApiPathItem CreateHealthPath(string description, bool includeUnhealthyResponse)
    {
        var responses = new OpenApiResponses
        {
            ["200"] = new OpenApiResponse { Description = "The service is healthy." }
        };

        if (includeUnhealthyResponse)
        {
            responses["503"] = new OpenApiResponse { Description = "The service is unhealthy." };
        }

        return new OpenApiPathItem
        {
            Operations = new Dictionary<HttpMethod, OpenApiOperation>
            {
                [HttpMethod.Get] = new()
                {
                    Summary = description,
                    Description = "Returns service health status for deployment and container orchestration probes.",
                    Responses = responses
                }
            }
        };
    }
}
