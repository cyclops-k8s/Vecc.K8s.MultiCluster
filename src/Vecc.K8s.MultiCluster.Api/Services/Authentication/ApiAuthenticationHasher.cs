using Microsoft.Extensions.Options;
using NewRelic.Api.Agent;
using System.Security.Cryptography;
using System.Text;

namespace Vecc.K8s.MultiCluster.Api.Services.Authentication
{
    public class ApiAuthenticationHasher
    {
        private readonly IOptions<MultiClusterOptions> _clusterOptions;

        public ApiAuthenticationHasher(IOptions<MultiClusterOptions> clusterOptions)
        {
            _clusterOptions = clusterOptions;
        }

        [Trace]
        public async Task<string> GetHashAsync(string key)
        {
            using var hasher = SHA512.Create();
            using var value = new MemoryStream();
            await value.WriteAsync(_clusterOptions.Value.ClusterSalt);
            await value.WriteAsync(Encoding.ASCII.GetBytes(key));
            value.Position = 0;
            var hashedBytes = await hasher.ComputeHashAsync(value);
            var result = Convert.ToBase64String(hashedBytes);
            return result;
        }

        [Trace]
        public async Task<byte[]> GenerateSaltAsync()
        {
            using var generator = RandomNumberGenerator.Create();
            using var stream = new MemoryStream();
            
            await stream.WriteAsync(_clusterOptions.Value.ClusterSalt);
            var byteCount = 512 / 8;
            var salt = new byte[byteCount];
            
            await stream.WriteAsync(salt);

            generator.GetNonZeroBytes(stream.ToArray());
            return salt;
        }
    }
}
