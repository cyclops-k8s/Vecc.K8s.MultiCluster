using System.ComponentModel.DataAnnotations;

namespace Vecc.K8s.MultiCluster.Api.Models.Api
{
    public class UpdateHostModel
    {
        [Required(AllowEmptyStrings = false)]
        public string Hostname { get; set; } = string.Empty;

        [Required]
        public HostIP[] HostIPs { get; set; } = Array.Empty<HostIP>();
    }
}
