using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Server.Services;

// F19: a commuter's travel preferences. Every read and write is scoped to the user from the sign-in token, so one
// user can never see or change another's; someone else's preference is answered as not found, so ids cannot be
// probed. A user may keep up to TravelPreference.MaxPerUser of them (there is no one-per-user rule). The bus must be
// one of the Dhaka city routes in the database; its name and route number are copied from there.
public class CommuterProfileService(AppDbContext db, DhakaBusGuard guard)
{
    public Task<List<TravelPreference>> MineAsync(Guid userId, CancellationToken ct = default) => db.TravelPreferences.AsNoTracking()
        .Where(p => p.UserId == userId).OrderBy(p => p.CreatedAt).ThenBy(p => p.Id).ToListAsync(ct);

    public Task<int> CountAsync(Guid userId, CancellationToken ct = default) => db.TravelPreferences.CountAsync(p => p.UserId == userId, ct);

    public Task<TravelPreference?> FindAsync(Guid userId, Guid id, CancellationToken ct = default) =>
        db.TravelPreferences.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId, ct);

    public async Task<ServiceResult<TravelPreference>> CreateAsync(Guid userId, TravelPreferenceRequest request, CancellationToken ct = default)
    {
        var checkedRequest = await ValidateAsync(userId, request, null, ct);
        if (!checkedRequest.Succeeded)
        {
            return checkedRequest;
        }
        if (await CountAsync(userId, ct) >= TravelPreference.MaxPerUser)
        {
            return ServiceResult<TravelPreference>.Conflict($"You can save up to {TravelPreference.MaxPerUser} travel preferences. Delete one you no longer use first.");
        }
        var preference = checkedRequest.Value!;
        preference.UserId = userId;
        db.TravelPreferences.Add(preference);
        await db.SaveChangesAsync(ct);
        return ServiceResult<TravelPreference>.Created(preference, "Travel preference saved.");
    }

    public async Task<ServiceResult<TravelPreference>> UpdateAsync(Guid userId, Guid id, TravelPreferenceRequest request, CancellationToken ct = default)
    {
        var existing = await db.TravelPreferences.FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId, ct);
        if (existing is null)
        {
            return ServiceResult<TravelPreference>.NotFound("Travel preference not found.");
        }
        var checkedRequest = await ValidateAsync(userId, request, id, ct);
        if (!checkedRequest.Succeeded)
        {
            return checkedRequest;
        }
        var v = checkedRequest.Value!;
        existing.PreferenceName = v.PreferenceName;
        existing.StartLocation = v.StartLocation;
        existing.Destination = v.Destination;
        existing.BusId = v.BusId;
        existing.FrequentlyUsedBus = v.FrequentlyUsedBus;
        existing.BusRouteNumber = v.BusRouteNumber;
        existing.PreferredTravelTime = v.PreferredTravelTime;
        existing.WeeklyFrequency = v.WeeklyFrequency;
        existing.TravelDays = v.TravelDays;
        existing.BusType = v.BusType;
        existing.TravelPurpose = v.TravelPurpose;
        existing.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return ServiceResult<TravelPreference>.Ok(existing, "Travel preference updated.");
    }

    // True when the user had this preference; only that one row is removed.
    public async Task<bool> DeleteAsync(Guid userId, Guid id, CancellationToken ct = default) =>
        await db.TravelPreferences.Where(p => p.Id == id && p.UserId == userId).ExecuteDeleteAsync(ct) > 0;

    // The request as a new, unsaved preference, or why it cannot be saved. ignoreId is the preference being edited.
    private async Task<ServiceResult<TravelPreference>> ValidateAsync(Guid userId, TravelPreferenceRequest r, Guid? ignoreId, CancellationToken ct)
    {
        var rules = CheckFields(r);
        if (rules is not null)
        {
            return ServiceResult<TravelPreference>.BadRequest(rules);
        }
        var name = Clean(r.PreferenceName);
        var lowered = name.ToLower();
        var duplicate = await db.TravelPreferences.AnyAsync(p => p.UserId == userId && p.PreferenceName.ToLower() == lowered && (ignoreId == null || p.Id != ignoreId), ct);
        if (duplicate)
        {
            return ServiceResult<TravelPreference>.Conflict("You already have a travel preference with this name. Choose another name.");
        }

        var bus = await guard.CityBusAsync(r.BusId ?? Guid.Empty, ct);
        if (!bus.Succeeded)
        {
            return ServiceResult<TravelPreference>.BadRequest(bus.StatusCode == StatusCodes.Status422UnprocessableEntity
                ? bus.Message : "Choose a Dhaka bus from the list.");
        }

        return ServiceResult<TravelPreference>.Ok(new TravelPreference
        {
            PreferenceName = name,
            StartLocation = Clean(r.StartLocation),
            Destination = Clean(r.Destination),
            BusId = r.BusId,
            FrequentlyUsedBus = bus.Value!.BusName,
            BusRouteNumber = bus.Value.RouteNumber,
            PreferredTravelTime = ParseTime(r.PreferredTravelTime)!.Value,
            WeeklyFrequency = r.WeeklyFrequency!.Value,
            TravelDays = NormalizeDays(r.TravelDays)!,
            BusType = TravelBusTypes.All.First(t => string.Equals(t, r.BusType.Trim(), StringComparison.OrdinalIgnoreCase)),
            TravelPurpose = TravelPurposes.All.First(t => string.Equals(t, r.TravelPurpose.Trim(), StringComparison.OrdinalIgnoreCase)),
        });
    }

    // ---- Field rules (no database), the same as the web form's ----

    // The first problem with the request's fields, or null when they are all fine.
    public static string? CheckFields(TravelPreferenceRequest r)
    {
        if (Clean(r.PreferenceName).Length is < 2 or > 60) return "The preference name must be 2 to 60 characters.";
        if (Clean(r.StartLocation).Length is < 2 or > 100) return "The start location must be 2 to 100 characters.";
        if (Clean(r.Destination).Length is < 2 or > 100) return "The destination must be 2 to 100 characters.";
        if (string.Equals(Clean(r.StartLocation), Clean(r.Destination), StringComparison.OrdinalIgnoreCase)) return "The start location and destination must be different.";
        if (r.BusId is null || r.BusId == Guid.Empty) return "Choose the Dhaka bus you use most.";
        if (ParseTime(r.PreferredTravelTime) is null) return "Enter a valid travel time, e.g. 08:30.";
        if (r.WeeklyFrequency is not (>= 1 and <= 7)) return "Weekly travel frequency must be between 1 and 7.";
        var days = NormalizeDays(r.TravelDays);
        if (days is null) return "Travel days must be days of the week, each chosen once.";
        if (days.Count == 0) return "Choose at least one travel day.";
        if (!TravelBusTypes.All.Any(t => string.Equals(t, r.BusType?.Trim(), StringComparison.OrdinalIgnoreCase))) return "Bus type must be AC, Non-AC or Both.";
        if (!TravelPurposes.All.Any(t => string.Equals(t, r.TravelPurpose?.Trim(), StringComparison.OrdinalIgnoreCase))) return "Choose one of the listed travel purposes.";
        return null;
    }

    public static TimeOnly? ParseTime(string? text) =>
        TimeOnly.TryParseExact(text?.Trim(), ["HH:mm", "H:mm", "HH:mm:ss"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) ? time : null;

    // The days in week order with their stored spelling; null when one is unknown or repeated.
    public static List<string>? NormalizeDays(IEnumerable<string>? days)
    {
        var result = new List<string>();
        foreach (var day in days ?? [])
        {
            var known = TravelWeekDays.All.FirstOrDefault(d => string.Equals(d, day?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (known is null || result.Contains(known))
            {
                return null;
            }
            result.Add(known);
        }
        return result.OrderBy(d => Array.IndexOf(TravelWeekDays.All, d)).ToList();
    }

    private static string Clean(string? text) => string.Join(' ', (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
