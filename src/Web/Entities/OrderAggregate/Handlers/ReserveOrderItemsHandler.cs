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
        if (string.IsNullOrWhiteSpace(_options.FunctionUrl))
        {
            _logger.LogWarning("OrderItemsReserver FunctionUrl not configured; skipping reservation call.");
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

        try
        {
            var client = _httpClientFactory.CreateClient("OrderItemsReserver");

            var requestUri = _options.FunctionUrl;

            // If a function key is provided and not already present in the URL, append it as code=...
            if (!string.IsNullOrWhiteSpace(_options.FunctionKey) && !requestUri.Contains("code="))
            {
                var separator = requestUri.Contains('?') ? '&' : '?';
                requestUri = $"{requestUri}{separator}code={Uri.EscapeDataString(_options.FunctionKey)}";
            }

            var response = await client.PostAsJsonAsync(requestUri, payload, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Failed to call OrderItemsReserver. Status: {StatusCode}. Body: {Body}", (int)response.StatusCode, body);
            }
            else
            {
                _logger.LogInformation("OrderItemsReserver called successfully for OrderId {OrderId}", order.Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception when calling OrderItemsReserver for OrderId {OrderId}", order.Id);
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


