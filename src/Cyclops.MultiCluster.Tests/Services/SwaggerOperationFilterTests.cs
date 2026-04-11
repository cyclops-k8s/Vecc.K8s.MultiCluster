using Microsoft.OpenApi;
using Moq;
using Swashbuckle.AspNetCore.SwaggerGen;
using System.Reflection;
using Cyclops.MultiCluster.Controllers;
using Cyclops.MultiCluster.Services;

namespace Cyclops.MultiCluster.Tests.Services
{
    public class SwaggerOperationFilterTests
    {
        private readonly SwaggerOperationFilter _filter;

        public SwaggerOperationFilterTests()
        {
            _filter = new SwaggerOperationFilter();
        }

        [Fact]
        public void Apply_ForAuthenticationController_RemovesSecurity()
        {
            var operation = new OpenApiOperation();
            operation.Security = new List<OpenApiSecurityRequirement>
            {
                new OpenApiSecurityRequirement()
            };

            var method = typeof(AuthenticationController).GetMethod("Auth")!;
            var context = CreateOperationFilterContext(method);

            _filter.Apply(operation, context);

            Assert.Empty(operation.Security);
        }

        [Fact]
        public void Apply_ForNonAuthController_AddsSecurity()
        {
            var operation = new OpenApiOperation();
            var method = typeof(HeartbeatController).GetMethod("Heartbeat")!;
            var context = CreateOperationFilterContext(method);

            _filter.Apply(operation, context);

            Assert.NotNull(operation.Security);
            Assert.NotEmpty(operation.Security);
        }

        private OperationFilterContext CreateOperationFilterContext(MethodInfo methodInfo)
        {
            var apiDescriptionMock = new Microsoft.AspNetCore.Mvc.ApiExplorer.ApiDescription();
            var schemaGeneratorMock = new Mock<ISchemaGenerator>();
            var schemaRepositoryMock = new SchemaRepository();

            return new OperationFilterContext(apiDescriptionMock, schemaGeneratorMock.Object, schemaRepositoryMock, new OpenApiDocument(), methodInfo);
        }
    }
}
