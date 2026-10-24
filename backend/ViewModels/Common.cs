using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace SmartCommute.Server.ViewModels;

// The clients were written against a MongoDB API, so every entity exposes its id as "_id".
public abstract class EntityViewModel
{
    [JsonPropertyName("_id")]
    public string Id { get; init; } = "";
}

// Flutter reads "error" and the web client reads "message", so errors carry both.
public class ErrorResponse(string message)
{
    public bool Success => false;
    public string Message { get; } = message;
    public string Error => Message;
}

public class AmountRequest
{
    [Range(0.01, 1_000_000, ErrorMessage = "Amount must be greater than zero.")]
    public decimal Amount { get; set; }
}

public class UserIdRequest
{
    [Required]
    public Guid? UserId { get; set; }
}
