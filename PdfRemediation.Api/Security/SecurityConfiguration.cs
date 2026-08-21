using AspNetCoreRateLimit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace PdfRemediation.Api.Security;

public static class SecurityConfiguration
{
    public static IServiceCollection AddSecurityServices(this IServiceCollection services, IConfiguration configuration)
    {
        // 1. Rate Limiting to prevent DoS
        services.AddMemoryCache();
        services.Configure<IpRateLimitOptions>(options =>
        {
            options.GeneralRules = new List<RateLimitRule>
            {
                new RateLimitRule
                {
                    Endpoint = "*",
                    Period = "1m",
                    Limit = 30
                }
            };
        });
        services.AddSingleton<IIpPolicyStore, MemoryCacheIpPolicyStore>();
        services.AddSingleton<IRateLimitCounterStore, MemoryCacheRateLimitCounterStore>();
        services.AddSingleton<IRateLimitConfiguration, RateLimitConfiguration>();
        services.AddSingleton<IProcessingStrategy, AsyncKeyLockProcessingStrategy>();
        services.AddInMemoryRateLimiting();

        // 2. JWT Authentication for Admin portal
        var jwtSecret = configuration["JWT_SECRET_KEY"] ?? "fallback-secret-for-dev-only-do-not-use-in-prod-123456";
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret))
                };
            });

        // 3. CORS targeting Vercel
        services.AddCors(options =>
        {
            options.AddPolicy("AllowVercel", builder =>
            {
                builder.WithOrigins(
                    "http://localhost:3000",
                    "https://pdf-remediation-api-moon.vercel.app" // Vercel Permlink
                )
                .AllowAnyMethod()
                .AllowAnyHeader();
            });
        });

        return services;
    }
}
