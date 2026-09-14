using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using ProposalService.Api.Contracts;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace ProposalService.Api.OpenApi;

public sealed class ProposalSchemaFilter : ISchemaFilter
{
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        if (context.Type == typeof(decimal) || context.Type == typeof(decimal?)) schema.Format = "decimal";
        if (context.Type == typeof(CreateProposalRequest)) schema.Example = new OpenApiObject
        {
            ["customerId"] = new OpenApiString("CUSTOMER-123"),
            ["productCode"] = new OpenApiString("AUTO_BASIC"),
            ["insuredAmount"] = new OpenApiDouble(75000),
            ["monthlyPremium"] = new OpenApiDouble(189.90)
        };
        if (context.Type == typeof(ChangeStatusRequest))
        {
            schema.Example = new OpenApiObject { ["status"] = new OpenApiString(ProposalStatusNames.Approved) };
            if (schema.Properties.TryGetValue("status", out var status))
            {
                status.Enum =
                [
                    new OpenApiString(ProposalStatusNames.Approved),
                    new OpenApiString(ProposalStatusNames.Rejected)
                ];
            }
        }
        if (context.Type == typeof(ProposalResponse))
        {
            if (schema.Properties.TryGetValue("status", out var status))
            {
                status.Enum =
                [
                    new OpenApiString(ProposalStatusNames.UnderReview),
                    new OpenApiString(ProposalStatusNames.Approved),
                    new OpenApiString(ProposalStatusNames.Rejected)
                ];
            }

            schema.Example = new OpenApiObject
            {
                ["id"] = new OpenApiString("7b0eb9ec-0d20-4481-b24f-aa399f27d9fe"),
                ["customerId"] = new OpenApiString("CUSTOMER-123"),
                ["productCode"] = new OpenApiString("AUTO_BASIC"),
                ["insuredAmount"] = new OpenApiDouble(75000),
                ["monthlyPremium"] = new OpenApiDouble(189.90),
                ["status"] = new OpenApiString(ProposalStatusNames.UnderReview),
                ["createdAtUtc"] = new OpenApiString("2026-09-14T21:30:00Z"),
                ["updatedAtUtc"] = new OpenApiString("2026-09-14T21:30:00Z"),
                ["version"] = new OpenApiInteger(1)
            };
        }
    }
}
