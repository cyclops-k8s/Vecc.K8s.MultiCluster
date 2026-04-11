using System.ComponentModel.DataAnnotations;

namespace Cyclops.MultiCluster.Models.Api
{
    /// <summary>
    /// </summary>
    public class HostModel
    {
        /// <summary>
        /// </summary>
        [Required(AllowEmptyStrings = false)]
        public string Hostname { get; set; } = string.Empty;

        /// <summary>
        /// </summary>
        [Required]
        public HostIP[] HostIPs { get; set; } = Array.Empty<HostIP>();
    }
}
