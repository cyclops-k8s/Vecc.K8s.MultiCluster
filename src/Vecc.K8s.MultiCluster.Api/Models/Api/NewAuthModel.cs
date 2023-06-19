namespace Vecc.K8s.MultiCluster.Api.Models.Api
{
    public class NewAuthModel
    {
        public string EnvironmentIdentifier { get; set; } = string.Empty;
        public string EnvironmentHash { get; set; } = string.Empty;
        public string Key { get; set; } = string.Empty;
    }
}
