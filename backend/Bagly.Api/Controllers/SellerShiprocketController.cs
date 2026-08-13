using Bagly.Api.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Bagly.Api.Controllers;

/// <summary>Seller-facing Shiprocket helpers (config nicknames only — no credentials).</summary>
[ApiController]
[Route("api/seller/shiprocket")]
[Authorize(Roles = "Seller")]
public class SellerShiprocketController(IOptions<ShiprocketOptions> options) : ControllerBase
{
    /// <summary>
    /// Pickup nicknames for the product form dropdown (<c>Shiprocket:PickupLocations</c>).
    /// These must already exist in the Shiprocket panel; Bagly never auto-creates addresses.
    /// </summary>
    [HttpGet("pickup-locations")]
    public ActionResult<object> GetConfiguredPickupLocations() =>
        Ok(new { locations = options.Value.GetPickupLocationChoices() });
}
