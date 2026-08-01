using Bagly.Api.Options;
using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.Extensions.Options;

namespace Bagly.Api.Services;

public interface ICloudinaryImageService
{
    /// <summary>True when CloudName, ApiKey, and ApiSecret are all set to real (non-placeholder) values.</summary>
    bool IsConfigured { get; }

    Task<string> UploadImageAsync(Stream fileStream, string fileName, CancellationToken cancellationToken);
}

/// <summary>
/// Uploads admin product images to Cloudinary's free tier and returns the resulting secure_url.
/// </summary>
public class CloudinaryImageService : ICloudinaryImageService
{
    private const string UploadFolder = "bagly/products";

    private readonly Cloudinary? _cloudinary;

    public CloudinaryImageService(IOptions<CloudinaryOptions> options)
    {
        var opts = options.Value;
        IsConfigured = opts.IsConfigured;

        if (IsConfigured)
        {
            var account = new Account(opts.CloudName, opts.ApiKey, opts.ApiSecret);
            _cloudinary = new Cloudinary(account)
            {
                Api = { Secure = true },
            };
        }
    }

    public bool IsConfigured { get; }

    public async Task<string> UploadImageAsync(Stream fileStream, string fileName, CancellationToken cancellationToken)
    {
        if (_cloudinary is null)
        {
            throw new InvalidOperationException(
                "Cloudinary is not configured. Set Cloudinary__CloudName, Cloudinary__ApiKey, and Cloudinary__ApiSecret.");
        }

        var uploadParams = new ImageUploadParams
        {
            File = new FileDescription(fileName, fileStream),
            Folder = UploadFolder,
            UseFilename = true,
            UniqueFilename = true,
            Overwrite = false,
        };

        var result = await _cloudinary.UploadAsync(uploadParams, cancellationToken);

        if (result.Error is not null)
        {
            throw new InvalidOperationException($"Cloudinary upload failed: {result.Error.Message}");
        }

        var url = result.SecureUrl?.ToString();
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new InvalidOperationException("Cloudinary did not return an image URL.");
        }

        return url;
    }
}
