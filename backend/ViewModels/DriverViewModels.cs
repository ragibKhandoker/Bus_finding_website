using System.ComponentModel.DataAnnotations;
using SmartCommute.Server.Models;
using SmartCommute.Server.Services;

namespace SmartCommute.Server.ViewModels;

// ---- Requests ----

// The application details a driver fills in. Used for the first registration (where everything required must be
// there) and for a resubmission (where only the sections the admin asked for are read). The server checks every
// rule again in DriverRules; the lengths here only stop oversized input early.
public class DriverApplicationInput
{
    [StringLength(100)] public string? FullName { get; set; }
    [StringLength(30)] public string? Phone { get; set; }
    // yyyy-MM-dd
    [StringLength(10)] public string? DateOfBirth { get; set; }
    [StringLength(30)] public string? Gender { get; set; }

    [StringLength(40)] public string? LicenseNumber { get; set; }
    [StringLength(30)] public string? LicenseType { get; set; }
    [StringLength(10)] public string? LicenseExpiryDate { get; set; }
    public int? DrivingExperienceYears { get; set; }

    [StringLength(30)] public string? NidNumber { get; set; }

    // A bus from the database, when the driver found theirs; otherwise the typed details below.
    public Guid? BusId { get; set; }
    [StringLength(100)] public string? BusName { get; set; }
    [StringLength(100)] public string? OperatorName { get; set; }
    [StringLength(200)] public string? Route { get; set; }
    [StringLength(40)] public string? BusRegistrationNumber { get; set; }
    [StringLength(20)] public string? EmploymentType { get; set; }

    [StringLength(300)] public string? PresentAddress { get; set; }
    [StringLength(60)] public string? District { get; set; }
    [StringLength(100)] public string? Area { get; set; }
    [StringLength(30)] public string? EmergencyContact { get; set; }

    // Document type (license_front, license_back, nid_front, nid_back, profile_photo) → "data:image/jpeg;base64,…".
    public Dictionary<string, string>? Documents { get; set; }
}

public class DriverRegistrationRequest : DriverApplicationInput
{
    [Required(ErrorMessage = "Please enter your email address.")]
    [StringLength(255, ErrorMessage = "Email must be at most 255 characters.")]
    [EmailAddress(ErrorMessage = "Please enter a valid email address.")]
    [RegularExpression(@"^\s*[^@\s]+@[^@\s]+\.[^@\s]+\s*$", ErrorMessage = "Please enter a valid email address.")]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = "Please choose a password.")]
    [StringLength(128, ErrorMessage = "Password is too long.")]
    public string Password { get; set; } = "";

    public string? ConfirmPassword { get; set; }

    public bool ConfirmAccuracy { get; set; }
    public bool AuthorizeVerification { get; set; }
    public bool AcceptTerms { get; set; }
}

// Contact details a driver may change at any time; everything that is verified goes through a resubmission.
public class DriverProfileUpdateRequest
{
    [StringLength(30)] public string? Phone { get; set; }
    [StringLength(30)] public string? EmergencyContact { get; set; }
}

public class DriverReviewRequest
{
    [StringLength(1000, ErrorMessage = "Keep the note under 1000 characters.")]
    public string? Reason { get; set; }
}

public class DriverResubmissionRequest
{
    [StringLength(1000, ErrorMessage = "Keep the reason under 1000 characters.")]
    public string? Reason { get; set; }

    public List<string>? Fields { get; set; }
}

public class AssignBusRequest
{
    [Required(ErrorMessage = "Choose a bus.")]
    public Guid? BusId { get; set; }
}

// ---- Responses ----

public record DriverAssignmentViewModel(string Id, string BusId, string BusName, string? RouteNumber, string? Operator, DateTime AssignedAt,
    bool IsActive, DateTime? EndedAt, string? VehicleId = null, string? VehicleCode = null, string? BusType = null)
{
    public static DriverAssignmentViewModel From(DriverBusAssignment a) => new(a.Id.ToString(), a.BusId.ToString(), a.Bus?.BusName ?? "",
        a.Bus?.RouteNumber, a.Bus?.Operator, a.AssignedAt, a.IsActive, a.EndedAt, a.IndividualBusId?.ToString(), a.IndividualBus?.BusCode,
        a.IndividualBus?.BusType ?? a.Bus?.BusType);
}

