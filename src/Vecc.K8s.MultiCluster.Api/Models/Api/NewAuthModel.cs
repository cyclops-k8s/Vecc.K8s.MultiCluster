namespace Vecc.K8s.MultiCluster.Api.Models.Api
{
    /// <summary>
    /// Authentication data to add to remote and local secrets and configmaps
    /// </summary>
    public class NewAuthModel
    {
        /// <summary>
        /// The configmap/secrets to add to the local cluster configuration
        /// </summary>
        public LocalAuthModel LocalAuthModel { get; set; } = new LocalAuthModel();

        /// <summary>
        /// The configmap/secrets to add to the remote cluster configuration
        /// </summary>
        public RemoteAuthModel RemoteAuthModel { get; set; } = new RemoteAuthModel();
    }
}
