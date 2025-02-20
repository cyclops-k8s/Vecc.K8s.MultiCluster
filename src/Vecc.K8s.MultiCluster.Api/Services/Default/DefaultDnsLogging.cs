using ILogger = Vecc.Dns.ILogger;

namespace Vecc.K8s.MultiCluster.Api.Services.Default
{
    public class DefaultDnsLogging : ILogger
    {
        private readonly ILogger<DefaultDnsLogging>  _logger;

        public DefaultDnsLogging(ILogger<DefaultDnsLogging> logger)
        {
            _logger = logger;
        }

#pragma warning disable CA2254 // Template should be a static expression
        public IDisposable? CreateScope(string message, params object?[] values) =>
            _logger.BeginScope(message, values);
#pragma warning restore CA2254 // Template should be a static expression

        public void LogDebug(string message, params object?[] values) =>
            _logger.LogDebug(message, values);

        public void LogError(Exception ex, string message, params object?[] values) =>
            _logger.LogError(ex, message, values);

        public void LogError(string message, params object?[] values) =>
            _logger.LogError(message, values);

        public void LogFatal(Exception ex, string message, params object?[] values) =>
            _logger.LogCritical(ex, message, values);

        public void LogInformation(string message, params object?[] values) =>
            _logger.LogInformation(message, values);

        public void LogVerbose(string message, params object?[] values) =>
            _logger.LogTrace(message, values);

        public void LogWarning(string message, params object?[] values) =>
            _logger.LogWarning(message, values);
    }
}