// Where a driver's application stands, for the driver's own status page.
public record DriverStatusViewModel(
    bool Registered, string? ApplicationId, string? ApplicationCode, string? VerificationStatus, string? AccountStatus,
    DateTime? SubmittedAt, DateTime? ReviewedAt, string? RejectionReason, string? ResubmissionReason, string[] RequestedFields,
    bool CanResubmit, bool CanAccessDashboard, bool HasBusAssignment, string NextStep, List<DriverAssignmentViewModel> Assignments)
{
    public static DriverStatusViewModel NotRegistered => new(false, null, null, null, null, null, null, null, null, [], false, false, false,
        "This driver account has no registration on file. Contact support to complete your driver verification.", []);

    public static DriverStatusViewModel From(DriverProfile p, List<DriverBusAssignment> assignments)
    {
        var active = assignments.Where(a => a.IsActive).ToList();
        var approved = p.VerificationStatus == Models.DriverVerificationStatus.Approved && p.AccountStatus == DriverAccountStatus.Active;
        var next = p.VerificationStatus switch
        {
            Models.DriverVerificationStatus.Pending => "An administrator will review your documents. You will get a notification when they do.",
            Models.DriverVerificationStatus.UnderReview => "An administrator is checking your documents now. No action is needed from you.",
            Models.DriverVerificationStatus.ResubmissionRequired => "Correct the items the administrator asked for and resubmit your application.",
            Models.DriverVerificationStatus.Rejected => "Contact support if you would like to know more or believe this is a mistake.",
            _ when active.Count == 0 => "Wait for an administrator to assign you to a bus before using bus operations.",
            _ => "You are verified and assigned to a bus.",
        };
        return new(true, p.Id.ToString(), p.ApplicationCode, p.VerificationStatus, p.AccountStatus, p.SubmittedAt, p.ReviewedAt,
            p.VerificationStatus == Models.DriverVerificationStatus.Rejected ? p.RejectionReason : null,
            p.VerificationStatus == Models.DriverVerificationStatus.ResubmissionRequired ? p.ResubmissionReason : null,
            p.VerificationStatus == Models.DriverVerificationStatus.ResubmissionRequired ? p.RequestedFields : [],
            p.VerificationStatus == Models.DriverVerificationStatus.ResubmissionRequired, approved, active.Count > 0, next,
            assignments.Select(DriverAssignmentViewModel.From).ToList());
    }
}

// A document's metadata. Url is an API path that needs an admin's token, never a public file link.
public record DriverDocumentViewModel(string Type, string ContentType, long FileSize, DateTime UploadedAt, string? Url);

public record DriverHistoryViewModel(string Id, string? PreviousStatus, string NewStatus, string? Reason, string? AdminName, bool ByAdmin, DateTime CreatedAt)
{
    public static DriverHistoryViewModel From(DriverVerificationHistory h) =>
        new(h.Id.ToString(), h.PreviousStatus, h.NewStatus, h.Reason, h.Admin?.Name, h.AdminId is not null, h.CreatedAt);
}

// The full application. Licence and NID numbers are always masked; documents are references only.
public class DriverApplicationViewModel
{
    public string DriverId { get; init; } = "";
    public string ApplicationId { get; init; } = "";
    public string ApplicationCode { get; init; } = "";
    public string FullName { get; init; } = "";
    public string Email { get; init; } = "";
    public string Phone { get; init; } = "";
    public string DateOfBirth { get; init; } = "";
    public int Age { get; init; }
    public string Gender { get; init; } = "";
    public string LicenseNumberMasked { get; init; } = "";
    public string LicenseType { get; init; } = "";
    public string LicenseExpiryDate { get; init; } = "";
    public int DrivingExperienceYears { get; init; }
    public string NidNumberMasked { get; init; } = "";
    public string PresentAddress { get; init; } = "";
    public string District { get; init; } = "";
    public string Area { get; init; } = "";
    public string EmergencyContact { get; init; } = "";
    public string? ClaimedBusId { get; init; }
    public bool ClaimedBusInDatabase { get; init; }
    public string ClaimedBusName { get; init; } = "";
    public string ClaimedOperatorName { get; init; } = "";
    public string ClaimedRoute { get; init; } = "";
    public string BusRegistrationNumber { get; init; } = "";
    public string EmploymentType { get; init; } = "";
    public string VerificationStatus { get; init; } = "";
    public string AccountStatus { get; init; } = "";
    public string? RejectionReason { get; init; }
    public string? ResubmissionReason { get; init; }
    public string[] RequestedFields { get; init; } = [];
    public DateTime SubmittedAt { get; init; }
    public DateTime? ReviewedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    public List<DriverDocumentViewModel> Documents { get; init; } = [];
    public List<DriverHistoryViewModel> History { get; init; } = [];
    public List<DriverAssignmentViewModel> Assignments { get; init; } = [];
    public string[] AllowedActions { get; init; } = [];

