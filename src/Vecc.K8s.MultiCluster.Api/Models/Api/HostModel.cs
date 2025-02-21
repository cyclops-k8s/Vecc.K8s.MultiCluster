using System.ComponentModel.DataAnnotations;

namespace Vecc.K8s.MultiCluster.Api.Models.Api
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
