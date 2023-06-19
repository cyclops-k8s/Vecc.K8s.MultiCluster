namespace Vecc.K8s.MultiCluster.Api.Services.Authentication
{
    public class ApiKey
    {
        public string ClusterIdentifier { get; set; } = string.Empty;
        public string Key { get; set; } = string.Empty;
    }
}
