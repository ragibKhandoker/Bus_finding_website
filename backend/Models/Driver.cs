using System.ComponentModel.DataAnnotations;

namespace SmartCommute.Server.Models;

// F21: driver registration and verification. A driver is an ordinary User with the "driver" role plus one
// DriverProfile. The profile starts PENDING; only an admin moves it on, and every move is written to
// driver_verification_history. Bus assignments are separate rows that only an admin creates.

public static class DriverVerificationStatus
{
    public const string Pending = "PENDING";
    public const string UnderReview = "UNDER_REVIEW";
    public const string Approved = "APPROVED";
    public const string Rejected = "REJECTED";
    public const string ResubmissionRequired = "RESUBMISSION_REQUIRED";

    public static readonly string[] All = [Pending, UnderReview, Approved, Rejected, ResubmissionRequired];

    // Where an admin may move an application from each status. A driver's resubmission moves
    // RESUBMISSION_REQUIRED back to PENDING (see DriverRegistrationService.ResubmitAsync); admins cannot do that.
    public static readonly Dictionary<string, string[]> AdminNext = new()
    {
        [Pending] = [UnderReview, Approved, Rejected, ResubmissionRequired],
        [UnderReview] = [Approved, Rejected, ResubmissionRequired],
        [ResubmissionRequired] = [Rejected],
        [Approved] = [],
        [Rejected] = [],
    };

    public static bool AdminCanMove(string from, string to) => AdminNext.TryGetValue(from, out var next) && next.Contains(to);

    // The account status that goes with each verification status.
    public static string AccountStatusFor(string status) => status switch
    {
        Approved => DriverAccountStatus.Active,
        Rejected => DriverAccountStatus.Rejected,
        _ => DriverAccountStatus.PendingVerification,
    };
}

public static class DriverAccountStatus
{
    public const string PendingVerification = "PENDING_VERIFICATION";
    public const string Active = "ACTIVE";
    public const string Rejected = "REJECTED";

    public static readonly string[] All = [PendingVerification, Active, Rejected];
}

public static class DriverLicenseTypes
{
    public static readonly string[] All = ["Professional", "Heavy Vehicle", "Other"];
}

public static class DriverEmploymentTypes
{
    public static readonly string[] All = ["Full-time", "Part-time", "Contractual", "Self-employed"];
}

public static class DriverDocumentTypes
{
    public const string LicenseFront = "license_front";
    public const string LicenseBack = "license_back";
    public const string NidFront = "nid_front";
    public const string NidBack = "nid_back";
    public const string ProfilePhoto = "profile_photo";

    public static readonly string[] All = [LicenseFront, LicenseBack, NidFront, NidBack, ProfilePhoto];
    public static readonly string[] Required = [LicenseFront, NidFront];
}

// The parts of an application an admin can ask a driver to correct. A resubmission may only change these.
public static class DriverResubmissionFields
{
    public const string Personal = "personal";
    public const string DrivingDetails = "driving_details";
    public const string NidNumber = "nid_number";
    public const string Employment = "employment";
    public const string Address = "address";

    public static readonly string[] Details = [Personal, DrivingDetails, NidNumber, Employment, Address];

    // Each document can also be asked for again on its own.
    public static readonly string[] All = [.. Details, .. DriverDocumentTypes.All];
}

