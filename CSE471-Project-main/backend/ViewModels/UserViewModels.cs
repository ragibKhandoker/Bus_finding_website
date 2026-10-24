using System.ComponentModel.DataAnnotations;
using SmartCommute.Server.Models;

namespace SmartCommute.Server.ViewModels;

public class UserViewModel : EntityViewModel
{
    // Flutter's User model reads "id"; everything else in the clients reads "_id".
    [System.Text.Json.Serialization.JsonPropertyName("id")]
    public string IdAlias => Id;
    public string Name { get; init; } = "";
    public string Email { get; init; } = "";
    public string Role { get; init; } = "";
    public string Gender { get; init; } = "";
    public bool Pass { get; init; }
    public string FriendCode { get; init; } = "";

    public static UserViewModel From(User u) => new()
    {
        Id = u.Id.ToString(),
        Name = u.Name,
        Email = u.Email,
        Role = u.Role,
        Gender = u.Gender,
        Pass = u.Pass,
        FriendCode = u.FriendCode,
    };
}

// Short form used where another person is embedded in a response.
public class PersonViewModel : EntityViewModel
{
    [System.Text.Json.Serialization.JsonPropertyName("id")]
    public string IdAlias => Id;
    public string Name { get; init; } = "";
    public string Email { get; init; } = "";
    public string Gender { get; init; } = "";
    public string FriendCode { get; init; } = "";

    public static PersonViewModel From(User u) => new()
    {
        Id = u.Id.ToString(),
        Name = u.Name,
        Email = u.Email,
        Gender = u.Gender,
        FriendCode = u.FriendCode,
    };
}

public class SignupRequest
{
    [Required, StringLength(100, MinimumLength = 1)]
    public string Name { get; set; } = "";

    [Required, EmailAddress, StringLength(255)]
    public string Email { get; set; } = "";

    [StringLength(30)]
    public string? Gender { get; set; }

    [StringLength(20)]
    public string? Role { get; set; }

    [Required, StringLength(128, MinimumLength = 6, ErrorMessage = "Password must be at least 6 characters.")]
    public string Password { get; set; } = "";
}

public class LoginRequest
{
    [Required, EmailAddress]
    public string Email { get; set; } = "";

    [Required]
    public string Password { get; set; } = "";
}

public class UpdateProfileRequest
{
    [StringLength(100, MinimumLength = 1)]
    public string? Name { get; set; }

    [EmailAddress, StringLength(255)]
    public string? Email { get; set; }

    [StringLength(30)]
    public string? Gender { get; set; }
}

public class UpdatePasswordRequest
{
    [Required]
    public string CurrentPassword { get; set; } = "";

    [Required, StringLength(128, MinimumLength = 6, ErrorMessage = "New password must be at least 6 characters.")]
    public string NewPassword { get; set; } = "";
}

public class SendFriendRequestRequest
{
    [Required]
    public Guid? ToUserId { get; set; }
}

public class AddFriendByCodeRequest
{
    [Required, StringLength(5, MinimumLength = 5)]
    public string FriendCode { get; set; } = "";
}

public class FriendRequestViewModel : EntityViewModel
{
    // Named fromUserId because that is the field the clients read; it holds the sender's details.
    public PersonViewModel FromUserId { get; init; } = new();
    public string ToUserId { get; init; } = "";
    public string Status { get; init; } = "";
    public DateTime CreatedAt { get; init; }

    public static FriendRequestViewModel From(FriendRequest r) => new()
    {
        Id = r.Id.ToString(),
        FromUserId = PersonViewModel.From(r.FromUser!),
        ToUserId = r.ToUserId.ToString(),
        Status = r.Status,
        CreatedAt = r.CreatedAt,
    };
}
