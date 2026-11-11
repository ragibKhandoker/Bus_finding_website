using System.Globalization;
using System.Text.RegularExpressions;
using SmartCommute.Server.Models;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Server.Services;

// The checks a driver application must pass, as pure functions so they can be tested without a database. Each
// returns null when the input is fine, or a message for the person filling in the form.
public static partial class DriverRules
{
    // The 64 districts of Bangladesh.
    public static readonly string[] Districts =
    [
        "Bagerhat", "Bandarban", "Barguna", "Barishal", "Bhola", "Bogura", "Brahmanbaria", "Chandpur", "Chapainawabganj", "Chattogram",
        "Chuadanga", "Cox's Bazar", "Cumilla", "Dhaka", "Dinajpur", "Faridpur", "Feni", "Gaibandha", "Gazipur", "Gopalganj",
        "Habiganj", "Jamalpur", "Jashore", "Jhalokati", "Jhenaidah", "Joypurhat", "Khagrachhari", "Khulna", "Kishoreganj", "Kurigram",
        "Kushtia", "Lakshmipur", "Lalmonirhat", "Madaripur", "Magura", "Manikganj", "Meherpur", "Moulvibazar", "Munshiganj", "Mymensingh",
        "Naogaon", "Narail", "Narayanganj", "Narsingdi", "Natore", "Netrokona", "Nilphamari", "Noakhali", "Pabna", "Panchagarh",
        "Patuakhali", "Pirojpur", "Rajbari", "Rajshahi", "Rangamati", "Rangpur", "Satkhira", "Shariatpur", "Sherpur", "Sirajganj",
        "Sunamganj", "Sylhet", "Tangail", "Thakurgaon",
    ];

    public const int MinPasswordLength = 8;
    public const int MaxExperienceYears = 60;
    public const int MaxAge = 80;

    [GeneratedRegex(@"^01[3-9]\d{8}$")]
    private static partial Regex LocalMobile();

    // A Bangladesh mobile number in its local form "01XXXXXXXXX", from "+880 1712-345678", "8801712345678" or
    // "01712345678"; null when it is not one.
    public static string? NormalizePhone(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        var digits = new string(value.Where(c => !char.IsWhiteSpace(c) && c is not ('-' or '(' or ')')).ToArray());
        if (digits.StartsWith('+'))
        {
            digits = digits[1..];
        }
        if (digits.StartsWith("880"))
        {
            digits = digits[2..];
        }
        return digits.All(char.IsAsciiDigit) && LocalMobile().IsMatch(digits) ? digits : null;
    }

