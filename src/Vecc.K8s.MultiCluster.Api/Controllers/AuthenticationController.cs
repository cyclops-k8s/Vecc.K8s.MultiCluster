using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Vecc.K8s.MultiCluster.Api.Services;
using Vecc.K8s.MultiCluster.Api.Services.Authentication;

namespace Vecc.K8s.MultiCluster.Api.Controllers
{
    [ApiController]
    [Route("[Controller]")]
    [AllowAnonymous]
    public class AuthenticationController : ControllerBase
    {
        private readonly ILogger<AuthenticationController> _logger;
        private readonly IOptions<MultiClusterOptions> _options;
        private readonly IOptions<ApiAuthenticationHandlerOptions> _authOptions;
        private readonly ApiAuthenticationHasher _hasher;

        public AuthenticationController(ILogger<AuthenticationController> logger,
            IOptions<MultiClusterOptions> options,
            IOptions<ApiAuthenticationHandlerOptions> authOptions,
            ApiAuthenticationHasher hasher)
        {
            _logger = logger;
            _options = options;
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

        public class NewAuthModel
        {
            public string EnvironmentIdentifier { get; set; } = string.Empty;
            public string EnvironmentHash { get; set; } = string.Empty;
            public string Key { get; set; } = string.Empty;
        }

        public class NewSaltModel
        {
            public string Salt { get; set; } = string.Empty;
        }
    }
}
