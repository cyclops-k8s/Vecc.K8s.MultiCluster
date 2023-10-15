using Destructurama.Attributed;
using Serilog.Core;
using Serilog.Events;

namespace Vecc.K8s.MultiCluster.Api
{
    public class SensitiveAttribute : Attribute, IPropertyDestructuringAttribute
    {
#pragma warning disable CS8767 // Nullability of reference types in type of parameter doesn't match implicitly implemented member (possibly because of nullability attributes).
        public bool TryCreateLogEventProperty(string name, object? value, ILogEventPropertyValueFactory propertyValueFactory, out LogEventProperty? property)
#pragma warning restore CS8767 // Nullability of reference types in type of parameter doesn't match implicitly implemented member (possibly because of nullability attributes).
        {
            property = new LogEventProperty(name, new ScalarValue("***"));
            return true;
        }
    }
}
