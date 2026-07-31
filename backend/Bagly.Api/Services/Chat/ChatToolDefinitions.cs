namespace Bagly.Api.Services.Chat;

/// <summary>Tool names and JSON-schema definitions shared by the OpenAI agent and the rule-based fallback.</summary>
public static class ChatToolDefinitions
{
    public const string CheckProductAvailability = "check_product_availability";
    public const string CreateStockAlert = "create_stock_alert";
    public const string GetOrderStatus = "get_order_status";

    public static IReadOnlyList<AgentToolDefinition> All { get; } =
    [
        new AgentToolDefinition
        {
            Name = CheckProductAvailability,
            Description =
                "Check whether a Bagly product is in stock and available to buy. " +
                "Use this whenever the customer asks about stock, availability, or 'do you have'.",
            ParametersSchemaJson = """
                {
                  "type": "object",
                  "properties": {
                    "productName": {
                      "type": "string",
                      "description": "The product name or id the customer is asking about, e.g. 'Trail Daypack' or 'trail-pack'."
                    }
                  },
                  "required": ["productName"],
                  "additionalProperties": false
                }
                """,
        },
        new AgentToolDefinition
        {
            Name = CreateStockAlert,
            Description =
                "Register an email alert so the customer is notified when an out-of-stock product is back " +
                "in stock. Only use this after the customer explicitly gives their email address and the " +
                "product they want to be notified about.",
            ParametersSchemaJson = """
                {
                  "type": "object",
                  "properties": {
                    "productName": {
                      "type": "string",
                      "description": "The product name or id to watch, e.g. 'Trail Daypack' or 'trail-pack'."
                    },
                    "email": {
                      "type": "string",
                      "description": "The customer's email address."
                    }
                  },
                  "required": ["productName", "email"],
                  "additionalProperties": false
                }
                """,
        },
        new AgentToolDefinition
        {
            Name = GetOrderStatus,
            Description =
                "Look up the status of an order. Requires BOTH the order number (format BG-...) and the " +
                "email address used on the order — never look up an order with only one of the two.",
            ParametersSchemaJson = """
                {
                  "type": "object",
                  "properties": {
                    "orderNumber": {
                      "type": "string",
                      "description": "The order number, e.g. 'BG-20260731-1234' or 'BG-DEMO-1001'."
                    },
                    "email": {
                      "type": "string",
                      "description": "The email address used to place the order."
                    }
                  },
                  "required": ["orderNumber", "email"],
                  "additionalProperties": false
                }
                """,
        },
    ];
}
