// Infrastructure/Extensions/AuthenticationExtensions.cs
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace KSeF.Backend.Infrastructure.Extensions;

public static class AuthenticationExtensions
{
    public static WebApplicationBuilder AddAppAuthentication(this WebApplicationBuilder builder)
    {
        var jwtKeyRaw = builder.Configuration.GetValue<string>("Jwt:Key");
        var jwtKey = string.IsNullOrWhiteSpace(jwtKeyRaw)
            ? "FallbackKeyForDev2025!MinLen32Chars!!"
            : jwtKeyRaw;
        var jwtIssuer = builder.Configuration.GetValue<string>("Jwt:Issuer") ?? "KSeFMaster";
        var jwtAudience = builder.Configuration.GetValue<string>("Jwt:Audience") ?? "KSeFMasterApp";

        builder.Services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwtIssuer,
                    ValidAudience = jwtAudience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
                    ClockSkew = TimeSpan.FromMinutes(2)
                };
            });

        builder.Services.AddAuthorization();

        return builder;
    }
}