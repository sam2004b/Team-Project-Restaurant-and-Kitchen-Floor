
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Restaurant.Api.Data;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Restaurant.Api.Controllers;

[ApiController]
[Route("dishes")]
public sealed class DishImagesController : ControllerBase
{
    private const long MaxImageSize = 5 * 1024 * 1024;

    private readonly RestaurantDbContext _db;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<DishImagesController> _logger;

    public DishImagesController(
        RestaurantDbContext db,
        IWebHostEnvironment environment,
        ILogger<DishImagesController> logger)
    {
        _db = db;
        _environment = environment;
        _logger = logger;
    }

    // POST /dishes/{id}/image
    [HttpPost("{id:int}/image")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<IActionResult> UploadImage(
        int id,
        IFormFile? image,
        CancellationToken cancellationToken)
    {
        // Temporary safeguard until Admin authentication exists.
        if (!_environment.IsDevelopment())
        {
            return StatusCode(403, new
            {
                error = "Image uploads are disabled outside Development."
            });
        }

        if (id <= 0)
            return BadRequest(new { error = "Invalid dish ID." });

        if (image == null || image.Length == 0)
            return BadRequest(new { error = "Image is required." });

        if (image.Length > MaxImageSize)
        {
            return StatusCode(413, new
            {
                error = "Maximum image size is 5 MB."
            });
        }

        var dish = await _db.Dishes
            .FirstOrDefaultAsync(
                d => d.Id == id,
                cancellationToken);

        if (dish == null)
            return NotFound(new { error = "Dish not found." });

        // Check file signature rather than trusting the filename.
        var signature = new byte[8];

        if (image.Length < signature.Length)
            return BadRequest(new { error = "Invalid image." });

        await using (var input = image.OpenReadStream())
        {
            await input.ReadExactlyAsync(
                signature.AsMemory(),
                cancellationToken);
        }

        bool isJpeg =
            signature[0] == 0xFF &&
            signature[1] == 0xD8 &&
            signature[2] == 0xFF;

        byte[] pngSignature =
        {
            0x89, 0x50, 0x4E, 0x47,
            0x0D, 0x0A, 0x1A, 0x0A
        };

        bool isPng = signature.SequenceEqual(pngSignature);

        if (!isJpeg && !isPng)
        {
            return BadRequest(new
            {
                error = "Only JPG and PNG images are supported."
            });
        }

        var extension = isPng ? ".png" : ".jpg";

        var webRoot = _environment.WebRootPath
            ?? Path.Combine(
                _environment.ContentRootPath,
                "wwwroot");

        var uploadDirectory = Path.Combine(
            webRoot, "images", "uploads");

        Directory.CreateDirectory(uploadDirectory);

        // Never use the original uploaded filename.
        var fileName = $"{Guid.NewGuid():N}{extension}";
        var filePath = Path.Combine(uploadDirectory, fileName);

        var previousImageUrl = dish.ImageUrl;

        try
        {
            await using (var output = new FileStream(
                filePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None))
            {
                await image.CopyToAsync(output, cancellationToken);
            }

            // Save the URL, not the image bytes, in PostgreSQL.
            dish.ImageUrl = $"/images/uploads/{fileName}";

            await _db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // Avoid leaving a new file if saving fails.
            if (System.IO.File.Exists(filePath))
                System.IO.File.Delete(filePath);

            throw;
        }

        // Delete the old image only if it was uploaded by this feature.
        // Never delete seeded images in /images/dishes/.
        const string uploadedPrefix = "/images/uploads/";

        if (previousImageUrl?.StartsWith(
            uploadedPrefix, StringComparison.Ordinal) == true)
        {
            var oldName = previousImageUrl[uploadedPrefix.Length..];

            if (Regex.IsMatch(
                oldName,
                @"^[a-f0-9]{32}\.(jpg|png)$",
                RegexOptions.IgnoreCase))
            {
                try
                {
                    System.IO.File.Delete(
                        Path.Combine(uploadDirectory, oldName));
                }
                catch (IOException ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Unable to delete previous image for dish {DishId}",
                        dish.Id);
                }
                catch (UnauthorizedAccessException ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Unable to delete previous image for dish {DishId}",
                        dish.Id);
                }
            }
        }

        return Ok(new
        {
            dishId = dish.Id,
            imageUrl = dish.ImageUrl
        });
    }
}
