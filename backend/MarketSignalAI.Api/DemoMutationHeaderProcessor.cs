using NJsonSchema;
using NSwag;
using NSwag.Generation.Processors;
using NSwag.Generation.Processors.Contexts;

namespace MarketSignalAI.Api;

public sealed class DemoMutationHeaderProcessor : IOperationProcessor
{
    public bool Process(OperationProcessorContext context)
    {
        if (new[] { "POST", "PUT", "DELETE" }.Contains(context.OperationDescription.Method, StringComparer.OrdinalIgnoreCase))
            context.OperationDescription.Operation.Parameters.Add(new OpenApiParameter
            {
                Name = "X-Market-Signal", Kind = OpenApiParameterKind.Header, IsRequired = true,
                Schema = new JsonSchema { Type = JsonObjectType.String, Default = "web" },
                Description = "Demo mutation header. Use web."
            });
        return true;
    }
}
