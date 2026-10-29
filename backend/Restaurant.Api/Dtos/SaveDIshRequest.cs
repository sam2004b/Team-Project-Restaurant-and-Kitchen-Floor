using System.ComponentModel.DataAnnotations;

namespace Restaurant.Api.Dtos;

public sealed class SaveDishRequest
{
    [Required]
    [MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    [Range(typeof(decimal), "0.01", "99999999.99")]
    public decimal Price { get; set; }

    [MaxLength(1000)]
    public string? Description { get; set; }

    [Required]
    [MaxLength(80)]
    public string Category { get; set; } = string.Empty;

    public bool IsAvailable { get; set; } = true;

    [Required]
    public List<string> Ingredients { get; set; } = new();
}