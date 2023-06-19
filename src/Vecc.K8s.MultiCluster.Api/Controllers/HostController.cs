using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vecc.K8s.MultiCluster.Api.Models.Api;
using Vecc.K8s.MultiCluster.Api.Models.Core;
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

        public HostController(ILogger<HostController> logger, ICache cache)
        {
            _logger = logger;
            _cache = cache;
        }

        [HttpPost]
        public async Task<ActionResult> UpdateHost([FromBody] UpdateHostModel model)
        {
            _logger.LogInformation("Received host update for {@hostname}", model.Hostname);

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
    }
}
