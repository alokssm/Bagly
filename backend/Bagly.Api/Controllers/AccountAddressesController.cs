using System.Security.Claims;
using Bagly.Api.Data;
using Bagly.Api.DTOs;
using Bagly.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bagly.Api.Controllers;

/// <summary>Saved shipping addresses for logged-in storefront customers.</summary>
[ApiController]
[Route("api/account/addresses")]
[Authorize(Roles = "Customer")]
public class AccountAddressesController(BaglyDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ShippingAddressDto>>> GetAddresses(
        CancellationToken cancellationToken)
    {
        var customerId = GetCustomerId();
        if (customerId is null)
        {
            return Unauthorized();
        }

        var addresses = await db.ShippingAddresses.AsNoTracking()
            .Where(a => a.CustomerUserId == customerId)
            .OrderByDescending(a => a.IsDefault)
            .ThenByDescending(a => a.CreatedAt)
            .ToListAsync(cancellationToken);

        return Ok(addresses.Select(MapAddress));
    }

    [HttpPost]
    public async Task<ActionResult<ShippingAddressDto>> CreateAddress(
        [FromBody] UpsertShippingAddressRequest request,
        CancellationToken cancellationToken)
    {
        var customerId = GetCustomerId();
        if (customerId is null)
        {
            return Unauthorized();
        }

        var validation = ValidateRequest(request);
        if (validation is not null)
        {
            return BadRequest(new { message = validation });
        }

        var isFirstAddress = !await db.ShippingAddresses
            .AnyAsync(a => a.CustomerUserId == customerId, cancellationToken);

        var address = new CustomerShippingAddress
        {
            CustomerUserId = customerId.Value,
            Label = string.IsNullOrWhiteSpace(request.Label) ? null : request.Label.Trim(),
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            Email = request.Email.Trim(),
            Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim(),
            Address = request.Address.Trim(),
            City = request.City.Trim(),
            State = request.State.Trim(),
            Zip = request.Zip.Trim(),
            Country = request.Country.Trim(),
            IsDefault = request.IsDefault || isFirstAddress,
            CreatedAt = DateTime.UtcNow,
        };

        if (address.IsDefault)
        {
            await ClearOtherDefaultsAsync(customerId.Value, null, cancellationToken);
        }

        db.ShippingAddresses.Add(address);
        await db.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetAddresses), MapAddress(address));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ShippingAddressDto>> UpdateAddress(
        Guid id,
        [FromBody] UpsertShippingAddressRequest request,
        CancellationToken cancellationToken)
    {
        var customerId = GetCustomerId();
        if (customerId is null)
        {
            return Unauthorized();
        }

        var validation = ValidateRequest(request);
        if (validation is not null)
        {
            return BadRequest(new { message = validation });
        }

        var address = await db.ShippingAddresses
            .FirstOrDefaultAsync(a => a.Id == id && a.CustomerUserId == customerId, cancellationToken);

        if (address is null)
        {
            return NotFound(new { message = "Address not found." });
        }

        address.Label = string.IsNullOrWhiteSpace(request.Label) ? null : request.Label.Trim();
        address.FirstName = request.FirstName.Trim();
        address.LastName = request.LastName.Trim();
        address.Email = request.Email.Trim();
        address.Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim();
        address.Address = request.Address.Trim();
        address.City = request.City.Trim();
        address.State = request.State.Trim();
        address.Zip = request.Zip.Trim();
        address.Country = request.Country.Trim();

        if (request.IsDefault && !address.IsDefault)
        {
            await ClearOtherDefaultsAsync(customerId.Value, address.Id, cancellationToken);
            address.IsDefault = true;
        }
        else if (!request.IsDefault && address.IsDefault)
        {
            address.IsDefault = false;
        }

        await db.SaveChangesAsync(cancellationToken);

        return Ok(MapAddress(address));
    }

    [HttpPatch("{id:guid}/default")]
    public async Task<ActionResult<ShippingAddressDto>> SetDefault(
        Guid id,
        CancellationToken cancellationToken)
    {
        var customerId = GetCustomerId();
        if (customerId is null)
        {
            return Unauthorized();
        }

        var address = await db.ShippingAddresses
            .FirstOrDefaultAsync(a => a.Id == id && a.CustomerUserId == customerId, cancellationToken);

        if (address is null)
        {
            return NotFound(new { message = "Address not found." });
        }

        await ClearOtherDefaultsAsync(customerId.Value, address.Id, cancellationToken);
        address.IsDefault = true;
        await db.SaveChangesAsync(cancellationToken);

        return Ok(MapAddress(address));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteAddress(Guid id, CancellationToken cancellationToken)
    {
        var customerId = GetCustomerId();
        if (customerId is null)
        {
            return Unauthorized();
        }

        var address = await db.ShippingAddresses
            .FirstOrDefaultAsync(a => a.Id == id && a.CustomerUserId == customerId, cancellationToken);

        if (address is null)
        {
            return NotFound(new { message = "Address not found." });
        }

        var wasDefault = address.IsDefault;
        db.ShippingAddresses.Remove(address);
        await db.SaveChangesAsync(cancellationToken);

        if (wasDefault)
        {
            var next = await db.ShippingAddresses
                .Where(a => a.CustomerUserId == customerId)
                .OrderByDescending(a => a.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);

            if (next is not null)
            {
                next.IsDefault = true;
                await db.SaveChangesAsync(cancellationToken);
            }
        }

        return NoContent();
    }

    private async Task ClearOtherDefaultsAsync(Guid customerId, Guid? exceptId, CancellationToken cancellationToken)
    {
        var others = await db.ShippingAddresses
            .Where(a => a.CustomerUserId == customerId && a.IsDefault && a.Id != exceptId)
            .ToListAsync(cancellationToken);

        foreach (var other in others)
        {
            other.IsDefault = false;
        }
    }

    private static string? ValidateRequest(UpsertShippingAddressRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FirstName) ||
            string.IsNullOrWhiteSpace(request.LastName) ||
            string.IsNullOrWhiteSpace(request.Email) ||
            string.IsNullOrWhiteSpace(request.Address) ||
            string.IsNullOrWhiteSpace(request.City) ||
            string.IsNullOrWhiteSpace(request.State) ||
            string.IsNullOrWhiteSpace(request.Zip) ||
            string.IsNullOrWhiteSpace(request.Country))
        {
            return "Shipping details are incomplete.";
        }

        return null;
    }

    private Guid? GetCustomerId()
    {
        var raw = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    private static ShippingAddressDto MapAddress(CustomerShippingAddress a) =>
        new(
            a.Id,
            a.Label,
            a.FirstName,
            a.LastName,
            a.Email,
            a.Phone,
            a.Address,
            a.City,
            a.State,
            a.Zip,
            a.Country,
            a.IsDefault,
            a.CreatedAt
        );
}
