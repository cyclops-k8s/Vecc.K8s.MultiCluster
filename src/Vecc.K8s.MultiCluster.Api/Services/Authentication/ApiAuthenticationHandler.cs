using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using NewRelic.Api.Agent;
using System.Security.Claims;
using System.Text.Encodings.Web;

namespace Vecc.K8s.MultiCluster.Api.Services.Authentication
{
    public class ApiAuthenticationHandler : AuthenticationHandler<ApiAuthenticationHandlerOptions>
    {
        private readonly IOptionsMonitor<ApiAuthenticationHandlerOptions> _options;
        private readonly ApiAuthenticationHasher _hasher;

        public ApiAuthenticationHandler(
            IOptionsMonitor<ApiAuthenticationHandlerOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            TimeProvider clock,
            ApiAuthenticationHasher hasher)
            : base(options, logger, encoder)
        {
            _options = options;
            _hasher = hasher;
        }

        [Trace]
        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (Request.Path.StartsWithSegments("/swagger") ||
                Request.Path.StartsWithSegments("/health") ||
                Request.Path.StartsWithSegments("/healthz") ||
                Request.Path.StartsWithSegments("/scalar") ||
                Request.Path.StartsWithSegments("/coredns.dns.DnsService"))
            {
                Logger.LogTrace("No auth route detected, {path}", Request.Path.ToString());
                return AuthenticateResult.NoResult();
            }

            var keyHeader = StringValues.Empty;
            if (!Request.Headers.TryGetValue("X-Api-Key", out keyHeader))
            {
                Logger.LogWarning("X-Api-Key not found in headers");
                return AuthenticateResult.Fail("Missing X-Api-Key");
            }

            var key = keyHeader.FirstOrDefault();
            if (key == null)
            {
                Logger.LogWarning("X-Api-Key not set");
                return AuthenticateResult.Fail("X-Api-Key not set");
            }

            var hash = await _hasher.GetHashAsync(key);
            var apiKey = _options.CurrentValue.ApiKeys?.FirstOrDefault(x => x.Key == hash);
            if (apiKey == null)
            {
                Logger.LogWarning("X-Api-Key hash not found: {hash}", hash);
                return AuthenticateResult.Fail("X-Api-Key not found");
            }

            using var logScope = Logger.BeginScope("{clusterIdentifier}", apiKey.ClusterIdentifier);
            Logger.LogInformation("Remote cluster authenticated");

            var claims = new[] { new Claim(ClaimTypes.Name, apiKey.ClusterIdentifier) };
            var identity = new ClaimsIdentity(claims, ApiAuthenticationHandlerOptions.DefaultScheme);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, ApiAuthenticationHandlerOptions.DefaultScheme);

            return AuthenticateResult.Success(ticket);
        }
    }
}
