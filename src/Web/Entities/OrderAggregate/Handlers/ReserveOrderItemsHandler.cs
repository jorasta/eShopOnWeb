using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate.Events;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.eShopWeb.Web.Configuration;
using Azure.Messaging.ServiceBus;
using System.Text.Json;

namespace Microsoft.eShopWeb.Web.Entities.OrderAggregate.Handlers;

public class ReserveOrderItemsHandler : INotificationHandler<OrderCreatedEvent>
{
    private readonly ILogger<ReserveOrderItemsHandler> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly OrderItemsReserverOptions _options;

    public ReserveOrderItemsHandler(
        ILogger<ReserveOrderItemsHandler> logger,
        IHttpClientFactory httpClientFactory,
        IOptions<OrderItemsReserverOptions> options)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
    }

    public async Task Handle(OrderCreatedEvent notification, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ServiceBusConnection))
        {
            _logger.LogWarning("ServiceBus Connection not configured; skipping reservation call.");
            return;
        }

        var order = notification.Order;

        var payload = new ReserveOrderRequest
        {
            OrderId = order.Id,
            FinalPrice = order.Total(),
            ShippingAddress = new ReserveOrderShippingAddress
            {
                Street = order.ShipToAddress.Street,
                City = order.ShipToAddress.City,
                State = order.ShipToAddress.State,
                Country = order.ShipToAddress.Country,
                ZipCode = order.ShipToAddress.ZipCode
            },
            Items = order.OrderItems
                .Select(oi => new ReserveOrderItem
                {
                    ItemId = oi.ItemOrdered.CatalogItemId,
                    ProductName = oi.ItemOrdered.ProductName,
                    UnitPrice = oi.UnitPrice,
                    Quantity = oi.Units
                })
                .ToList()
        };
        var _serviceBusClient = new ServiceBusClient(_options.ServiceBusConnection);
        try
        {
            var sender = _serviceBusClient.CreateSender(_options.QueueName);
            var body = JsonSerializer.Serialize(payload);
            var message = new ServiceBusMessage(body)
            {
                ContentType = "application/json"
            };
            message.ApplicationProperties["orderId"] = payload.OrderId;
            await sender.SendMessageAsync(message, cancellationToken);
            _logger.LogInformation("Published order reservation message to Service Bus for OrderId {OrderId}", order.Id);
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish to Service Bus; will fallback to calling Function HTTP endpoint");
        }
    }

    private sealed class ReserveOrderRequest
    {
        public int OrderId { get; set; }
        public decimal FinalPrice { get; set; }
        public ReserveOrderShippingAddress ShippingAddress { get; set; } = new();
        public List<ReserveOrderItem> Items { get; set; } = new();
    }

    private sealed class ReserveOrderItem
    {
        public int ItemId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public decimal UnitPrice { get; set; }
        public int Quantity { get; set; }
    }

    private sealed class ReserveOrderShippingAddress
    {
        public string Street { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public string Country { get; set; } = string.Empty;
        public string ZipCode { get; set; } = string.Empty;
    }
}


