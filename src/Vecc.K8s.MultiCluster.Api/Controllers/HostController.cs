using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Vecc.K8s.MultiCluster.Api.Models.Api;
using Vecc.K8s.MultiCluster.Api.Services;

namespace Vecc.K8s.MultiCluster.Api.Controllers
{
    [ApiController]
    [Authorize]
    [Route("[controller]")]
    public class HostController : ControllerBase
    {
        private readonly ILogger<HostController> _logger;
        private readonly ICache _cache;
        private readonly IOptions<MultiClusterOptions> _options;

        public HostController(ILogger<HostController> logger, ICache cache, IOptions<MultiClusterOptions> options)
        {
            _logger = logger;
            _cache = cache;
            _options = options;
        }

        /// <summary>
        /// Updates the host with the new ip's
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        [HttpPost]
        [ProducesResponseType(204)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        [ProducesResponseType(500)]
        public async Task<ActionResult> UpdateHost([FromBody] HostModel model)
        {
            _logger.LogInformation("Received host update for {@hostname}", model.Hostname);
            _logger.LogDebug("Model {@model}", model);

            try
            {
                if (!ModelState.IsValid)
                {
                    _logger.LogWarning("Invalid request state {@model}", model);
                    return BadRequest(ModelState);
                }

                var clusterIdentifier = User.Identity!.Name!;
                var hostIPs = Array.Empty<Models.Core.HostIP>();

                if (model.HostIPs != null && model.HostIPs.Any())
                {
                    hostIPs = model.HostIPs.Select(ip => new Models.Core.HostIP
                    {
                        IPAddress = ip.IPAddress,
                        Priority = ip.Priority,
                        Weight = ip.Weight
                    }).ToArray();
                }

                await _cache.SetHostIPsAsync(model.Hostname, clusterIdentifier, hostIPs);
                return NoContent();
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Error updating host with {@model}", model);
                return base.Problem(exception.Message);
            }
        }

        [HttpGet]
        [ProducesResponseType(typeof(HostModel[]), 200)]
        [ProducesResponseType(404)]
        public async Task<ActionResult<HostModel[]>> Get()
        {
            _logger.LogInformation("Getting a list of local hosts");
            try
            {
                var clusterIdentifier = _options.Value.ClusterIdentifier;
                var hosts = await _cache.GetHostsAsync(clusterIdentifier);

                if (hosts == null)
                {
                    _logger.LogWarning("Hosts for the local cluster is not found.");
                    return NotFound();
                }

                var result = hosts.Select(host => new HostModel
                {
                    HostIPs = host.HostIPs.Select(ip => new HostIP
                    {
                        IPAddress = ip.IPAddress,
                        Priority = ip.Priority,
                        Weight = ip.Weight
                    }).ToArray(),
                    Hostname = host.Hostname
                });

                return Ok(result);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Unable to get local cluster hosts");
                throw;
            }
        }
    }
}
