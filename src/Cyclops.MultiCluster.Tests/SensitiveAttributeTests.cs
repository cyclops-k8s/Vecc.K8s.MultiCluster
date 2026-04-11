using Serilog.Events;

namespace Cyclops.MultiCluster.Tests
{
    public class SensitiveAttributeTests
    {
        [Fact]
        public void TryCreateLogEventProperty_AlwaysReturnsTrueWithMaskedValue()
        {
            // Arrange
            var attribute = new SensitiveAttribute();

            // Act
            var result = attribute.TryCreateLogEventProperty(
                "SecretKey",
                "actual-secret-value",
                null!,
                out var property);

            // Assert
            Assert.True(result);
            Assert.NotNull(property);
            Assert.Equal("SecretKey", property!.Name);
            Assert.IsType<ScalarValue>(property.Value);
            Assert.Equal("***", ((ScalarValue)property.Value).Value);
        }

        [Fact]
        public void TryCreateLogEventProperty_NullValue_StillReturnsMasked()
        {
            // Arrange
            var attribute = new SensitiveAttribute();

            // Act
            var result = attribute.TryCreateLogEventProperty("Password", null, null!, out var property);

            // Assert
            Assert.True(result);
            Assert.NotNull(property);
            Assert.Equal("***", ((ScalarValue)property!.Value).Value);
        }

        [Fact]
        public void TryCreateLogEventProperty_DifferentPropertyNames()
        {
            var attribute = new SensitiveAttribute();

            attribute.TryCreateLogEventProperty("ApiKey", "key123", null!, out var prop1);
            attribute.TryCreateLogEventProperty("Token", "token456", null!, out var prop2);

            Assert.Equal("ApiKey", prop1!.Name);
            Assert.Equal("Token", prop2!.Name);
            Assert.Equal("***", ((ScalarValue)prop1.Value).Value);
            Assert.Equal("***", ((ScalarValue)prop2.Value).Value);
        }

        [Fact]
        public void SensitiveAttribute_IsAnAttribute()
        {
            var attribute = new SensitiveAttribute();
            Assert.IsAssignableFrom<Attribute>(attribute);
        }
    }
}
