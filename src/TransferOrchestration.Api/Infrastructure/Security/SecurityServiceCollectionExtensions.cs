using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using TransferOrchestration.BuildingBlocks.Api;
using TransferOrchestration.BuildingBlocks.Security;

namespace TransferOrchestration.Api.Infrastructure.Security;

internal static class SecurityServiceCollectionExtensions
{
    public static IServiceCollection AddApiSecurity(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddHttpContextAccessor();
        services.AddScoped<ICallerIdentity, HttpCallerIdentity>();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, ApiAuthorizationMiddlewareResultHandler>();

        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.Issuer),
                "Authentication:Jwt:Issuer must be configured.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.Audience),
                "Authentication:Jwt:Audience must be configured.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.SigningKey) && options.SigningKey.Length >= 32,
                "Authentication:Jwt:SigningKey must be at least 32 characters.")
            .ValidateOnStart();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                var jwtOptions = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
                    ?? throw new InvalidOperationException("Authentication:Jwt configuration is missing.");

                options.RequireHttpsMetadata = false;
                options.SaveToken = false;
                options.IncludeErrorDetails = false;
                options.TokenValidationParameters = CreateValidationParameters(jwtOptions);
                options.Events = new JwtBearerEvents
                {
                    OnChallenge = async context =>
                    {
                        context.HandleResponse();
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        context.Response.ContentType = "application/problem+json";
                        await context.Response.WriteAsync(
                            JsonSerializer.Serialize(
                                ApiProblemResults.CreateProblemDetails(
                                    StatusCodes.Status401Unauthorized,
                                    "unauthorized",
                                    "Unauthorized",
                                    "Authentication is required."),
                                ApiProblemResults.JsonOptions));
                    },
                    OnAuthenticationFailed = context =>
                    {
                        context.NoResult();
                        return Task.CompletedTask;
                    }
                };
            });

        services.AddAuthorization(options =>
        {
            options.AddPolicy(
                AuthorizationPolicies.Customer,
                policy => policy.RequireRole(SecurityClaimTypes.CustomerRole));
            options.AddPolicy(
                AuthorizationPolicies.Operator,
                policy => policy.RequireRole(SecurityClaimTypes.OperatorRole));
        });

        return services;
    }

    internal static TokenValidationParameters CreateValidationParameters(JwtOptions jwtOptions) =>
        new()
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
            NameClaimType = "sub",
            RoleClaimType = ClaimTypes.Role
        };
}
