using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vecc.K8s.MultiCluster.Api.Services;

namespace Vecc.K8s.MultiCluster.Api.Controllers
{
    [ApiController]
    [Route("[controller]")]
    [Authorize]
    public class HeartbeatController : ControllerBase
    {
        private readonly ILogger<HeartbeatController> _logger;
        private readonly ICache _cache;
        private readonly IDateTimeProvider _dateTimeProvider;

        public HeartbeatController(ILogger<HeartbeatController> logger, ICache cache, IDateTimeProvider dateTimeProvider)
        {
            _logger = logger;
            _cache = cache;
            _dateTimeProvider = dateTimeProvider;
        }

        /// <summary>
        /// Updates the liveness check for the specified cluster identifier
        /// </summary>
        /// <param name="clusterIdentifier">Cluster identifier to update the heartbeat for</param>
        /// <returns>Nothing</returns>
        [HttpPost("")]
        [ProducesResponseType(500)]
        [ProducesResponseType(400)]
        [ProducesResponseType(204)]
        public async Task<ActionResult> Heartbeat()
        {
            var clusterIdentifier = User.Identity?.Name;
            if (clusterIdentifier == null)
            {
                return Unauthorized();
            }

            try
            {
                _logger.LogInformation("Received cluster heartbeat for {@clusterIdentifier}", clusterIdentifier);
                await _cache.SetClusterHeartbeatAsync(clusterIdentifier, _dateTimeProvider.UtcNow);
                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unable to update cluster heartbeat for cluster {@clusterIdentifier}", clusterIdentifier);
                return Problem();
            }
        }
    }
}
