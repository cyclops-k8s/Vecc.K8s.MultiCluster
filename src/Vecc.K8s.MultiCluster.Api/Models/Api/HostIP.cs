namespace Vecc.K8s.MultiCluster.Api.Models.Api
{
    /// <summary>
    /// </summary>
    public class HostIP
    {
        /// <summary>
        /// </summary>
        public string IPAddress { get; set; } = string.Empty;

        /// <summary>
        /// </summary>
        public int Priority { get; set; }

        /// <summary>
        /// </summary>
        public int Weight { get; set; }
    }
}
