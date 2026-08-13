using Bagly.Api.Options;
using Bagly.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Bagly.Api.Controllers;

/// <summary>Admin diagnostics for Shiprocket auth + pickup nickname matching.</summary>
[ApiController]
[Route("api/admin/shiprocket")]
[Authorize(Roles = "Admin")]
public class AdminShiprocketController(
    IShiprocketService shiprocket,
    IOptions<ShiprocketOptions> options) : ControllerBase
{
    /// <summary>
    /// Login to Shiprocket and list pickup nicknames so ops can verify
    /// <c>Shiprocket__PickupLocation</c> matches exactly (case-sensitive).
    /// Never returns credentials.
    /// </summary>
    [HttpGet("connection")]
    public async Task<ActionResult<object>> ProbeConnection(CancellationToken cancellationToken)
    {
        var result = await shiprocket.ProbeConnectionAsync(cancellationToken);
        return Ok(new
        {
            loginOk = result.LoginOk,
            loginError = result.LoginError,
            configuredPickup = result.ConfiguredPickup,
            configuredPickupMatched = result.ConfiguredPickupMatched,
            pickupNicknames = result.PickupNicknames,
            pickupListError = result.PickupListError,
            configPickupChoices = options.Value.GetPickupLocationChoices(),
            hint = !result.LoginOk
                ? "Fix Shiprocket__Email / Shiprocket__Password (API user from Shiprocket → Settings → API)."
                : result.ConfiguredPickupMatched
                    ? "Configured pickup nickname matches a Shiprocket pickup address."
                    : string.IsNullOrWhiteSpace(result.ConfiguredPickup)
                        ? "Set Shiprocket__PickupLocation to an exact nickname from pickupNicknames (not 'test')."
                        : $"Configured pickup '{result.ConfiguredPickup}' was not found in Shiprocket (case-sensitive). Copy one of pickupNicknames into Shiprocket__PickupLocation on Render.",
        });
    }

    /// <summary>Configured UI nicknames from <c>Shiprocket:PickupLocations</c> (no Shiprocket API call).</summary>
    [HttpGet("pickup-locations")]
    public ActionResult<object> GetConfiguredPickupLocations() =>
        Ok(new { locations = options.Value.GetPickupLocationChoices() });
}
