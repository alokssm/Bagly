namespace Bagly.Api.Services;

/// <summary>Thrown when Shiprocket returns 401 or login fails; callers should invalidate token and retry once.</summary>
public sealed class ShiprocketAuthException(string message) : Exception(message);