    // forAdmin: documents carry the admin API path to fetch them; the driver only sees what was uploaded.
    public static DriverApplicationViewModel From(DriverProfile p, List<DriverBusAssignment> assignments, bool forAdmin, DateOnly today) => new()
    {
        DriverId = p.UserId.ToString(),
        ApplicationId = p.Id.ToString(),
        ApplicationCode = p.ApplicationCode,
        FullName = p.User?.Name ?? "",
        Email = p.User?.Email ?? "",
        Phone = p.User?.Phone ?? "",
        DateOfBirth = p.DateOfBirth.ToString("yyyy-MM-dd"),
        Age = DriverRules.AgeOn(p.DateOfBirth, today),
        Gender = p.Gender,
        LicenseNumberMasked = SensitiveDataProtector.Mask(p.LicenseNumberLast4),
        LicenseType = p.LicenseType,
        LicenseExpiryDate = p.LicenseExpiryDate.ToString("yyyy-MM-dd"),
        DrivingExperienceYears = p.DrivingExperienceYears,
        NidNumberMasked = SensitiveDataProtector.Mask(p.NidNumberLast4),
        PresentAddress = p.PresentAddress,
        District = p.District,
        Area = p.Area,
        EmergencyContact = p.EmergencyContact ?? "",
        ClaimedBusId = p.ClaimedBusId?.ToString(),
        ClaimedBusInDatabase = p.ClaimedBusId is not null,
        ClaimedBusName = p.ClaimedBusName,
        ClaimedOperatorName = p.ClaimedOperatorName,
        ClaimedRoute = p.ClaimedRoute,
        BusRegistrationNumber = p.BusRegistrationNumber ?? "",
        EmploymentType = p.EmploymentType,
        VerificationStatus = p.VerificationStatus,
        AccountStatus = p.AccountStatus,
        RejectionReason = p.RejectionReason,
        ResubmissionReason = p.ResubmissionReason,
        RequestedFields = p.RequestedFields,
        SubmittedAt = p.SubmittedAt,
        ReviewedAt = p.ReviewedAt,
        UpdatedAt = p.UpdatedAt,
        Documents = p.Documents.OrderBy(d => Array.IndexOf(DriverDocumentTypes.All, d.DocumentType))
            .Select(d => new DriverDocumentViewModel(d.DocumentType, d.ContentType, d.FileSize, d.UploadedAt,
                forAdmin ? $"/api/admin/drivers/{p.UserId}/documents/{d.DocumentType}" : null)).ToList(),
        History = p.History.OrderBy(h => h.CreatedAt).Select(DriverHistoryViewModel.From).ToList(),
        Assignments = assignments.Select(DriverAssignmentViewModel.From).ToList(),
        AllowedActions = forAdmin ? Models.DriverVerificationStatus.AdminNext.GetValueOrDefault(p.VerificationStatus) ?? [] : [],
    };
}

// One row of the admin's application table.
public record DriverApplicationRowViewModel(string DriverId, string ApplicationCode, string FullName, string Email, string Phone,
    string LicenseNumberMasked, string BusName, string OperatorName, DateTime SubmittedAt, string VerificationStatus)
{
    public static DriverApplicationRowViewModel From(DriverProfile p) => new(p.UserId.ToString(), p.ApplicationCode, p.User?.Name ?? "",
        p.User?.Email ?? "", p.User?.Phone ?? "", SensitiveDataProtector.Mask(p.LicenseNumberLast4), p.ClaimedBusName, p.ClaimedOperatorName,
        p.SubmittedAt, p.VerificationStatus);
}
