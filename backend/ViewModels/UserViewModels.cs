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
    public string Phone { get; init; } = "";
    public string Bio { get; init; } = "";
    public string HomeArea { get; init; } = "";
    public string WorkArea { get; init; } = "";
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }

    public static UserViewModel From(User u) => new()
    {
        Id = u.Id.ToString(),
        Name = u.Name,
        Email = u.Email,
        Role = u.Role,
        Gender = u.Gender,
        Pass = u.Pass,
        FriendCode = u.FriendCode,
        Phone = u.Phone ?? "",
        Bio = u.Bio ?? "",
        HomeArea = u.HomeArea ?? "",
        WorkArea = u.WorkArea ?? "",
        CreatedAt = u.CreatedAt,
        UpdatedAt = u.UpdatedAt,
    };
}

// What the admin user list shows for each account.
public class AdminUserViewModel : EntityViewModel
{
    [System.Text.Json.Serialization.JsonPropertyName("id")]
    public string IdAlias => Id;
    public string Name { get; init; } = "";
    public string Email { get; init; } = "";
    public string Role { get; init; } = "";
    public DateTime CreatedAt { get; init; }
    public DateTime? LastLoginAt { get; init; }

    public static AdminUserViewModel From(User u) => new()
    {
        Id = u.Id.ToString(),
        Name = u.Name,
        Email = u.Email,
        Role = u.Role,
        CreatedAt = u.CreatedAt,
        LastLoginAt = u.LastLoginAt,
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
    [Required(ErrorMessage = "Please enter your name.")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Name must be between 2 and 100 characters.")]
    public string Name { get; set; } = "";

    // [EmailAddress] alone accepts "a@b", so also require a dot in the domain.
    [Required(ErrorMessage = "Please enter your email address.")]
    [StringLength(255, ErrorMessage = "Email must be at most 255 characters.")]
    [EmailAddress(ErrorMessage = "Please enter a valid email address.")]
    [RegularExpression(@"^\s*[^@\s]+@[^@\s]+\.[^@\s]+\s*$", ErrorMessage = "Please enter a valid email address.")]
    public string Email { get; set; } = "";

    [StringLength(30)]
    public string? Gender { get; set; }

    [StringLength(20)]
    public string? Role { get; set; }

    [Required(ErrorMessage = "Please choose a password.")]
    [StringLength(128, MinimumLength = 6, ErrorMessage = "Password must be at least 6 characters.")]
    [RegularExpression(@"^(?=.*[A-Za-z])(?=.*\d).+$", ErrorMessage = "Password must contain at least one letter and one number.")]
    public string Password { get; set; } = "";
}

public class LoginRequest
{
    [Required(ErrorMessage = "Please enter your email address.")]
    [EmailAddress(ErrorMessage = "Please enter a valid email address.")]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = "Please enter your password.")]
    public string Password { get; set; } = "";
}

// A field left out (null) is not changed. For the optional details an empty string clears the value.
public class UpdateProfileRequest
{
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Name must be between 2 and 100 characters.")]
    public string? Name { get; set; }

    [StringLength(255, ErrorMessage = "Email must be at most 255 characters.")]
    [EmailAddress(ErrorMessage = "Please enter a valid email address.")]
    [RegularExpression(@"^\s*[^@\s]+@[^@\s]+\.[^@\s]+\s*$", ErrorMessage = "Please enter a valid email address.")]
    public string? Email { get; set; }

    [StringLength(30)]
    public string? Gender { get; set; }

    // Digits with an optional leading + and common separators, e.g. "+880 1712-345678".
    [StringLength(20, ErrorMessage = "Phone number must be at most 20 characters.")]
    [RegularExpression(@"^\s*(\+?[0-9][0-9 \-]{5,18})?\s*$", ErrorMessage = "Please enter a valid phone number.")]
    public string? Phone { get; set; }

    [StringLength(200, ErrorMessage = "Bio must be at most 200 characters.")]
    public string? Bio { get; set; }

    [StringLength(100, ErrorMessage = "Home area must be at most 100 characters.")]
    public string? HomeArea { get; set; }

    [StringLength(100, ErrorMessage = "Work or study area must be at most 100 characters.")]
    public string? WorkArea { get; set; }
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
