using System.Text.Json.Nodes;

namespace AgentConnector;

public static class ActionSchemas
{
    public static JsonObject Input(ConnectorAction action)
    {
        var properties = new JsonObject();
        var required = new JsonArray();
        foreach (var parameter in action.Parameters)
        {
            properties[parameter.Name] = new JsonObject
            {
                ["type"] = parameter.Kind switch { ParameterKind.Integer => "integer", ParameterKind.Boolean => "boolean", _ => "string" }
            };
            if (parameter.Required) required.Add(parameter.Name);
        }
        if (action.AcceptsBody)
        {
            properties["body"] = new JsonObject { ["type"] = "object", ["description"] = "Provider-native JSON request body. Validated by the provider." };
            if (action.RequiresBody) required.Add("body");
        }
        return new() { ["type"] = "object", ["properties"] = properties, ["required"] = required, ["additionalProperties"] = false };
    }

    public static JsonObject OpenApi(ProviderCatalog catalog, ConnectorPolicy policy, string prefix)
    {
        var paths = new JsonObject();
        foreach (var action in catalog.Actions.Where(action => policy.IsActionAllowed(action.Id)))
        {
            paths["/v1/actions/" + action.Id] = new JsonObject
            {
                ["post"] = new JsonObject
                {
                    ["operationId"] = action.Id, ["summary"] = action.Description,
                    ["requestBody"] = new JsonObject
                    {
                        ["required"] = true,
                        ["content"] = new JsonObject
                        {
                            ["application/json"] = new JsonObject
                            {
                                ["schema"] = new JsonObject
                                {
                                    ["type"] = "object", ["required"] = new JsonArray("input"), ["additionalProperties"] = false,
                                    ["properties"] = new JsonObject { ["input"] = Input(action), ["connectionName"] = new JsonObject { ["type"] = "string", ["default"] = "default" } }
                                }
                            }
                        }
                    },
                    ["responses"] = new JsonObject
                    {
                        ["200"] = new JsonObject { ["description"] = "Execution result in the success/data/meta envelope." },
                        ["400"] = new JsonObject { ["description"] = "Invalid input." },
                        ["401"] = new JsonObject { ["description"] = "Authentication required or expired credentials." },
                        ["403"] = new JsonObject { ["description"] = "Policy denied access." },
                        ["429"] = new JsonObject { ["description"] = "Rate limited by provider." },
                        ["502"] = new JsonObject { ["description"] = "Provider failed; writes must not be blindly retried." }
                    }
                }
            };
        }
        return new()
        {
            ["openapi"] = "3.1.0", ["info"] = new JsonObject { ["title"] = "AgentConnector", ["version"] = "0.1.0" },
            ["servers"] = new JsonArray(new JsonObject { ["url"] = string.IsNullOrEmpty(prefix) ? "/" : prefix }),
            ["security"] = new JsonArray(new JsonObject { ["HostBearer"] = new JsonArray() }),
            ["components"] = new JsonObject { ["securitySchemes"] = new JsonObject { ["HostBearer"] = new JsonObject { ["type"] = "http", ["scheme"] = "bearer" } } },
            ["paths"] = paths
        };
    }
}