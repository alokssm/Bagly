using System.Text.Json;
using Bagly.Api.Models;
using Bagly.Api.Shipping;
using Xunit;

namespace Bagly.Api.Tests;

public class ShipmentTrackingStatusTests
{
    [Theory]
    [InlineData("IN TRANSIT", ShipmentTrackingStatus.InTransit)]
    [InlineData("OUT FOR DELIVERY", ShipmentTrackingStatus.OutForDelivery)]
    [InlineData("PICKED UP", ShipmentTrackingStatus.PickedUp)]
    [InlineData("SHIPPED", ShipmentTrackingStatus.PickedUp)]
    [InlineData("Delivered", ShipmentTrackingStatus.Delivered)]
    [InlineData("PICKUP SCHEDULED", ShipmentTrackingStatus.PickupRequested)]
    [InlineData("MANIFEST GENERATED", null)]
    [InlineData("NA", null)]
    public void MapFromShiprocket_maps_common_labels(string raw, string? expected)
    {
        Assert.Equal(expected, ShipmentTrackingStatus.MapFromShiprocket(raw));
    }

    [Fact]
    public void IsForwardOf_rejects_backwards()
    {
        Assert.True(ShipmentTrackingStatus.IsForwardOf(
            ShipmentTrackingStatus.InTransit,
            ShipmentTrackingStatus.PickedUp));
        Assert.False(ShipmentTrackingStatus.IsForwardOf(
            ShipmentTrackingStatus.PickedUp,
            ShipmentTrackingStatus.InTransit));
        Assert.False(ShipmentTrackingStatus.IsForwardOf(
            ShipmentTrackingStatus.Delivered,
            ShipmentTrackingStatus.Delivered));
        Assert.True(ShipmentTrackingStatus.IsForwardOf(
            ShipmentTrackingStatus.PickupRequested,
            null));
    }

    [Fact]
    public void CollectForwardStatuses_walks_scans_and_current_status()
    {
        const string json = """
            {
              "awb": "19041424751540",
              "current_status": "IN TRANSIT",
              "shipment_status": "IN TRANSIT",
              "scans": [
                { "sr-status-label": "MANIFEST GENERATED" },
                { "sr-status-label": "PICKED UP" },
                { "sr-status-label": "SHIPPED" },
                { "sr-status-label": "IN TRANSIT" },
                { "sr-status-label": "OUT FOR DELIVERY" }
              ]
            }
            """;
        using var doc = JsonDocument.Parse(json);
        var pipeline = ShiprocketWebhookController.CollectForwardStatuses(
            doc.RootElement,
            ShipmentTrackingStatus.PickupRequested);

        Assert.Equal(
            [
                ShipmentTrackingStatus.PickedUp,
                ShipmentTrackingStatus.InTransit,
                ShipmentTrackingStatus.OutForDelivery,
            ],
            pipeline);
    }

    [Fact]
    public void CollectForwardStatuses_empty_when_only_backwards()
    {
        const string json = """{ "current_status": "PICKED UP" }""";
        using var doc = JsonDocument.Parse(json);
        var pipeline = ShiprocketWebhookController.CollectForwardStatuses(
            doc.RootElement,
            ShipmentTrackingStatus.Delivered);

        Assert.Empty(pipeline);
    }
}