public class DriverProfile
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }
    public User? User { get; set; }

    public DateOnly DateOfBirth { get; set; }

    [MaxLength(30)]
    public string Gender { get; set; } = UserGenders.Unspecified;

    // The licence and NID numbers are encrypted at rest (see SensitiveDataProtector). The hash is a keyed HMAC of
    // the normalised number, so a second application with the same document can be refused without decrypting
    // anything; Last4 is kept for the masked display ("••••••1234").
    [MaxLength(300)]
    public string LicenseNumberEncrypted { get; set; } = "";

    [MaxLength(64)]
    public string LicenseNumberHash { get; set; } = "";

    [MaxLength(4)]
    public string LicenseNumberLast4 { get; set; } = "";

    [MaxLength(30)]
    public string LicenseType { get; set; } = "";

    public DateOnly LicenseExpiryDate { get; set; }

    public int DrivingExperienceYears { get; set; }

    [MaxLength(300)]
    public string NidNumberEncrypted { get; set; } = "";

    [MaxLength(64)]
    public string NidNumberHash { get; set; } = "";

    [MaxLength(4)]
    public string NidNumberLast4 { get; set; } = "";

    [MaxLength(300)]
    public string PresentAddress { get; set; } = "";

    [MaxLength(60)]
    public string District { get; set; } = "";

    [MaxLength(100)]
    public string Area { get; set; } = "";

    [MaxLength(20)]
    public string? EmergencyContact { get; set; }

    // The bus the driver says they work on: only a claim. A real assignment is a DriverBusAssignment made by an admin.
    public Guid? ClaimedBusId { get; set; }
    public Bus? ClaimedBus { get; set; }

    [MaxLength(100)]
    public string ClaimedBusName { get; set; } = "";

    [MaxLength(100)]
    public string ClaimedOperatorName { get; set; } = "";

    [MaxLength(200)]
    public string ClaimedRoute { get; set; } = "";

    [MaxLength(40)]
    public string? BusRegistrationNumber { get; set; }

    [MaxLength(20)]
    public string EmploymentType { get; set; } = "";

    [MaxLength(30)]
    public string VerificationStatus { get; set; } = Models.DriverVerificationStatus.Pending;

    [MaxLength(30)]
    public string AccountStatus { get; set; } = DriverAccountStatus.PendingVerification;

    [MaxLength(1000)]
    public string? RejectionReason { get; set; }

    [MaxLength(1000)]
    public string? ResubmissionReason { get; set; }

    // Comma-separated DriverResubmissionFields values the admin asked to be corrected.
    [MaxLength(300)]
    public string? ResubmissionFields { get; set; }

    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReviewedAt { get; set; }
    public Guid? ReviewedById { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Two admins reviewing the same application at once cannot both be saved.
    public uint Version { get; set; }

    public List<DriverDocument> Documents { get; set; } = [];
    public List<DriverVerificationHistory> History { get; set; } = [];

    public string ApplicationCode => $"DRV-{Id.ToString("N")[..8].ToUpperInvariant()}";

    public string[] RequestedFields => string.IsNullOrWhiteSpace(ResubmissionFields)
        ? []
        : ResubmissionFields.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

// A verification document. The file is kept in private storage (never under wwwroot) with a random name and is only
// served, through the admin API, to admins.
public class DriverDocument
{
    public Guid Id { get; set; }
    public Guid DriverProfileId { get; set; }
    public DriverProfile? DriverProfile { get; set; }

    [MaxLength(20)]
    public string DocumentType { get; set; } = "";

    [MaxLength(100)]
    public string PrivateStorageKey { get; set; } = "";

    [MaxLength(30)]
    public string ContentType { get; set; } = "";

    public long FileSize { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
}

// Every step of an application: the submission, each admin decision and each resubmission.
public class DriverVerificationHistory
{
    public Guid Id { get; set; }
    public Guid DriverProfileId { get; set; }
    public DriverProfile? DriverProfile { get; set; }

    // The admin who made the decision; null for steps the driver made (submitting, resubmitting).
    public Guid? AdminId { get; set; }
    public User? Admin { get; set; }

    [MaxLength(30)]
    public string? PreviousStatus { get; set; }

    [MaxLength(30)]
    public string NewStatus { get; set; } = "";

    [MaxLength(1000)]
    public string? Reason { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// A bus an admin has assigned to an approved driver. Only admins create these; a driver's claim never does.
public class DriverBusAssignment
{
    public Guid Id { get; set; }

    public Guid DriverId { get; set; }
    public User? Driver { get; set; }

    public Guid BusId { get; set; }
    public Bus? Bus { get; set; }

    // F22: the vehicle on that route the driver drives, when the admin named one. Its code is the registration
    // shown to the driver.
    public Guid? IndividualBusId { get; set; }
    public IndividualBus? IndividualBus { get; set; }

    public Guid? AssignedByAdminId { get; set; }
    public User? AssignedByAdmin { get; set; }

    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;
    public DateTime? EndedAt { get; set; }
    public Guid? EndedByAdminId { get; set; }
}
