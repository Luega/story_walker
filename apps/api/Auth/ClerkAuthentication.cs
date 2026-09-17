using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace StoryWalker.Api.Auth;

/// <summary>
/// Registers Clerk-issued session tokens as the API's bearer credentials (ADR-0002). Clerk is an
/// ordinary OIDC issuer with one twist: its session tokens carry no <c>aud</c> claim, so audience
/// validation is off and the <c>azp</c> claim — the origin that obtained the token — takes its
/// place. Skipping that check would let any site holding a Clerk token for another application
/// call this API, so it is a hard failure, not a warning.
/// </summary>
public static class ClerkAuthentication
{
    public static IServiceCollection AddClerkAuthentication(
        this IServiceCollection services,
        string authority,
        IReadOnlyCollection<string> authorizedParties
    )
    {
        var parties = new HashSet<string>(authorizedParties, StringComparer.Ordinal);

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // Authority drives JWKS discovery; the tests null it out and supply a key directly.
                options.Authority = authority;

                // Read `sub`, `azp` and `email` under their JWT names instead of the WS-Federation
                // URIs .NET maps them to by default.
                options.MapInboundClaims = false;

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = authority,
                    ValidateAudience = false,
                    ValidateLifetime = true,
                    // Clerk session tokens live 60 seconds, and signing out cannot revoke one that
                    // is already issued — it only stops the next one being minted. This tolerance
                    // is therefore added straight onto the window in which a signed-out token still
                    // works, so it stays small. .NET's default is 5 minutes, which would stretch a
                    // 60-second token to six; raising it back "to be safe" widens exactly the gap
                    // the short lifetime exists to close.
                    ClockSkew = TimeSpan.FromSeconds(5),
                };

                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = context =>
                    {
                        var azp = context.Principal?.FindFirst("azp")?.Value;
                        if (azp is null || !parties.Contains(azp))
                        {
                            context.Fail("The 'azp' claim is not an authorized party.");
                        }

                        return Task.CompletedTask;
                    },
                };
            });

        services.AddAuthorization();

        return services;
    }
}
