using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vecc.K8s.MultiCluster.Api.Services;

namespace Vecc.K8s.MultiCluster.Api.Controllers
{
    /// <summary>
    /// Remote cluster heartbeat operations
    /// </summary>
    [ApiController]
    [Route("[controller]")]
    [Authorize]
    public class HeartbeatController : ControllerBase
    {
        private readonly ILogger<HeartbeatController> _logger;
        private readonly ICache _cache;
        private readonly IDateTimeProvider _dateTimeProvider;

        /// <summary>
        /// Remote cluster heartbeat constructor
        /// </summary>
        /// <param name="logger"></param>
        /// <param name="cache"></param>
        /// <param name="dateTimeProvider"></param>
        public HeartbeatController(ILogger<HeartbeatController> logger, ICache cache, IDateTimeProvider dateTimeProvider)
        {
            _logger = logger;
            _cache = cache;
            _dateTimeProvider = dateTimeProvider;
        }

        /// <summary>
        /// Updates the liveness check for the specified cluster identifier
        /// </summary>
        /// <returns>Nothing</returns>
        [HttpPost]
        [ProducesResponseType(204)]
        [ProducesResponseType(401)]
        [ProducesResponseType(500)]
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
