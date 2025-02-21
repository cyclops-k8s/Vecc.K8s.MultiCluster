using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Text;
using Vecc.K8s.MultiCluster.Api.Models.Api;
using Vecc.K8s.MultiCluster.Api.Services;
using Vecc.K8s.MultiCluster.Api.Services.Authentication;

namespace Vecc.K8s.MultiCluster.Api.Controllers
{
    /// <summary>
    /// Authentication related actions happen here
    /// </summary>
    [ApiController]
    [Route("[Controller]")]
    [AllowAnonymous]
    public class AuthenticationController : ControllerBase
    {
        private readonly IOptions<ApiAuthenticationHandlerOptions> _authOptions;
        private readonly ApiAuthenticationHasher _hasher;
        private readonly IOptions<MultiClusterOptions> _options;

        /// <summary>
        /// Authentication related actions happen here
        /// </summary>
        public AuthenticationController(
            IOptions<ApiAuthenticationHandlerOptions> authOptions,
            ApiAuthenticationHasher hasher,
            IOptions<MultiClusterOptions> options)
        {
            _authOptions = authOptions;
            _hasher = hasher;
            _options = options;
        }

        /// <summary>
        /// Generate auth configuration
        /// </summary>
        /// <param name="identifier"></param>
        /// <returns></returns>
        [HttpGet("[action]")]
        public async Task<ActionResult<NewAuthModel>> Auth([FromQuery] string? identifier = null)
        {
            var clusterIdentifier = identifier;
            if (string.IsNullOrWhiteSpace(clusterIdentifier))
            {
                clusterIdentifier = Guid.NewGuid().ToString();
            }

            var nextIndex = (_authOptions.Value.ApiKeys?.Length ?? 0);
            var key = Guid.NewGuid().ToString();
            var hash = await _hasher.GetHashAsync(key);
            var result = new NewAuthModel();
            result.LocalAuthModel.ConfigMap.ClusterIdentifier = $"Authentication__ApiKeys__{nextIndex}__ClusterIdentifier: {clusterIdentifier}";
            result.LocalAuthModel.Secret.Hash = $"Authentication__ApiKeys__{nextIndex}__Key: {Convert.ToBase64String(Encoding.UTF8.GetBytes(hash))}";

            result.RemoteAuthModel.ConfigMap.ClusterIdentifier = $"Peers__0__Identifier: {_options.Value.ClusterIdentifier}";
            result.RemoteAuthModel.ConfigMap.Url = $"Peers__0__Url: {Request.Scheme}://{Request.Host}";
            result.RemoteAuthModel.Secret.Key = $"Peers__0__Key: {Convert.ToBase64String(Encoding.UTF8.GetBytes(key))}";

            return Ok(result);
        }

        /// <summary>
        /// Generate a new salt for use in the configuration
        /// </summary>
        /// <returns></returns>
        [HttpGet("[action]")]
        public async Task<ActionResult<NewSaltModel>> Salt()
        {
            var salt = await _hasher.GenerateSaltAsync();
            var result = new NewSaltModel
            {
                Salt = $"ClusterSalt: {Convert.ToBase64String(salt)}"
            };

            return Ok(result);
        }
    }
}
