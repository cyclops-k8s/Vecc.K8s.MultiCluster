namespace Vecc.K8s.MultiCluster.Api.Models.Api
{
    /// <summary>
    /// Configuration to add to the local cluster
    /// </summary>
    public class LocalAuthModel
    {
        /// <summary>
        /// Information to copy into the local ConfigMap
        /// </summary>
        public LocalConfigMapModel ConfigMap { get; set; } = new LocalConfigMapModel();

        /// <summary>
        /// Information to copy into the local Secret, pre encoded so it's a simple copy/paste
        /// </summary>
        public LocalSecretModel Secret { get; set; } = new LocalSecretModel();

        /// <summary>
        /// Configmap data for local cluster
        /// </summary>
        public class LocalConfigMapModel
        {
            /// <summary>
            /// Cluster identifier data
            /// </summary>
            public string ClusterIdentifier { get; set; } = string.Empty;
        }

        /// <summary>
        /// Secret data for the local cluster
        /// </summary>
        public class LocalSecretModel
        {
            /// <summary>
            /// The hash part of the secret to add, this is already encoded for a simple copy/paste
            /// </summary>
            public string Hash { get; set; } = string.Empty;
        }

    }
}
