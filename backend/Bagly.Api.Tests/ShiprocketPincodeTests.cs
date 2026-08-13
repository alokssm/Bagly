using System.Text.Json;
using Bagly.Api.Services;
using Xunit;

namespace Bagly.Api.Tests;

public class ShiprocketPincodeTests
{
    [Theory]
    [InlineData("110001", 110001)]
    [InlineData(" 110001 ", 110001)]
    [InlineData("Delhi 110001", 110001)]
    [InlineData("110001-IND", 110001)]
    [InlineData("PIN:110001", 110001)]
    [InlineData("91110001", 110001)] // longer: last 6
    public void NormalizePincode_accepts_valid_indian_pins(string raw, int expected)
    {
        Assert.Equal(expected, ShiprocketService.NormalizePincode(raw));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("90210")]
    [InlineData("012345")]
    [InlineData("abc")]
    [InlineData("11000")]
    public void NormalizePincode_rejects_invalid(string? raw)
    {
        Assert.Null(ShiprocketService.NormalizePincode(raw));
    }

    [Fact]
    public void Billing_pincode_serializes_as_json_number_without_quotes()
    {
        var json = ShiprocketService.SerializeBillingPincodeProbe(110001);

        Assert.Contains("\"billing_pincode\":110001", json);
        Assert.DoesNotContain("\"billing_pincode\":\"110001\"", json);

        using var doc = JsonDocument.Parse(json);
        var el = doc.RootElement.GetProperty("billing_pincode");
        Assert.Equal(JsonValueKind.Number, el.ValueKind);
        Assert.Equal(110001, el.GetInt32());
        Assert.Equal("110001", el.GetRawText());
        Assert.True(ShiprocketService.IsSixDigitJsonNumberToken(el.GetRawText()));
    }

    [Fact]
    public void Shipping_pincode_omitted_when_shipping_is_billing()
    {
        var json = ShiprocketService.SerializeCreatePayload(new ShiprocketService.ShiprocketCreatePayload
        {
            BillingPincode = 110001,
            ShippingIsBilling = true,
            ShippingPincode = null,
        });

        Assert.Contains("\"shipping_is_billing\":true", json);
        Assert.DoesNotContain("shipping_pincode", json);
    }

    [Fact]
    public void Shipping_pincode_serializes_as_number_when_not_billing()
    {
        var json = ShiprocketService.SerializeCreatePayload(new ShiprocketService.ShiprocketCreatePayload
        {
            BillingPincode = 110001,
            ShippingIsBilling = false,
            ShippingPincode = 400001,
        });

        Assert.Contains("\"shipping_is_billing\":false", json);
        Assert.Contains("\"shipping_pincode\":400001", json);
        Assert.DoesNotContain("\"shipping_pincode\":\"400001\"", json);
    }
}
