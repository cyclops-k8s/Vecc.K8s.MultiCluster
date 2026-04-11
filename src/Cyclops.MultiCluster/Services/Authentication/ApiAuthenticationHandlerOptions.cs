using Microsoft.AspNetCore.Authentication;

namespace Cyclops.MultiCluster.Services.Authentication
{
    public class ApiAuthenticationHandlerOptions : AuthenticationSchemeOptions
    {
        public const string DefaultScheme = "ApiKey";
        public ApiKey[] ApiKeys { get; set; } = Array.Empty<ApiKey>();
    }
}
