using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using Vecc.K8s.MultiCluster.Api.Controllers;
using Vecc.K8s.MultiCluster.Api.Services.Authentication;

namespace Vecc.K8s.MultiCluster.Api.Services
{
    public class SwaggerOperationFilter : IOperationFilter
    {
        public void Apply(OpenApiOperation operation, OperationFilterContext context)
        {
            var method = context.MethodInfo;
            if (method.DeclaringType == typeof(AuthenticationController))
            {
                operation.Security = new List<OpenApiSecurityRequirement>();
            }
            else
            {
                var securityScheme = new OpenApiSecurityScheme
                {
                    In = ParameterLocation.Header,
                    Name = "X-Api-Key",
                    Type = SecuritySchemeType.ApiKey,
                    Reference = new OpenApiReference
                    {
                        Type = ReferenceType.SecurityScheme,
                        Id = ApiAuthenticationHandlerOptions.DefaultScheme
                    },
                };
                operation.Security = new List<OpenApiSecurityRequirement>
            {
                new OpenApiSecurityRequirement
                {
                    {
                        securityScheme,
                        Array.Empty<string>()
                    }
                }
            };

            }
        }
    }
}
