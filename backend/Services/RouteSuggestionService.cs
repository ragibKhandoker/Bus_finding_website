using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Server.Services;

public record RouteSuggestionFilter(string? Status = null, string? Type = null, string? Search = null, int Page = 1, int PageSize = 15);

public record RouteSuggestionCounts(int Pending, int Approved, int Rejected);

// F20: route and stop suggestions. The submitter always comes from the sign-in token and every suggestion starts
// PENDING. Suggestions are checked against the real bus data when sent, and again when an admin approves, because the
// data may have changed in between. Only an approval changes the official buses / bus_stops rows, and it does so in
// the same database transaction as the status change: either both happen or neither does. A rejection never touches
// them. Photos are checked like the other uploads and kept as private files.
public partial class RouteSuggestionService(AppDbContext db, BusRouteCache routes, IWebHostEnvironment env, IOptions<RouteSuggestionOptions> options,
    NotificationService notifications)
{
    public const int MaxPendingPerUser = 10;
    public const int MaxRouteStops = 60;
    // A new stop, or a moved one, must be this close to the route it is on.
    public const double MaxKmFromRoute = 3;

    private string Folder => Path.GetFullPath(Path.Combine(env.ContentRootPath, options.Value.StoragePath));

    private IQueryable<RouteSuggestion> Full => db.RouteSuggestions.Include(s => s.Stops).Include(s => s.User).Include(s => s.ReviewedBy).AsSplitQuery();

    // ---- Reading ----

    public async Task<(List<RouteSuggestion> Items, int Total)> MineAsync(Guid userId, int page, int pageSize, CancellationToken ct = default)
    {
        var query = db.RouteSuggestions.Where(s => s.UserId == userId);
        var items = await query.OrderByDescending(s => s.CreatedAt).ThenByDescending(s => s.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Include(s => s.Stops).AsNoTracking().AsSplitQuery().ToListAsync(ct);
        var total = page == 1 && items.Count < pageSize ? items.Count : await query.CountAsync(ct);
        return (items, total);
    }

    // Only the person who sent it, or an admin. Someone else's suggestion is answered as not found, so ids cannot be probed.
    public async Task<RouteSuggestion?> FindAsync(Guid id, Guid viewerId, bool isAdmin, CancellationToken ct = default)
    {
        var suggestion = await Full.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
        return suggestion is not null && (isAdmin || suggestion.UserId == viewerId) ? suggestion : null;
    }

    public async Task<(List<RouteSuggestion> Items, int Total)> AllAsync(RouteSuggestionFilter f, CancellationToken ct = default)
    {
        var query = db.RouteSuggestions.AsQueryable();
        if (f.Status is { Length: > 0 } status) query = query.Where(s => s.Status == status);
        if (f.Type is { Length: > 0 } type) query = query.Where(s => s.Type == type);
        if (!string.IsNullOrWhiteSpace(f.Search))
        {
            var text = f.Search.Trim();
            var code = text.StartsWith("RS-", StringComparison.OrdinalIgnoreCase) ? text[3..] : text;
            var like = $"%{text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")}%";
            var idPrefix = $"{code.ToLowerInvariant().Replace("\\", "").Replace("%", "").Replace("_", "")}%";
            query = query.Where(s => EF.Functions.ILike(s.Id.ToString(), idPrefix) || EF.Functions.ILike(s.Title, like)
                || EF.Functions.ILike(s.User!.Name, like) || EF.Functions.ILike(s.User.Email, like));
        }
        // Pending ones oldest first (the queue), the others newest first.
        var ordered = f.Status == RouteSuggestionStatus.Pending
            ? query.OrderBy(s => s.CreatedAt).ThenBy(s => s.Id)
            : query.OrderByDescending(s => s.CreatedAt).ThenByDescending(s => s.Id);
        var items = await ordered.Skip((f.Page - 1) * f.PageSize).Take(f.PageSize).Include(s => s.User).Include(s => s.Stops)
            .AsNoTracking().AsSplitQuery().ToListAsync(ct);
        var total = f.Page == 1 && items.Count < f.PageSize ? items.Count : await query.CountAsync(ct);
        return (items, total);
    }

    public async Task<RouteSuggestionCounts> CountsAsync(CancellationToken ct = default)
    {
        var byStatus = await db.RouteSuggestions.AsNoTracking().GroupBy(s => s.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        int Count(string s) => byStatus.FirstOrDefault(x => x.Key == s)?.Count ?? 0;
        return new(Count(RouteSuggestionStatus.Pending), Count(RouteSuggestionStatus.Approved), Count(RouteSuggestionStatus.Rejected));
    }

    // The photo, if the viewer may see the suggestion it belongs to.
    public async Task<StoredImage?> ImageAsync(Guid id, Guid viewerId, bool isAdmin, CancellationToken ct = default)
    {
        var item = await db.RouteSuggestions.AsNoTracking().Where(s => s.Id == id)
            .Select(s => new { s.UserId, s.ImageKey, s.ImageContentType }).FirstOrDefaultAsync(ct);
        if (item?.ImageKey is null || !isAdmin && item.UserId != viewerId)
        {
            return null;
        }
        return ImageStorageService.OpenFile(Folder, item.ImageKey, item.ImageContentType ?? "image/jpeg");
    }

    // ---- Sending ----

    public async Task<ServiceResult<RouteSuggestion>> CreateAsync(Guid userId, CreateRouteSuggestionRequest r, CancellationToken ct = default)
    {
        var built = await BuildAsync(r, ct);
        if (!built.Succeeded)
        {
            return built.As<RouteSuggestion>();
        }
        var (suggestion, image) = built.Value!;
        if (await db.RouteSuggestions.CountAsync(s => s.UserId == userId && s.Status == RouteSuggestionStatus.Pending, ct) >= MaxPendingPerUser)
        {
            return ServiceResult<RouteSuggestion>.Conflict($"You already have {MaxPendingPerUser} suggestions waiting for review. Please wait until some are reviewed.");
        }
        // The same suggestion sent twice in a row (a double click, a retry) is saved once.
        var since = DateTime.UtcNow.AddMinutes(-2);
        if (await db.RouteSuggestions.AnyAsync(s => s.UserId == userId && s.Type == suggestion.Type && s.Title == suggestion.Title && s.CreatedAt > since, ct))
        {
            return ServiceResult<RouteSuggestion>.Conflict("You just sent this suggestion. Find it under My Suggestions.");
        }
        suggestion.UserId = userId;
        string? savedKey = null;
        try
        {
            savedKey = await SaveImageAsync(suggestion, image, ct);
            db.RouteSuggestions.Add(suggestion);
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            DeleteFile(savedKey);
            throw;
        }
        return ServiceResult<RouteSuggestion>.Created((await FindAsync(suggestion.Id, userId, false, ct))!,
            $"Thank you. Your suggestion {RouteSuggestionViewModel.CodeOf(suggestion.Id)} was sent and is waiting for an admin to review it.");
    }

    // The owner may change their own suggestion while it is still pending; it is checked exactly like a new one. The
    // photo stays unless a new one is sent or removeImage is set. Someone else's suggestion is not found.
    public async Task<ServiceResult<RouteSuggestion>> UpdateAsync(Guid id, Guid userId, CreateRouteSuggestionRequest r, bool removeImage, CancellationToken ct = default)
    {
        var existing = await db.RouteSuggestions.Include(s => s.Stops).FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId, ct);
        if (existing is null)
        {
            return ServiceResult<RouteSuggestion>.NotFound("Suggestion not found.");
        }
        if (existing.Status != RouteSuggestionStatus.Pending)
        {
            return ServiceResult<RouteSuggestion>.Conflict("Only suggestions that are still pending can be edited.");
        }
        var built = await BuildAsync(r, ct);
        if (!built.Succeeded)
        {
            return built.As<RouteSuggestion>();
        }
        var (draft, image) = built.Value!;
        var oldKey = existing.ImageKey;
        string? savedKey = null;
        try
        {
            existing.Type = draft.Type;
            existing.Title = draft.Title;
            existing.Description = draft.Description;
            existing.LocationName = draft.LocationName;
            existing.Latitude = draft.Latitude;
            existing.Longitude = draft.Longitude;
            existing.RelatedBusId = draft.RelatedBusId;
            existing.RelatedBusName = draft.RelatedBusName;
            existing.RelatedRouteNumber = draft.RelatedRouteNumber;
            existing.RelatedBusStopId = draft.RelatedBusStopId;
            existing.RelatedStopName = draft.RelatedStopName;
            existing.RouteName = draft.RouteName;
            existing.RouteNumber = draft.RouteNumber;
            existing.BusType = draft.BusType;
            existing.BusStopName = draft.BusStopName;
            existing.CorrectionField = draft.CorrectionField;
            existing.CorrectedValue = draft.CorrectedValue;
            db.RouteSuggestionStops.RemoveRange(existing.Stops);
            existing.Stops = draft.Stops;
            if (image is not null)
            {
                savedKey = await SaveImageAsync(existing, image, ct);
            }
            else if (removeImage)
            {
                existing.ImageKey = null;
                existing.ImageContentType = null;
            }
            existing.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            DeleteFile(savedKey);
            db.ChangeTracker.Clear();
            return ServiceResult<RouteSuggestion>.Conflict("An admin reviewed this suggestion while you were editing it. Refresh to see the result.");
        }
        catch
        {
            DeleteFile(savedKey);
            throw;
        }
        if (oldKey is not null && oldKey != existing.ImageKey)
        {
            DeleteFile(oldKey);
        }
        db.ChangeTracker.Clear();
        return ServiceResult<RouteSuggestion>.Ok((await FindAsync(id, userId, false, ct))!, "Suggestion updated. It is still waiting for review.");
    }

    // A checked, unsaved suggestion built from a request (shared by sending and editing), with its decoded photo.
    private async Task<ServiceResult<(RouteSuggestion Suggestion, DecodedImage? Image)>> BuildAsync(CreateRouteSuggestionRequest r, CancellationToken ct)
    {
        static ServiceResult<(RouteSuggestion, DecodedImage?)> Refuse(string message) => ServiceResult<(RouteSuggestion, DecodedImage?)>.BadRequest(message);
        var now = DateTime.UtcNow;
        var type = RouteSuggestionTypes.All.FirstOrDefault(t => string.Equals(t, r.Type?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (type is null) return Refuse("Choose a suggestion type: a new route, a new stop or incorrect information.");
        var title = Line(r.Title);
        if (title.Length is < 5 or > 150) return Refuse("The title must be 5 to 150 characters.");
        var description = Text(r.Description);
        if (description.Length is < 10 or > 2000) return Refuse("The description must be 10 to 2000 characters.");
        var locationName = NullIfEmpty(Line(r.LocationName));
        if (locationName?.Length > 200) return Refuse("The location name can be at most 200 characters.");
        var coordinates = CheckCoordinates(r.Latitude, r.Longitude);
        if (coordinates.Problem is not null) return Refuse(coordinates.Problem);

        var suggestion = new RouteSuggestion
        {
            Type = type, Title = title, Description = description, LocationName = locationName,
            Latitude = coordinates.Lat, Longitude = coordinates.Lng, Status = RouteSuggestionStatus.Pending, CreatedAt = now, UpdatedAt = now,
        };
        var all = await routes.AllAsync(ct);
        var problem = type switch
        {
            RouteSuggestionTypes.NewRoute => FillNewRoute(suggestion, r, all),
            RouteSuggestionTypes.NewStop => FillNewStop(suggestion, r, all),
            _ => FillCorrection(suggestion, r, all),
        };
        if (problem is not null)
        {
            return problem.StartsWith('!') ? ServiceResult<(RouteSuggestion, DecodedImage?)>.Conflict(problem[1..]) : Refuse(problem);
        }
        DecodedImage? image = null;
        if (!string.IsNullOrWhiteSpace(r.Image))
        {
            try
            {
                image = ImageStorageService.Decode(r.Image, options.Value.MaxImageBytes);
            }
            catch (ArgumentException e)
            {
                return Refuse(e.Message);
            }
        }
        return ServiceResult<(RouteSuggestion, DecodedImage?)>.Ok((suggestion, image));
    }

    // Writes the photo as a private file and points the suggestion at it. Returns the new file's key.
    private async Task<string?> SaveImageAsync(RouteSuggestion suggestion, DecodedImage? image, CancellationToken ct)
    {
        if (image is null) return null;
        Directory.CreateDirectory(Folder);
        var key = $"{Guid.NewGuid():N}{image.Extension}";
        await using (var file = new FileStream(Path.Combine(Folder, key), FileMode.CreateNew, FileAccess.Write))
        {
            await file.WriteAsync(image.Bytes.AsMemory(0, image.Length), ct);
        }
        suggestion.ImageKey = key;
        suggestion.ImageContentType = image.ContentType;
        return key;
    }

    // Each Fill method copies the type's fields into the suggestion and returns the first problem, or null. A
    // problem starting with "!" is a conflict with existing data rather than a bad field.

    private static string? FillNewRoute(RouteSuggestion s, CreateRouteSuggestionRequest r, List<BusViewModel> all)
    {
        var name = Line(r.RouteName);
        if (name.Length is < 2 or > 100) return "Enter the bus name, e.g. Raida Paribahan (2 to 100 characters).";
        var number = NullIfEmpty(Line(r.RouteNumber));
        if (number is not null)
        {
            if (!RouteNumberPattern().IsMatch(number)) return "The route number can only use letters, digits and dashes, e.g. 36 or DBR-12.";
            if (all.Any(b => string.Equals(b.RouteNumber, number, StringComparison.OrdinalIgnoreCase)))
                return $"!Route number {number} already exists. To fix it, report incorrect information about that route instead.";
        }
        var busType = string.IsNullOrWhiteSpace(r.BusType) ? RouteBusTypes.General
            : RouteBusTypes.All.FirstOrDefault(t => string.Equals(t, r.BusType.Trim(), StringComparison.OrdinalIgnoreCase));
        if (busType is null) return "The bus type must be general or seating.";

        var stops = r.Stops ?? [];
        if (stops.Count < 2) return "Add the start and end of the route (at least two stops).";
        if (stops.Count > MaxRouteStops) return $"A route can have at most {MaxRouteStops} stops.";
        var known = KnownStops(all);
        var seen = new HashSet<string>();
        for (var i = 0; i < stops.Count; i++)
        {
            var stopName = Line(stops[i].Name);
            var label = i == 0 ? "The start location" : i == stops.Count - 1 ? "The end location" : $"Stop {i + 1}";
            if (stopName.Length is < 2 or > 100) return $"{label} needs a name of 2 to 100 characters.";
            if (!seen.Add(BusRouteMatcher.Normalize(stopName))) return $"\"{stopName}\" is in the list twice. Each stop can appear once.";
            if (known.TryGetValue(BusRouteMatcher.Normalize(stopName), out var existing))
            {
                s.Stops.Add(new RouteSuggestionStop { Order = i, Name = existing.Name, Lat = existing.Lat, Lng = existing.Lng, IsExisting = true });
                continue;
            }
            // A new place: its position is optional here; the approving admin enters it when it is missing.
            var point = CheckCoordinates(stops[i].Lat, stops[i].Lng);
            if (point.Problem is not null) return $"{label} (\"{stopName}\"): {point.Problem}";
            s.Stops.Add(new RouteSuggestionStop { Order = i, Name = stopName, Lat = point.Lat, Lng = point.Lng });
        }
        var first = BusRouteMatcher.Normalize(s.Stops[0].Name);
        var last = BusRouteMatcher.Normalize(s.Stops[^1].Name);
        if (all.Any(b => SameRoute(b, name, first, last)))
            return $"!{name} already has a route from {s.Stops[0].Name} to {s.Stops[^1].Name}. Report incorrect information about it instead.";
        s.RouteName = name;
        s.RouteNumber = number;
        s.BusType = busType;
        return null;
    }

    private static string? FillNewStop(RouteSuggestion s, CreateRouteSuggestionRequest r, List<BusViewModel> all)
    {
        var bus = r.RelatedBusId is { } busId ? all.FirstOrDefault(b => b.Id == busId.ToString()) : null;
        if (bus is null) return "Choose the Dhaka bus route this stop is on.";
        var name = Line(r.BusStopName);
        if (name.Length is < 2 or > 100) return "Enter the stop name (2 to 100 characters).";
        if (bus.Stops.Any(x => BusRouteMatcher.Normalize(x.Name) == BusRouteMatcher.Normalize(name)))
            return $"!{name} is already a stop on this route.";
        if (s.Latitude is { } lat && s.Longitude is { } lng)
        {
            if (!NearRoute(bus, lat, lng)) return $"This location is more than {MaxKmFromRoute:0} km from every stop of this route. Check the location or the route.";
        }
        else if ((s.LocationName?.Length ?? 0) < 3)
        {
            return "Describe where the stop is, e.g. In front of the Bijoy Sarani signal.";
        }
        Relate(s, bus);
        s.BusStopName = name;
        return null;
    }

    private static string? FillCorrection(RouteSuggestion s, CreateRouteSuggestionRequest r, List<BusViewModel> all)
    {
        var bus = r.RelatedBusId is { } busId ? all.FirstOrDefault(b => b.Id == busId.ToString()) : null;
        if (bus is null) return "Choose the Dhaka bus route that has the wrong information.";
        var field = RouteCorrectionFields.All.FirstOrDefault(f => string.Equals(f, r.CorrectionField?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (field is null) return "Choose what is wrong: the bus name, route number, bus type, a stop's name or a stop's location.";
        Relate(s, bus);
        s.CorrectionField = field;
        BusStopViewModel? stop = null;
        if (RouteCorrectionFields.ForStop.Contains(field))
        {
            stop = r.RelatedBusStopId is { } stopId ? bus.Stops.FirstOrDefault(x => x.Id == stopId.ToString()) : null;
            if (stop is null) return "Choose the stop on this route that has the wrong information.";
            s.RelatedBusStopId = Guid.Parse(stop.Id!);
            s.RelatedStopName = stop.Name;
        }
        var value = Line(r.CorrectedValue);
        switch (field)
        {
            case RouteCorrectionFields.BusName:
            case RouteCorrectionFields.StopName:
                if (value.Length is < 2 or > 100) return "Enter the correct name (2 to 100 characters).";
                var current = field == RouteCorrectionFields.BusName ? bus.BusName : stop!.Name;
                if (string.Equals(value, current, StringComparison.Ordinal)) return "The correct name is the same as the current one.";
                if (field == RouteCorrectionFields.StopName && bus.Stops.Any(x => x.Id != stop!.Id && BusRouteMatcher.Normalize(x.Name) == BusRouteMatcher.Normalize(value)))
                    return $"!This route already has another stop called {value}.";
                s.CorrectedValue = value;
                return null;
            case RouteCorrectionFields.RouteNumber:
                if (!RouteNumberPattern().IsMatch(value)) return "Enter the correct route number: letters, digits and dashes only, e.g. 36 or DBR-12.";
                if (string.Equals(value, bus.RouteNumber, StringComparison.Ordinal)) return "The correct route number is the same as the current one.";
                if (all.Any(b => b.Id != bus.Id && string.Equals(b.RouteNumber, value, StringComparison.OrdinalIgnoreCase)))
                    return $"!Another route already uses number {value}.";
                s.CorrectedValue = value;
                return null;
            case RouteCorrectionFields.BusType:
                var type = RouteBusTypes.All.FirstOrDefault(t => string.Equals(t, value, StringComparison.OrdinalIgnoreCase));
                if (type is null) return "The correct bus type must be general or seating.";
                if (type == bus.BusType) return "The bus type is already " + type + ".";
                s.CorrectedValue = type;
                return null;
            default: // STOP_LOCATION
                if (s.Latitude is { } lat && s.Longitude is { } lng)
                {
                    if (GeoService.HaversineKm(stop!.Lat, stop.Lng, lat, lng) < 0.02) return "The new location is the same as the current one.";
                    if (!NearRoute(bus, lat, lng)) return $"This location is more than {MaxKmFromRoute:0} km from every stop of this route. Check the location.";
                    return null;
                }
                return (s.LocationName?.Length ?? 0) < 3 ? "Describe where the stop really is, e.g. 50 m north, in front of the mosque." : null;
        }
    }

    // ---- Deleting ----

    // The owner may delete their own pending suggestion; admins may delete any. Someone else's is not found.
    public async Task<ServiceResult<bool>> DeleteAsync(Guid id, Guid viewerId, bool isAdmin, CancellationToken ct = default)
    {
        var suggestion = await db.RouteSuggestions.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (suggestion is null || !isAdmin && suggestion.UserId != viewerId)
        {
            return ServiceResult<bool>.NotFound("Suggestion not found.");
        }
        if (!isAdmin && suggestion.Status != RouteSuggestionStatus.Pending)
        {
            return ServiceResult<bool>.Conflict("Only suggestions that are still pending can be deleted.");
        }
        var key = suggestion.ImageKey;
        db.RouteSuggestions.Remove(suggestion);
        await db.SaveChangesAsync(ct);
        DeleteFile(key);
        return ServiceResult<bool>.Ok(true, "Suggestion deleted.");
    }

    // ---- Reviewing (admins) ----

    // What approving would change, without changing anything.
    public async Task<ServiceResult<SuggestionApprovalPreviewViewModel>> PreviewAsync(Guid id, Guid adminId, CancellationToken ct = default)
    {
        var suggestion = await db.RouteSuggestions.Include(s => s.Stops).AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
        if (suggestion is null)
        {
            return ServiceResult<SuggestionApprovalPreviewViewModel>.NotFound("Suggestion not found.");
        }
        string? blocked = suggestion.Status != RouteSuggestionStatus.Pending ? $"This suggestion was already {suggestion.Status.ToLowerInvariant()}."
            : suggestion.UserId == adminId ? "You cannot review your own suggestion. Another admin has to review it." : null;
        var plan = blocked is null ? await PrepareAsync(suggestion, apply: false, null, ct) : new Prepared(blocked, [], null);
        db.ChangeTracker.Clear();
        return ServiceResult<SuggestionApprovalPreviewViewModel>.Ok(new()
        {
            CanApprove = plan.Problem is null, Problem = plan.Problem, Changes = plan.Changes, NeedsLocations = plan.Needs ?? [],
        });
    }

    public async Task<ServiceResult<RouteSuggestion>> ReviewAsync(Guid id, Guid adminId, ReviewRouteSuggestionRequest r, CancellationToken ct = default)
    {
        var status = r.Status?.Trim().ToUpperInvariant();
        if (status is not (RouteSuggestionStatus.Approved or RouteSuggestionStatus.Rejected))
        {
            return Bad("The review must be APPROVED or REJECTED.");
        }
        var note = NullIfEmpty(Text(r.AdminReviewNote));
        if (status == RouteSuggestionStatus.Rejected && (note?.Length ?? 0) < 5)
        {
            return Bad("Tell the user why it was rejected (at least 5 characters).");
        }
        if (note?.Length > 1000)
        {
            return Bad("The note can be at most 1000 characters.");
        }

        ServiceResult<RouteSuggestion> result;
        try
        {
            var strategy = db.Database.CreateExecutionStrategy();
            result = await strategy.ExecuteAsync(async () =>
            {
                db.ChangeTracker.Clear();
                await using var transaction = await db.Database.BeginTransactionAsync(ct);
                var suggestion = await db.RouteSuggestions.Include(s => s.Stops).FirstOrDefaultAsync(s => s.Id == id, ct);
                if (suggestion is null)
                {
                    return ServiceResult<RouteSuggestion>.NotFound("Suggestion not found.");
                }
                if (suggestion.UserId == adminId)
                {
                    return ServiceResult<RouteSuggestion>.Forbidden("You cannot review your own suggestion. Another admin has to review it.");
                }
                if (suggestion.Status != RouteSuggestionStatus.Pending)
                {
                    return ServiceResult<RouteSuggestion>.Conflict($"This suggestion was already {suggestion.Status.ToLowerInvariant()}.");
                }
                var now = DateTime.UtcNow;
                if (status == RouteSuggestionStatus.Approved)
                {
                    var plan = await PrepareAsync(suggestion, apply: true, r, ct);
                    if (plan.Problem is not null)
                    {
                        await transaction.RollbackAsync(ct);
                        return ServiceResult<RouteSuggestion>.Unprocessable($"It cannot be approved: {plan.Problem}");
                    }
                    var applied = string.Join(" ", plan.Changes);
                    suggestion.AppliedChange = applied.Length > 500 ? applied[..497] + "..." : applied;
                    suggestion.AppliedBusId = plan.BusId;
                }
                suggestion.Status = status;
                suggestion.AdminReviewNote = note;
                suggestion.ReviewedById = adminId;
                suggestion.ReviewedAt = now;
                suggestion.UpdatedAt = now;
                var code = RouteSuggestionViewModel.CodeOf(suggestion.Id);
                notifications.Notify(suggestion.UserId,
                    status == RouteSuggestionStatus.Approved ? $"Suggestion {code} was approved" : $"Suggestion {code} was not accepted",
                    status == RouteSuggestionStatus.Approved
                        ? $"Thank you! \"{suggestion.Title}\" was approved and the bus information has been updated.{(note is null ? "" : " Note: " + note)}"
                        : $"\"{suggestion.Title}\" was reviewed and not accepted. Reason: {note}",
                    status == RouteSuggestionStatus.Approved ? NotificationTypes.Success : NotificationTypes.Warning,
                    "/route-suggestions", NotificationCategories.BusRoute, relatedId: suggestion.Id);
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return ServiceResult<RouteSuggestion>.Ok(suggestion,
                    status == RouteSuggestionStatus.Approved ? "Suggestion approved. The official bus data was updated." : "Suggestion rejected. The bus data was not changed.");
            });
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return ServiceResult<RouteSuggestion>.Conflict("Another admin reviewed this suggestion at the same time. Refresh to see the result.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();
            return ServiceResult<RouteSuggestion>.Conflict("The bus data changed while approving (for example the route number is now taken). Nothing was changed; refresh and try again.");
        }
        if (!result.Succeeded)
        {
            return result;
        }
        if (status == RouteSuggestionStatus.Approved)
        {
            // The in-memory route list must show the change at once (search, route pages, stops).
            routes.Invalidate();
        }
        db.ChangeTracker.Clear();
        return ServiceResult<RouteSuggestion>.Ok((await FindAsync(id, adminId, true, ct))!, result.Message);
    }

    private record Prepared(string? Problem, List<string> Changes, Guid? BusId, List<NeededLocationViewModel>? Needs = null);

    // The suggestion's own position, or the one the approving admin entered (which wins). Null when there is none.
    private static (double? Lat, double? Lng, string? Problem) PositionOf(RouteSuggestion s, ReviewRouteSuggestionRequest? given)
    {
        var entered = CheckCoordinates(given?.Latitude, given?.Longitude);
        if (entered.Problem is not null) return (null, null, $"the location you entered: {entered.Problem}");
        return entered.Lat is not null ? (entered.Lat, entered.Lng, null) : (s.Latitude, s.Longitude, null);
    }

    // Checks the suggestion against the official data as it is now and lists what approving changes. Places the user
    // only described need coordinates from the admin: a preview lists them (Needs), an approval refuses without them.
    // With apply, it also makes the changes on the tracked rows (saved by the caller's transaction).
    private async Task<Prepared> PrepareAsync(RouteSuggestion s, bool apply, ReviewRouteSuggestionRequest? given, CancellationToken ct)
    {
        static Prepared Fail(string problem) => new(problem, [], null);
        var city = DhakaCityArea.Default;

        if (s.Type == RouteSuggestionTypes.NewRoute)
        {
            var stops = s.Stops.OrderBy(x => x.Order).ToList();
            if (stops.Count < 2 || string.IsNullOrWhiteSpace(s.RouteName)) return Fail("a new route needs a bus name and at least two stops.");
            var routeNeeds = new List<NeededLocationViewModel>();
            foreach (var x in stops.Where(x => x.Lat is null || x.Lng is null))
            {
                var entered = given?.StopLocations?.FirstOrDefault(l => l.Order == x.Order);
                var point = CheckCoordinates(entered?.Lat, entered?.Lng);
                if (point.Problem is not null) return Fail($"the location of {x.Name}: {point.Problem}");
                if (point.Lat is null)
                {
                    routeNeeds.Add(new() { Order = x.Order, Name = x.Name, Description = s.LocationName });
                    continue;
                }
                x.Lat = point.Lat;
                x.Lng = point.Lng;
            }
            if (apply && routeNeeds.Count > 0) return Fail($"enter the location of {string.Join(", ", routeNeeds.Select(n => n.Name))}.");
            if (!stops.Where(x => x.Lat is not null).All(x => city.IsCityStop(x.Name, x.Lat!.Value, x.Lng!.Value)))
                return Fail("every stop of the route must be inside Dhaka City.");
            var number = s.RouteNumber ?? await FreeRouteNumberAsync(s.Id, ct);
            if (await db.Buses.AnyAsync(b => b.RouteNumber != null && b.RouteNumber.ToLower() == number.ToLower(), ct))
                return Fail($"route number {number} is already used by another route.");
            var first = BusRouteMatcher.Normalize(stops[0].Name);
            var last = BusRouteMatcher.Normalize(stops[^1].Name);
            if ((await routes.AllAsync(ct)).Any(b => SameRoute(b, s.RouteName, first, last)))
                return Fail($"{s.RouteName} already has a route from {stops[0].Name} to {stops[^1].Name}.");
            var busType = s.BusType ?? RouteBusTypes.General;
            var (baseFare, perKm) = RouteBusTypes.StartingFares(busType);
            var newPlaces = stops.Where(x => !x.IsExisting).Select(x => x.Name).ToList();
            var changes = new List<string>
            {
                $"Create route {number}: {s.RouteName} ({busType}) with {stops.Count} stops, {stops[0].Name} → {stops[^1].Name}.",
                $"Stops in order: {string.Join(", ", stops.Select(x => x.Name))}.",
                $"Starting fares (usual {busType} fares): base {baseFare.ToString("0.##", CultureInfo.InvariantCulture)} taka + {perKm.ToString("0.##", CultureInfo.InvariantCulture)} taka per km.",
            };
            if (newPlaces.Count > 0) changes.Add($"New stop places: {string.Join(", ", newPlaces)}.");
            if (!apply) return new(null, changes, null, routeNeeds);
            if (!city.IsCityRoute(stops.Select(x => (x.Name, x.Lat!.Value, x.Lng!.Value)).ToList())) return Fail("every stop of the route must be inside Dhaka City.");
            var bus = new Bus
            {
                Id = Guid.NewGuid(), BusName = s.RouteName, RouteNumber = number, BusType = busType, BaseFare = baseFare, PerKmFare = perKm, RunsBothWays = true,
                Stops = stops.Select((x, i) => new BusStop { Order = i, Name = x.Name, Lat = x.Lat!.Value, Lng = x.Lng!.Value }).ToList(),
            };
            db.Buses.Add(bus);
            s.RouteNumber = number;
            return new(null, changes, bus.Id);
        }

        var target = s.RelatedBusId is { } busId ? await db.Buses.Include(b => b.Stops).FirstOrDefaultAsync(b => b.Id == busId, ct) : null;
        if (target is null) return Fail("the route it is about no longer exists.");
        var label = target.RouteNumber is { Length: > 0 } n ? $"{target.BusName} ({n})" : target.BusName;
        var ordered = target.Stops.OrderBy(x => x.Order).ToList();

        if (s.Type == RouteSuggestionTypes.NewStop)
        {
            if (string.IsNullOrWhiteSpace(s.BusStopName)) return Fail("a new stop needs a name.");
            if (ordered.Any(x => BusRouteMatcher.Normalize(x.Name) == BusRouteMatcher.Normalize(s.BusStopName))) return Fail($"{s.BusStopName} is already a stop on {label}.");
            var position = PositionOf(s, given);
            if (position.Problem is not null) return Fail(position.Problem);
            if (position.Lat is not { } lat || position.Lng is not { } lng)
            {
                if (apply) return Fail($"enter the location of {s.BusStopName}.");
                return new(null, [$"Add stop \"{s.BusStopName}\" to {label}. Its place on the route is chosen from the location you enter."], target.Id,
                    [new() { Name = s.BusStopName, Description = s.LocationName }]);
            }
            if (!city.IsCityStop(s.BusStopName, lat, lng)) return Fail("the stop must be inside Dhaka City.");
            if (ordered.Count > 0 && ordered.Min(x => GeoService.HaversineKm(x.Lat, x.Lng, lat, lng)) > MaxKmFromRoute)
                return Fail($"the location is more than {MaxKmFromRoute:0} km from every stop of {label}.");
            var index = BestPosition(ordered.Select(x => (x.Lat, x.Lng)).ToList(), lat, lng);
            var place = index == 0 ? $"before {ordered.FirstOrDefault()?.Name ?? "the first stop"} (new first stop)"
                : index >= ordered.Count ? $"after {ordered[^1].Name} (new last stop)"
                : $"between {ordered[index - 1].Name} and {ordered[index].Name}";
            var changes = new List<string>
            {
                $"Add stop \"{s.BusStopName}\" to {label}, {place}.",
                $"Location: {Coordinates(lat, lng)}.",
            };
            if (!apply) return new(null, changes, target.Id);
            // Make room: move the later stops one place down. Done in two steps because (bus_id, order) is unique.
            var stopIds = ordered.Skip(index).Select(x => x.Id).ToList();
            if (stopIds.Count > 0)
            {
                await db.BusStops.Where(x => stopIds.Contains(x.Id)).ExecuteUpdateAsync(u => u.SetProperty(x => x.Order, x => x.Order + 100000), ct);
                await db.BusStops.Where(x => stopIds.Contains(x.Id)).ExecuteUpdateAsync(u => u.SetProperty(x => x.Order, x => x.Order - 99999), ct);
            }
            db.BusStops.Add(new BusStop { Id = Guid.NewGuid(), BusId = target.Id, Order = index, Name = s.BusStopName, Lat = lat, Lng = lng });
            (s.Latitude, s.Longitude) = (lat, lng); // the record keeps the position that was used
            return new(null, changes, target.Id);
        }

        // INCORRECT_INFORMATION
        var stop = s.RelatedBusStopId is { } stopId ? ordered.FirstOrDefault(x => x.Id == stopId) : null;
        if (RouteCorrectionFields.ForStop.Contains(s.CorrectionField) && stop is null) return Fail("the stop it is about is no longer on this route.");
        switch (s.CorrectionField)
        {
            case RouteCorrectionFields.BusName when !string.IsNullOrWhiteSpace(s.CorrectedValue):
                if (!apply) return new(null, [$"Rename {label} to \"{s.CorrectedValue}\"."], target.Id);
                target.BusName = s.CorrectedValue;
                return new(null, [$"Renamed {label} to \"{s.CorrectedValue}\"."], target.Id);
            case RouteCorrectionFields.RouteNumber when !string.IsNullOrWhiteSpace(s.CorrectedValue):
                var number = s.CorrectedValue;
                if (await db.Buses.AnyAsync(b => b.Id != target.Id && b.RouteNumber != null && b.RouteNumber.ToLower() == number.ToLower(), ct))
                    return Fail($"route number {number} is already used by another route.");
                if (!apply) return new(null, [$"Change the route number of {target.BusName} from {target.RouteNumber ?? "none"} to {number}."], target.Id);
                var old = target.RouteNumber ?? "none";
                target.RouteNumber = number;
                return new(null, [$"Changed the route number of {target.BusName} from {old} to {number}."], target.Id);
            case RouteCorrectionFields.BusType when RouteBusTypes.All.Contains(s.CorrectedValue):
                if (!apply) return new(null, [$"Change the bus type of {label} from {target.BusType} to {s.CorrectedValue} (fares stay as they are)."], target.Id);
                var oldType = target.BusType;
                target.BusType = s.CorrectedValue!;
                return new(null, [$"Changed the bus type of {label} from {oldType} to {s.CorrectedValue}."], target.Id);
            case RouteCorrectionFields.StopName when !string.IsNullOrWhiteSpace(s.CorrectedValue):
                if (ordered.Any(x => x.Id != stop!.Id && BusRouteMatcher.Normalize(x.Name) == BusRouteMatcher.Normalize(s.CorrectedValue)))
                    return Fail($"{label} already has another stop called {s.CorrectedValue}.");
                if (!apply) return new(null, [$"Rename stop \"{stop!.Name}\" on {label} to \"{s.CorrectedValue}\"."], target.Id);
                var oldName = stop!.Name;
                stop.Name = s.CorrectedValue;
                return new(null, [$"Renamed stop \"{oldName}\" on {label} to \"{s.CorrectedValue}\"."], target.Id);
            case RouteCorrectionFields.StopLocation:
                var corrected = PositionOf(s, given);
                if (corrected.Problem is not null) return Fail(corrected.Problem);
                if (corrected.Lat is not { } lat || corrected.Lng is not { } lng)
                {
                    if (apply) return Fail($"enter the correct location of {stop!.Name}.");
                    return new(null, [$"Move stop \"{stop!.Name}\" on {label} from {Coordinates(stop.Lat, stop.Lng)} to the location you enter."], target.Id,
                        [new() { Name = stop.Name, Description = s.LocationName }]);
                }
                if (!city.IsCityStop(stop!.Name, lat, lng)) return Fail("the new location must be inside Dhaka City.");
                var moved = GeoService.HaversineKm(stop.Lat, stop.Lng, lat, lng);
                if (moved > MaxKmFromRoute) return Fail($"the new location is more than {MaxKmFromRoute:0} km from where the stop is now.");
                var text = $"Move stop \"{stop.Name}\" on {label} from {Coordinates(stop.Lat, stop.Lng)} to {Coordinates(lat, lng)} ({moved * 1000:0} m).";
                if (!apply) return new(null, [text], target.Id);
                stop.Lat = lat;
                stop.Lng = lng;
                (s.Latitude, s.Longitude) = (lat, lng);
                return new(null, [text.Replace("Move stop", "Moved stop")], target.Id);
            default:
                return Fail("the correction does not say what to change.");
        }
    }

    // A route number for a suggested route that had none: "S-" and the start of the suggestion id, made unique.
    private async Task<string> FreeRouteNumberAsync(Guid id, CancellationToken ct)
    {
        var hex = id.ToString("N").ToUpperInvariant();
        for (var length = 5; ; length++)
        {
            var number = "S-" + hex[..Math.Min(length, hex.Length)];
            if (length >= hex.Length || !await db.Buses.AnyAsync(b => b.RouteNumber == number, ct))
            {
                return number;
            }
        }
    }

    // ---- Rules shared with the tests ----

    // Where a new stop fits best on a route: the place that adds the least extra distance.
    public static int BestPosition(IReadOnlyList<(double Lat, double Lng)> stops, double lat, double lng)
    {
        if (stops.Count == 0) return 0;
        double D((double Lat, double Lng) a, double bLat, double bLng) => GeoService.HaversineKm(a.Lat, a.Lng, bLat, bLng);
        var best = 0;
        var bestCost = D(stops[0], lat, lng);
        for (var i = 1; i < stops.Count; i++)
        {
            var cost = D(stops[i - 1], lat, lng) + D(stops[i], lat, lng) - D(stops[i - 1], stops[i].Lat, stops[i].Lng);
            if (cost < bestCost) (best, bestCost) = (i, cost);
        }
        if (D(stops[^1], lat, lng) < bestCost) best = stops.Count;
        return best;
    }

    // Both coordinates or neither; when given they must be real numbers inside Dhaka City.
    public static (double? Lat, double? Lng, string? Problem) CheckCoordinates(double? lat, double? lng)
    {
        if (lat is null && lng is null) return (null, null, null);
        if (lat is null || lng is null) return (null, null, "Give both latitude and longitude, or neither.");
        if (!double.IsFinite(lat.Value) || !double.IsFinite(lng.Value) || lat is < -90 or > 90 || lng is < -180 or > 180)
            return (null, null, "Latitude must be between -90 and 90 and longitude between -180 and 180.");
        if (!DhakaCityArea.Default.InCity(lat.Value, lng.Value)) return (null, null, "The location must be inside Dhaka City.");
        return (Math.Round(lat.Value, 6), Math.Round(lng.Value, 6), null);
    }

    // One line of text: control characters removed and spaces collapsed.
    // Tabs and line breaks count as spaces; other control characters are dropped.
    public static string Line(string? text) =>
        string.Join(' ', new string((text ?? "").Where(c => !char.IsControl(c) || char.IsWhiteSpace(c)).ToArray())
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    // Several lines of text: tabs become spaces, other control characters except line breaks are removed, and at most
    // one empty line in a row.
    public static string Text(string? text)
    {
        var clean = new string((text ?? "").Replace("\r\n", "\n").Replace('\t', ' ').Where(c => c == '\n' || !char.IsControl(c)).ToArray());
        return BlankLines().Replace(clean, "\n\n").Trim();
    }

    private static string? NullIfEmpty(string text) => text.Length == 0 ? null : text;

    private static ServiceResult<RouteSuggestion> Bad(string message) => ServiceResult<RouteSuggestion>.BadRequest(message);

    private static string Coordinates(double lat, double lng) =>
        $"{lat.ToString("0.00000", CultureInfo.InvariantCulture)}, {lng.ToString("0.00000", CultureInfo.InvariantCulture)}";

    private static void Relate(RouteSuggestion s, BusViewModel bus)
    {
        s.RelatedBusId = Guid.Parse(bus.Id);
        s.RelatedBusName = bus.BusName;
        s.RelatedRouteNumber = bus.RouteNumber;
    }

    private static bool NearRoute(BusViewModel bus, double lat, double lng) =>
        bus.Stops.Count == 0 || bus.Stops.Min(x => GeoService.HaversineKm(x.Lat, x.Lng, lat, lng)) <= MaxKmFromRoute;

    private static bool SameRoute(BusViewModel b, string name, string first, string last)
    {
        if (b.Stops.Count < 2 || BusRouteMatcher.Normalize(b.BusName) != BusRouteMatcher.Normalize(name)) return false;
        var a = BusRouteMatcher.Normalize(b.Stops[0].Name);
        var z = BusRouteMatcher.Normalize(b.Stops[^1].Name);
        return a == first && z == last || a == last && z == first;
    }

    // Every stop name in the database once (normalized), with its position.
    private static Dictionary<string, (string Name, double Lat, double Lng)> KnownStops(List<BusViewModel> all) =>
        all.SelectMany(b => b.Stops).GroupBy(x => BusRouteMatcher.Normalize(x.Name))
            .ToDictionary(g => g.Key, g => (g.First().Name, g.Average(x => x.Lat), g.Average(x => x.Lng)));

    private void DeleteFile(string? key)
    {
        if (key is null) return;
        try { File.Delete(Path.Combine(Folder, key)); } catch (IOException) { /* a leftover file is harmless */ }
    }

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9\-]{0,19}$")]
    private static partial Regex RouteNumberPattern();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex BlankLines();
}
