using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Vecc.K8s.MultiCluster.Api.Models.Api;
using Vecc.K8s.MultiCluster.Api.Services;
using Vecc.K8s.MultiCluster.Api.Services.Authentication;

namespace Vecc.K8s.MultiCluster.Api.Controllers
{
    [ApiController]
    [Route("[Controller]")]
    [AllowAnonymous]
    public class AuthenticationController : ControllerBase
    {
        private readonly IOptions<ApiAuthenticationHandlerOptions> _authOptions;
        private readonly ApiAuthenticationHasher _hasher;

        public AuthenticationController(IOptions<ApiAuthenticationHandlerOptions> authOptions, ApiAuthenticationHasher hasher)
        {
            _authOptions = authOptions;
            _hasher = hasher;
        }

        [HttpGet("[action]")]
        public async Task<ActionResult<NewAuthModel>> Auth([FromQuery] string? identifier = null)
        {
            var clusterIdentifier = identifier;
            if (string.IsNullOrWhiteSpace(clusterIdentifier))
            {
                clusterIdentifier = Guid.NewGuid().ToString();
            }

            var nextIndex = (_authOptions.Value.ApiKeys?.Length ?? -1) + 1;
            var key = Guid.NewGuid().ToString();
            var hash = await _hasher.GetHashAsync(key);
            var result = new NewAuthModel
            {
                EnvironmentHash = $"Authentication__Keys__{nextIndex}__Key={hash}",
                EnvironmentIdentifier = $"Authentication__Keys__{nextIndex}__ClusterIdentifier={clusterIdentifier}",
                Key = key
            };
            return Ok(result);
        }

        [HttpGet("[action]")]
        public async Task<ActionResult<NewSaltModel>> Salt()
        {
            var salt = await _hasher.GenerateSaltAsync();
            var result = new NewSaltModel
            {
                Salt = $"ClusterSalt={Convert.ToBase64String(salt)}"
            };

            return Ok(result);
        }
    }
}
