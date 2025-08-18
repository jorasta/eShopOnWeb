namespace Microsoft.eShopWeb.Web.Configuration;

public class OrderItemsReserverOptions
{
    public string FunctionUrl { get; set; } = string.Empty;
    public string? FunctionKey { get; set; }
    public string ServiceBusConnection { get; set; } = string.Empty;
    public string QueueName { get; set; } = string.Empty;

}


