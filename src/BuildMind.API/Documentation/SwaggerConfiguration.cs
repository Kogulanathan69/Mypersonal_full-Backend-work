using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace BuildMind.API.Documentation;

/// <summary>
/// Development-only OpenAPI document and Swagger UI (decision C7 proposed; Swashbuckle pending dependency approval).
/// </summary>
/// <remarks>
/// Nothing is registered or mapped outside the Development environment. When M1 authentication is active the
/// document declares an HTTP bearer (JWT) scheme, and only endpoints that require authorization carry the security
/// requirement, so anonymous endpoints (register, login) stay callable without a token in the UI.
/// </remarks>
public static class SwaggerConfiguration
{
    public const string DocumentName = "v1";
    public const string DocumentPath = "/swagger/v1/swagger.json";
    public const string BearerSchemeId = "Bearer";

    public static WebApplicationBuilder AddBuildMindDevelopmentSwagger(
        this WebApplicationBuilder builder,
        bool bearerAuthenticationEnabled)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (!builder.Environment.IsDevelopment())
        {
            return builder;
        }

        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc(DocumentName, new OpenApiInfo
            {
                Title = "BuildMind API",
                Version = DocumentName,
                Description = "Development documentation only. BuildMind produces preliminary planning assistance, "
                    + "not legally certified plans or construction approval.",
            });

            options.OperationFilter<SurveyUploadOperationFilter>();

            if (bearerAuthenticationEnabled)
            {
                options.AddSecurityDefinition(BearerSchemeId, new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                    In = ParameterLocation.Header,
                    Description = "Paste the access token returned by POST /api/v1/auth/login (without the 'Bearer ' prefix).",
                });
                options.OperationFilter<BearerRequirementOperationFilter>();
            }
        });

        return builder;
    }

    public static WebApplication UseBuildMindDevelopmentSwagger(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        if (!app.Environment.IsDevelopment())
        {
            return app;
        }

        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint(DocumentPath, "BuildMind API v1");
            options.DocumentTitle = "BuildMind API Documentation";
            options.RoutePrefix = "swagger";
        });

        return app;
    }


    internal sealed class SurveyUploadOperationFilter : IOperationFilter
    {
        public void Apply(OpenApiOperation operation, OperationFilterContext context)
        {
            var path = context.ApiDescription.RelativePath?.Trim('/');

            if (context.ApiDescription.HttpMethod != "POST"
                || path != "api/v1/projects/{id}/surveys")
            {
                return;
            }

            operation.RequestBody = new OpenApiRequestBody
            {
                Required = true,
                Content = new Dictionary<string, IOpenApiMediaType>
                {
                    ["multipart/form-data"] = new OpenApiMediaType
                    {
                        Schema = new OpenApiSchema
                        {
                            Type = JsonSchemaType.Object,
                            Required = new HashSet<string> { "file" },
                            Properties = new Dictionary<string, IOpenApiSchema>
                            {
                                ["file"] = new OpenApiSchema
                                {
                                    Type = JsonSchemaType.String,
                                    Format = "binary"
                                }
                            }
                        }
                    }
                }
            };
        }
    }

    /// <summary>Adds the bearer requirement only to operations whose endpoint requires authorization.</summary>
    internal sealed class BearerRequirementOperationFilter : IOperationFilter
    {
        public void Apply(OpenApiOperation operation, OperationFilterContext context)
        {
            var metadata = context.ApiDescription.ActionDescriptor.EndpointMetadata;
            var requiresAuthorization = metadata.OfType<IAuthorizeData>().Any()
                && !metadata.OfType<IAllowAnonymous>().Any();
            if (!requiresAuthorization)
            {
                return;
            }

            operation.Security ??= [];
            operation.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(BearerSchemeId, context.Document)] = [],
            });
        }
    }
}