    public static DateOnly? ParseDate(string? value) =>
        DateOnly.TryParseExact(value?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;

    public static int AgeOn(DateOnly birth, DateOnly today)
    {
        var age = today.Year - birth.Year;
        return birth > today.AddYears(-age) ? age - 1 : age;
    }

    public static DateOnly Today(DateTime utcNow) => DateOnly.FromDateTime(DhakaTime.FromUtc(utcNow));

    public static string? Match(IEnumerable<string> allowed, string? value) =>
        allowed.FirstOrDefault(a => string.Equals(a, value?.Trim(), StringComparison.OrdinalIgnoreCase));

    public static string? PasswordProblem(string password, string? confirm)
    {
        if (password.Length < MinPasswordLength)
        {
            return $"Password must be at least {MinPasswordLength} characters.";
        }
        if (PasswordHashing.IsTooLong(password))
        {
            return "Password is too long. Use at most 72 characters.";
        }
        if (!password.Any(char.IsLetter) || !password.Any(char.IsDigit))
        {
            return "Password must contain at least one letter and one number.";
        }
        if (password.Trim().Length != password.Length)
        {
            return "Password cannot start or end with a space.";
        }
        if (confirm is not null && confirm != password)
        {
            return "Passwords do not match.";
        }
        return null;
    }

    // ---- Sections. Each checks one part of the form and returns the cleaned values. ----

    public record Personal(string Name, string Phone, DateOnly DateOfBirth, string Gender);

    public static (Personal? Value, string? Problem) CheckPersonal(DriverApplicationInput r, DateOnly today, int minimumAge)
    {
        var name = r.FullName?.Trim() ?? "";
        if (name.Length == 0)
        {
            return (null, "Please enter your full name.");
        }
        if (name.Length is < 2 or > 100)
        {
            return (null, "Name must be between 2 and 100 characters.");
        }
        if (NormalizePhone(r.Phone) is not { } phone)
        {
            return (null, "Enter a valid Bangladesh mobile number, like 01712-345678.");
        }
        if (ParseDate(r.DateOfBirth) is not { } birth)
        {
            return (null, "Enter your date of birth.");
        }
        if (birth > today)
        {
            return (null, "Date of birth cannot be in the future.");
        }
        var age = AgeOn(birth, today);
        if (age < minimumAge)
        {
            return (null, $"Drivers must be at least {minimumAge} years old.");
        }
        if (age > MaxAge)
        {
            return (null, "Check your date of birth.");
        }
        if (string.IsNullOrWhiteSpace(r.Gender) || UserGenders.Normalize(r.Gender) is not { } gender)
        {
            return (null, $"Gender must be one of: {string.Join(", ", UserGenders.All)}.");
        }
        return (new Personal(name, phone, birth, gender), null);
    }

    public record Driving(string LicenseNumber, string LicenseType, DateOnly Expiry, int ExperienceYears);

    public static (Driving? Value, string? Problem) CheckDriving(DriverApplicationInput r, DateOnly today, string licensePattern, DateOnly? birth = null)
    {
        var number = SensitiveDataProtector.Normalize(r.LicenseNumber ?? "");
        if (number.Length == 0)
        {
            return (null, "Enter your driving licence number.");
        }
        if (!Regex.IsMatch(number, licensePattern, RegexOptions.None, TimeSpan.FromMilliseconds(200)))
        {
            return (null, "Enter the driving licence number exactly as it is printed (letters and digits, 6 to 25 characters).");
        }
        if (Match(DriverLicenseTypes.All, r.LicenseType) is not { } type)
        {
            return (null, "Choose the licence type: Professional, Heavy Vehicle or Other.");
        }
        if (ParseDate(r.LicenseExpiryDate) is not { } expiry)
        {
            return (null, "Enter the licence expiry date.");
        }
        if (expiry <= today)
        {
            return (null, "Your driving licence has expired. The expiry date must be in the future.");
        }
        if (expiry > today.AddYears(20))
        {
            return (null, "Check the licence expiry date.");
        }
        if (r.DrivingExperienceYears is not { } years)
        {
            return (null, "Enter your driving experience in years.");
        }
        if (years < 0)
        {
            return (null, "Driving experience cannot be negative.");
        }
        if (years > MaxExperienceYears)
        {
            return (null, $"Driving experience must be at most {MaxExperienceYears} years.");
        }
        if (birth is { } b && years > Math.Max(0, AgeOn(b, today) - 16))
        {
            return (null, "Driving experience is longer than your age allows. Check the years you entered.");
        }
        return (new Driving(number, type, expiry, years), null);
    }

    public static (string? Value, string? Problem) CheckNid(DriverApplicationInput r, string nidPattern)
    {
        var nid = SensitiveDataProtector.Normalize(r.NidNumber ?? "");
        if (nid.Length == 0)
        {
            return (null, "Enter your NID number.");
        }
        if (!Regex.IsMatch(nid, nidPattern, RegexOptions.None, TimeSpan.FromMilliseconds(200)))
        {
            return (null, "Enter a valid NID number (10, 13 or 17 digits).");
        }
        return (nid, null);
    }

    public record Address(string PresentAddress, string District, string Area, string? EmergencyContact);

    public static (Address? Value, string? Problem) CheckAddress(DriverApplicationInput r)
    {
        var address = r.PresentAddress?.Trim() ?? "";
        if (address.Length < 5)
        {
            return (null, "Enter your present address (at least 5 characters).");
        }
        if (Match(Districts, r.District) is not { } district)
        {
            return (null, "Choose your district from the list.");
        }
        var area = r.Area?.Trim() ?? "";
        if (area.Length is < 2 or > 100)
        {
            return (null, "Enter your area or thana.");
        }
        string? emergency = null;
        if (!string.IsNullOrWhiteSpace(r.EmergencyContact))
        {
            emergency = NormalizePhone(r.EmergencyContact);
            if (emergency is null)
            {
                return (null, "Enter a valid emergency contact number, like 01712-345678.");
            }
        }
        return (new Address(address, district, area, emergency), null);
    }

    public record Employment(Guid? BusId, string BusName, string OperatorName, string Route, string? RegistrationNumber, string EmploymentType);

    // bus: the bus row for r.BusId, already looked up (null when none was chosen or it does not exist).
    public static (Employment? Value, string? Problem) CheckEmployment(DriverApplicationInput r, Bus? bus)
    {
        if (r.BusId is not null && bus is null)
        {
            return (null, "That bus was not found. Choose it again or type its details.");
        }
        if (Match(DriverEmploymentTypes.All, r.EmploymentType) is not { } employment)
        {
            return (null, "Choose your employment type.");
        }
        var registration = string.IsNullOrWhiteSpace(r.BusRegistrationNumber) ? null : r.BusRegistrationNumber.Trim();
        if (registration is { Length: < 3 })
        {
            return (null, "Enter the full bus registration number, or leave it empty.");
        }
        if (bus is not null)
        {
            // Details come from the database, never from what the client sent for a chosen bus.
            var route = bus.Stops.Count >= 2
                ? $"{bus.Stops.OrderBy(s => s.Order).First().Name} – {bus.Stops.OrderBy(s => s.Order).Last().Name}"
                : bus.RouteNumber ?? "";
            var routeText = string.IsNullOrWhiteSpace(bus.RouteNumber) ? route : $"{bus.RouteNumber} · {route}";
            return (new Employment(bus.Id, bus.BusName, bus.Operator ?? r.OperatorName?.Trim() ?? bus.BusName, routeText.Length > 200 ? routeText[..200] : routeText,
                registration, employment), null);
        }
        var name = r.BusName?.Trim() ?? "";
        if (name.Length < 2)
        {
            return (null, "Enter the bus name.");
        }
        var op = r.OperatorName?.Trim() ?? "";
        if (op.Length < 2)
        {
            return (null, "Enter the bus operator or company name.");
        }
        var typedRoute = r.Route?.Trim() ?? "";
        if (typedRoute.Length < 2)
        {
            return (null, "Enter the route the bus runs on.");
        }
        return (new Employment(null, name, op, typedRoute, registration, employment), null);
    }

    // The decoded documents of the given types, after checking each one. required: types that must be present.
    public static (List<(string Type, DecodedImage Image)>? Value, string? Problem) CheckDocuments(
        Dictionary<string, string>? documents, IReadOnlyCollection<string> allowed, IReadOnlyCollection<string> required, int maxBytes)
    {
        var result = new List<(string, DecodedImage)>();
        var given = documents ?? [];
        foreach (var key in given.Keys)
        {
            if (!DriverDocumentTypes.All.Contains(key))
            {
                return (null, "Unknown document type.");
            }
        }
        foreach (var type in required)
        {
            if (!given.TryGetValue(type, out var data) || string.IsNullOrWhiteSpace(data))
            {
                return (null, $"Upload the {Label(type)}.");
            }
        }
        foreach (var (type, data) in given)
        {
            if (!allowed.Contains(type) || string.IsNullOrWhiteSpace(data))
            {
                continue;
            }
            try
            {
                result.Add((type, ImageStorageService.Decode(data, maxBytes)));
            }
            catch (ArgumentException ex)
            {
                return (null, $"{char.ToUpperInvariant(Label(type)[0])}{Label(type)[1..]}: {ex.Message}");
            }
        }
        return (result, null);
    }

    public static string Label(string type) => type switch
    {
        DriverDocumentTypes.LicenseFront => "front of your driving licence",
        DriverDocumentTypes.LicenseBack => "back of your driving licence",
        DriverDocumentTypes.NidFront => "front of your NID",
        DriverDocumentTypes.NidBack => "back of your NID",
        DriverDocumentTypes.ProfilePhoto => "profile picture",
        DriverResubmissionFields.Personal => "personal information",
        DriverResubmissionFields.DrivingDetails => "driving licence details",
        DriverResubmissionFields.NidNumber => "NID number",
        DriverResubmissionFields.Employment => "bus and employment information",
        DriverResubmissionFields.Address => "address",
        _ => type,
    };
}
