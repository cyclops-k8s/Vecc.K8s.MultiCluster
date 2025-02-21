using System;

namespace Vecc.K8s.MultiCluster.Api.Models.Api
{
    /// <summary>
    /// Data to add to the configmap and secret in the remote cluster
    /// </summary>
    public class RemoteAuthModel
    {
        /// <summary>
        /// Information to copy into the remote ConfigMap.
        /// </summary>
        public RemoteConfigMapModel ConfigMap { get; set; } = new RemoteConfigMapModel();

        /// <summary>
        /// Information to copy into the remote Secret, pre encoded so it's a simple copy/paste. Be sure to change the cluster index (__0__) to the appropriate index.
        /// </summary>
        public RemoteSecretModel Secret { get; set; } = new RemoteSecretModel();

        /// <summary>
        /// Data to add to the remote configmap
        /// </summary>
        public class RemoteConfigMapModel
        {
            /// <summary>
            /// Cluster identifier data for this cluster
            /// </summary>
            public string ClusterIdentifier { get; set; } = string.Empty;

            /// <summary>
            /// This clusters api endpoint
            /// </summary>
            public string Url { get; set; } = string.Empty;
        }

        /// <summary>
        /// Data to add to the remote secret
        /// </summary>
        public class RemoteSecretModel
        {
            /// <summary>
            /// Alreaded encoded value and key to add to the remote cluster secret. Be sure to change the cluster index(__0__) to the appropriate index.
            /// </summary>
            public string Key { get; set; } = string.Empty;
        }
    }
}
