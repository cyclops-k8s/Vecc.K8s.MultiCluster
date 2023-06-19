using Microsoft.AspNetCore.Authentication;

namespace Vecc.K8s.MultiCluster.Api.Services.Authentication
{
    public class ApiAuthenticationHandlerOptions : AuthenticationSchemeOptions
    {
        public const string DefaultScheme = "ApiKey";
        public ApiKey[] ApiKeys { get; set; }
    }
}
