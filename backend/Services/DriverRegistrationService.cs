using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Server.Services;

public record DriverRegistered(SignedIn SignedIn, DriverProfile Profile);

// F21, the driver's side: registering, reading their own application, changing contact details and resubmitting
// after an admin asked for corrections. The driver is always the user in the token; the role, the status and any
// bus assignment are set here on the server and never read from the request.
//
// Documents are checked (type, real image bytes, size) before anything is written. Files are written to private
// storage and removed again if the database save fails, so a failed registration leaves nothing behind.
// Licence and NID numbers are stored encrypted and never logged.
public class DriverRegistrationService(
    AppDbContext db,
    AccountService accounts,
    FriendCodeGenerator friendCodes,
    SensitiveDataProtector protector,
    IPrivateDocumentStore store,
    NotificationService notifications,
    IOptions<DriverVerificationOptions> options)
{
    public const string LicensePurpose = "license";
    public const string NidPurpose = "nid";
    public const string DuplicateDocumentMessage =
        "These licence or NID details are already registered with another driver application. Contact support if you think this is a mistake.";

    private DriverVerificationOptions Options => options.Value;

    // ---- Registering ----

    public async Task<ServiceResult<DriverRegistered>> RegisterAsync(DriverRegistrationRequest r, ClientInfo client, DateTime? nowUtc = null)
    {
        var now = nowUtc ?? DateTime.UtcNow;
        var today = DriverRules.Today(now);

        // Every check first, so nothing is stored for a form that cannot be accepted.
        if (!r.ConfirmAccuracy || !r.AuthorizeVerification || !r.AcceptTerms)
        {
            return ServiceResult<DriverRegistered>.BadRequest("Please tick all three declarations to submit your application.");
        }
        var (personal, problem) = DriverRules.CheckPersonal(r, today, Options.MinimumAge);
        if (problem is not null)
        {
            return ServiceResult<DriverRegistered>.BadRequest(problem);
        }
        var email = r.Email.Trim().ToLowerInvariant();
        if (DriverRules.PasswordProblem(r.Password, r.ConfirmPassword ?? "") is { } passwordProblem)
        {
            return ServiceResult<DriverRegistered>.BadRequest(passwordProblem);
        }
        var (driving, drivingProblem) = DriverRules.CheckDriving(r, today, Options.LicensePattern, personal!.DateOfBirth);
        if (drivingProblem is not null)
        {
            return ServiceResult<DriverRegistered>.BadRequest(drivingProblem);
        }
        var (nid, nidProblem) = DriverRules.CheckNid(r, Options.NidPattern);
        if (nidProblem is not null)
        {
            return ServiceResult<DriverRegistered>.BadRequest(nidProblem);
        }
        var bus = r.BusId is { } busId ? await db.Buses.Include(b => b.Stops).AsNoTracking().FirstOrDefaultAsync(b => b.Id == busId) : null;
        var (employment, employmentProblem) = DriverRules.CheckEmployment(r, bus);
        if (employmentProblem is not null)
        {
            return ServiceResult<DriverRegistered>.BadRequest(employmentProblem);
        }
        var (address, addressProblem) = DriverRules.CheckAddress(r);
        if (addressProblem is not null)
        {
            return ServiceResult<DriverRegistered>.BadRequest(addressProblem);
        }
        var (documents, documentProblem) = DriverRules.CheckDocuments(r.Documents, DriverDocumentTypes.All, DriverDocumentTypes.Required, Options.MaxImageBytes);
        if (documentProblem is not null)
        {
            return ServiceResult<DriverRegistered>.BadRequest(documentProblem);
        }

        if (await db.Users.AnyAsync(u => u.Email == email))
        {
            return ServiceResult<DriverRegistered>.Conflict("An account with this email already exists.");
        }
        var licenseHash = protector.Hash(LicensePurpose, driving!.LicenseNumber);
        var nidHash = protector.Hash(NidPurpose, nid!);
        if (await db.DriverProfiles.AnyAsync(p => p.LicenseNumberHash == licenseHash || p.NidNumberHash == nidHash))
        {
            return ServiceResult<DriverRegistered>.Conflict(DuplicateDocumentMessage);
        }

        var user = new User
        {
            Id = Guid.NewGuid(),
            Name = personal.Name,
            Email = email,
            Phone = personal.Phone,
            Gender = personal.Gender,
            Role = UserRoles.Driver,
            PasswordHash = PasswordHashing.Hash(r.Password),
            FriendCode = await friendCodes.GenerateUniqueAsync(),
            CreatedAt = now,
            UpdatedAt = now,
        };
        var profile = new DriverProfile
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            User = user,
            DateOfBirth = personal.DateOfBirth,
            Gender = personal.Gender,
            VerificationStatus = DriverVerificationStatus.Pending,
            AccountStatus = DriverAccountStatus.PendingVerification,
            SubmittedAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        };
        ApplyDriving(profile, driving);
        ApplyNid(profile, nid!);
        ApplyEmployment(profile, employment!);
        ApplyAddress(profile, address!);
        profile.History.Add(new DriverVerificationHistory
        {
            PreviousStatus = null,
            NewStatus = DriverVerificationStatus.Pending,
            Reason = "Application submitted.",
            CreatedAt = now,
        });

        var written = new List<string>();
        try
        {
            foreach (var (type, image) in documents!)
            {
                var key = await store.SaveAsync(image.Bytes.AsMemory(0, image.Length), image.Extension);
                written.Add(key);
                profile.Documents.Add(new DriverDocument
                {
                    DocumentType = type,
                    PrivateStorageKey = key,
                    ContentType = image.ContentType,
                    FileSize = image.Length,
                    UploadedAt = now,
                });
            }

            db.Users.Add(user);
            db.Wallets.Add(new Wallet { User = user });
            db.DriverProfiles.Add(profile);
            await NotifyAdminsAsync("New driver application",
                $"{user.Name} applied to drive with SmartCommute ({profile.ApplicationCode}). Review their documents.", user.Id, profile.Id);

            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    await db.SaveChangesAsync();
                    break;
                }
                catch (DbUpdateException ex) when (FriendCodeGenerator.IsCodeClash(ex) && attempt < 5)
                {
                    user.FriendCode = await friendCodes.GenerateUniqueAsync();
                }
            }
        }
        catch (DbUpdateException ex) when (UniqueViolation(ex) is { } constraint)
        {
            // Another registration with the same email or documents won the race between the checks and the insert.
            Cleanup(written);
            db.ChangeTracker.Clear();
            return ServiceResult<DriverRegistered>.Conflict(constraint.Contains("email")
                ? "An account with this email already exists."
                : DuplicateDocumentMessage);
        }
        catch
        {
            Cleanup(written);
            throw;
        }

        // Signed straight in, so the driver lands on their verification status page.
        var signedIn = await accounts.StartSessionAsync(user, client);
        return ServiceResult<DriverRegistered>.Created(new DriverRegistered(signedIn, profile),
            "Your driver registration has been submitted. An administrator will review your documents.");
    }

    // ---- Reading ----

    public Task<DriverProfile?> ProfileOfAsync(Guid userId) => db.DriverProfiles
        .Include(p => p.User).Include(p => p.Documents).Include(p => p.History).ThenInclude(h => h.Admin)
        .AsSplitQuery().AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId);

    public Task<List<DriverBusAssignment>> AssignmentsOfAsync(Guid userId) => db.DriverBusAssignments.Include(a => a.Bus).Include(a => a.IndividualBus).AsNoTracking()
        .Where(a => a.DriverId == userId).OrderByDescending(a => a.IsActive).ThenByDescending(a => a.AssignedAt).ToListAsync();

    // The buses an admin has assigned this driver to (active assignments only), with their stops.
    public async Task<List<Bus>> AssignedBusesAsync(Guid userId)
    {
        var busIds = db.DriverBusAssignments.Where(a => a.DriverId == userId && a.IsActive).Select(a => a.BusId);
        return await db.Buses.Include(b => b.Stops).AsNoTracking().Where(b => busIds.Contains(b.Id)).OrderBy(b => b.BusName).ToListAsync();
    }

    // ---- Changing contact details ----

    public async Task<ServiceResult<DriverProfile>> UpdateContactAsync(Guid userId, DriverProfileUpdateRequest r)
    {
        var profile = await db.DriverProfiles.Include(p => p.User).FirstOrDefaultAsync(p => p.UserId == userId);
        if (profile is null)
        {
            return ServiceResult<DriverProfile>.NotFound("No driver registration was found for this account.");
        }
        if (r.Phone is not null)
        {
            if (DriverRules.NormalizePhone(r.Phone) is not { } phone)
            {
                return ServiceResult<DriverProfile>.BadRequest("Enter a valid Bangladesh mobile number, like 01712-345678.");
            }
            profile.User!.Phone = phone;
            profile.User.UpdatedAt = DateTime.UtcNow;
        }
        if (r.EmergencyContact is not null)
        {
            if (string.IsNullOrWhiteSpace(r.EmergencyContact))
            {
                profile.EmergencyContact = null;
            }
            else if (DriverRules.NormalizePhone(r.EmergencyContact) is { } emergency)
            {
                profile.EmergencyContact = emergency;
            }
            else
            {
                return ServiceResult<DriverProfile>.BadRequest("Enter a valid emergency contact number, like 01712-345678.");
            }
        }
        profile.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return ServiceResult<DriverProfile>.Ok((await ProfileOfAsync(userId))!, "Your contact details were updated.");
    }

    // ---- Resubmitting ----

    // Only while the admin has asked for corrections, and only the parts they asked for are read: anything else in
    // the request is ignored. The application then goes back to PENDING for another review.
    public async Task<ServiceResult<DriverProfile>> ResubmitAsync(Guid userId, DriverApplicationInput r, DateTime? nowUtc = null)
    {
        var now = nowUtc ?? DateTime.UtcNow;
        var today = DriverRules.Today(now);
        var profile = await db.DriverProfiles.Include(p => p.User).Include(p => p.Documents).FirstOrDefaultAsync(p => p.UserId == userId);
        if (profile is null)
        {
            return ServiceResult<DriverProfile>.NotFound("No driver registration was found for this account.");
        }
        if (profile.VerificationStatus != DriverVerificationStatus.ResubmissionRequired)
        {
            return ServiceResult<DriverProfile>.Conflict("Your application is not waiting for corrections.");
        }
        var requested = profile.RequestedFields.ToHashSet();
        if (requested.Count == 0)
        {
            return ServiceResult<DriverProfile>.Conflict("Your application is not waiting for corrections.");
        }

        if (requested.Contains(DriverResubmissionFields.Personal))
        {
            var (personal, problem) = DriverRules.CheckPersonal(r, today, Options.MinimumAge);
            if (problem is not null)
            {
                return ServiceResult<DriverProfile>.BadRequest(problem);
            }
            profile.User!.Name = personal!.Name;
            profile.User.Phone = personal.Phone;
            profile.User.Gender = personal.Gender;
            profile.User.UpdatedAt = now;
            profile.DateOfBirth = personal.DateOfBirth;
            profile.Gender = personal.Gender;
        }
        if (requested.Contains(DriverResubmissionFields.DrivingDetails))
        {
            var (driving, problem) = DriverRules.CheckDriving(r, today, Options.LicensePattern, profile.DateOfBirth);
            if (problem is not null)
            {
                return ServiceResult<DriverProfile>.BadRequest(problem);
            }
            var hash = protector.Hash(LicensePurpose, driving!.LicenseNumber);
            if (await db.DriverProfiles.AnyAsync(p => p.Id != profile.Id && p.LicenseNumberHash == hash))
            {
                return ServiceResult<DriverProfile>.Conflict(DuplicateDocumentMessage);
            }
            ApplyDriving(profile, driving);
        }
        if (requested.Contains(DriverResubmissionFields.NidNumber))
        {
            var (nid, problem) = DriverRules.CheckNid(r, Options.NidPattern);
            if (problem is not null)
            {
                return ServiceResult<DriverProfile>.BadRequest(problem);
            }
            var hash = protector.Hash(NidPurpose, nid!);
            if (await db.DriverProfiles.AnyAsync(p => p.Id != profile.Id && p.NidNumberHash == hash))
            {
                return ServiceResult<DriverProfile>.Conflict(DuplicateDocumentMessage);
            }
            ApplyNid(profile, nid!);
        }
        if (requested.Contains(DriverResubmissionFields.Employment))
        {
            var bus = r.BusId is { } busId ? await db.Buses.Include(b => b.Stops).AsNoTracking().FirstOrDefaultAsync(b => b.Id == busId) : null;
            var (employment, problem) = DriverRules.CheckEmployment(r, bus);
            if (problem is not null)
            {
                return ServiceResult<DriverProfile>.BadRequest(problem);
            }
            ApplyEmployment(profile, employment!);
        }
        if (requested.Contains(DriverResubmissionFields.Address))
        {
            var (address, problem) = DriverRules.CheckAddress(r);
            if (problem is not null)
            {
                return ServiceResult<DriverProfile>.BadRequest(problem);
            }
            ApplyAddress(profile, address!);
        }

        var documentTypes = requested.Where(f => DriverDocumentTypes.All.Contains(f)).ToList();
        var (documents, documentProblem) = DriverRules.CheckDocuments(r.Documents is null ? null
                : r.Documents.Where(d => documentTypes.Contains(d.Key)).ToDictionary(d => d.Key, d => d.Value),
            documentTypes, documentTypes, Options.MaxImageBytes);
        if (documentProblem is not null)
        {
            return ServiceResult<DriverProfile>.BadRequest(documentProblem);
        }

        var written = new List<string>();
        var replaced = new List<string>();
        var previous = profile.VerificationStatus;
        try
        {
            foreach (var (type, image) in documents!)
            {
                var key = await store.SaveAsync(image.Bytes.AsMemory(0, image.Length), image.Extension);
                written.Add(key);
                var existing = profile.Documents.FirstOrDefault(d => d.DocumentType == type);
                if (existing is null)
                {
                    db.DriverDocuments.Add(new DriverDocument
                    {
                        DriverProfileId = profile.Id, DocumentType = type, PrivateStorageKey = key, ContentType = image.ContentType,
                        FileSize = image.Length, UploadedAt = now,
                    });
                }
                else
                {
                    replaced.Add(existing.PrivateStorageKey);
                    existing.PrivateStorageKey = key;
                    existing.ContentType = image.ContentType;
                    existing.FileSize = image.Length;
                    existing.UploadedAt = now;
                }
            }

            profile.VerificationStatus = DriverVerificationStatus.Pending;
            profile.AccountStatus = DriverAccountStatus.PendingVerification;
            profile.ResubmissionFields = null;
            profile.SubmittedAt = now;
            profile.UpdatedAt = now;
            db.DriverVerificationHistory.Add(new DriverVerificationHistory
            {
                DriverProfileId = profile.Id,
                PreviousStatus = previous,
                NewStatus = DriverVerificationStatus.Pending,
                Reason = $"Resubmitted: {string.Join(", ", requested.Select(DriverRules.Label))}.",
                CreatedAt = now,
            });
            await NotifyAdminsAsync("Driver resubmitted application",
                $"{profile.User!.Name} sent the corrections you asked for ({profile.ApplicationCode}).", userId, profile.Id);
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (UniqueViolation(ex) is not null)
        {
            Cleanup(written);
            db.ChangeTracker.Clear();
            return ServiceResult<DriverProfile>.Conflict(DuplicateDocumentMessage);
        }
        catch
        {
            Cleanup(written);
            throw;
        }
        // The old files are only removed once the new ones are saved.
        Cleanup(replaced);
        return ServiceResult<DriverProfile>.Ok((await ProfileOfAsync(userId))!, "Your corrections were sent for review.");
    }

    // ---- Helpers ----

    private void ApplyDriving(DriverProfile profile, DriverRules.Driving driving)
    {
        profile.LicenseNumberEncrypted = protector.Encrypt(driving.LicenseNumber);
        profile.LicenseNumberHash = protector.Hash(LicensePurpose, driving.LicenseNumber);
        profile.LicenseNumberLast4 = SensitiveDataProtector.Last4(driving.LicenseNumber);
        profile.LicenseType = driving.LicenseType;
        profile.LicenseExpiryDate = driving.Expiry;
        profile.DrivingExperienceYears = driving.ExperienceYears;
    }

    private void ApplyNid(DriverProfile profile, string nid)
    {
        profile.NidNumberEncrypted = protector.Encrypt(nid);
        profile.NidNumberHash = protector.Hash(NidPurpose, nid);
        profile.NidNumberLast4 = SensitiveDataProtector.Last4(nid);
    }

    private static void ApplyEmployment(DriverProfile profile, DriverRules.Employment e)
    {
        profile.ClaimedBusId = e.BusId;
        profile.ClaimedBusName = e.BusName;
        profile.ClaimedOperatorName = e.OperatorName;
        profile.ClaimedRoute = e.Route;
        profile.BusRegistrationNumber = e.RegistrationNumber;
        profile.EmploymentType = e.EmploymentType;
    }

    private static void ApplyAddress(DriverProfile profile, DriverRules.Address a)
    {
        profile.PresentAddress = a.PresentAddress;
        profile.District = a.District;
        profile.Area = a.Area;
        profile.EmergencyContact = a.EmergencyContact;
    }

    private async Task NotifyAdminsAsync(string title, string message, Guid driverId, Guid profileId)
    {
        var admins = await db.Users.AsNoTracking().Where(u => u.Role == UserRoles.Admin).Select(u => u.Id).ToListAsync();
        foreach (var admin in admins)
        {
            notifications.Notify(admin, title, message, NotificationTypes.Info, $"/admin/driver-verification/{driverId}",
                NotificationCategories.Account, driverId, profileId);
        }
    }

    private void Cleanup(IEnumerable<string> keys)
    {
        foreach (var key in keys)
        {
            store.Delete(key);
        }
    }

    private static string? UniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg ? pg.ConstraintName ?? "" : null;
}
