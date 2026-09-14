using ContractService.Api.Contracts;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace ContractService.Api.OpenApi;

public sealed class ContractSchemaFilter : ISchemaFilter
{
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        if (context.Type == typeof(CreateContractRequest)) schema.Example = new OpenApiObject
        {
            ["proposalId"] = new OpenApiString("7b0eb9ec-0d20-4481-b24f-aa399f27d9fe")
        };
        if (context.Type == typeof(ContractResponse)) schema.Example = new OpenApiObject
        {
            ["id"] = new OpenApiString("97fabcf7-6dc4-4896-bbe1-274ce749217f"),
            ["proposalId"] = new OpenApiString("7b0eb9ec-0d20-4481-b24f-aa399f27d9fe"),
            ["contractedAtUtc"] = new OpenApiString("2026-09-14T21:40:00Z")
        };
    }
}
