using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Server.Services;

// QR codes inside buses, and boarding by scanning them.
//
// 1. An admin, or a driver assigned to the bus, generates the bus's code (GenerateAsync). The code is a random
//    token; only its hash and an encrypted copy are stored (see BusQrCode).
// 2. A signed-in rider scans it (VerifyAsync). The server looks the token up, checks the code and the bus are in
//    service, and answers with the bus and its route plus a one-time boarding ticket.
// 3. The rider confirms (BoardAsync). The ticket is spent and the trip started in one transaction, through the same
//    TripService.BoardAsync as boarding from search, so every boarding rule (one active trip, seats, stops) holds.
//    The bus comes from the ticket, never from the request.
public partial class BusQrService(AppDbContext db, BusRouteCache routes, TripService trips, SensitiveDataProtector protector)
{
    // How long a scan can be used to board.
    public static readonly TimeSpan TicketLifetime = TimeSpan.FromMinutes(10);

    // Scans one rider may make in ScanWindow, so nobody can try tokens in bulk.
    public const int MaxScansPerWindow = 30;
    public static readonly TimeSpan ScanWindow = TimeSpan.FromMinutes(10);

    // ---- Managing codes (admin, or a driver assigned to the bus) ----

    // Whether the caller may show, generate or retire this vehicle's code: admins always; drivers when an active
    // assignment names this vehicle, or names its route without a vehicle.
    public async Task<bool> CanManageAsync(Guid userId, bool isAdmin, IndividualBus vehicle) =>
        isAdmin || await db.DriverBusAssignments.AnyAsync(a => a.DriverId == userId && a.IsActive &&
            (a.IndividualBusId == vehicle.Id || (a.IndividualBusId == null && a.BusId == vehicle.BusId)));

    public async Task<ServiceResult<BusQrCodeViewModel?>> CurrentAsync(Guid userId, bool isAdmin, Guid vehicleId)
    {
        var vehicle = await db.IndividualBuses.Include(v => v.Bus).AsNoTracking().FirstOrDefaultAsync(v => v.Id == vehicleId);
        if (vehicle is null)
        {
            return ServiceResult<BusQrCodeViewModel?>.NotFound("Bus not found.");
        }
        if (!await CanManageAsync(userId, isAdmin, vehicle))
        {
            return ServiceResult<BusQrCodeViewModel?>.Forbidden("Only an admin or this bus's driver can see its QR code.");
        }
        var code = await db.BusQrCodes.AsNoTracking().FirstOrDefaultAsync(c => c.IndividualBusId == vehicleId && c.IsActive);
        return ServiceResult<BusQrCodeViewModel?>.Ok(code is null ? null : View(code, vehicle));
    }

    // Makes a new code for the vehicle and retires the old one (if any), in one transaction.
    public async Task<ServiceResult<BusQrCodeViewModel>> GenerateAsync(Guid userId, bool isAdmin, Guid vehicleId)
    {
        var vehicle = await db.IndividualBuses.Include(v => v.Bus).AsNoTracking().FirstOrDefaultAsync(v => v.Id == vehicleId);
        if (vehicle is null)
        {
            return ServiceResult<BusQrCodeViewModel>.NotFound("Bus not found.");
        }
        if (!await CanManageAsync(userId, isAdmin, vehicle))
        {
            return ServiceResult<BusQrCodeViewModel>.Forbidden("Only an admin or this bus's driver can create its QR code.");
        }
        var token = NewSecret();
        var code = new BusQrCode
        {
            IndividualBusId = vehicle.Id,
            TokenHash = Hash(token),
            TokenCipher = protector.Encrypt(token),
            CreatedById = userId,
        };
        try
        {
            await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                db.ChangeTracker.Clear();
                await using var tx = await db.Database.BeginTransactionAsync();
                var now = DateTime.UtcNow;
                await db.BusQrCodes.Where(c => c.IndividualBusId == vehicle.Id && c.IsActive)
                    .ExecuteUpdateAsync(u => u.SetProperty(c => c.IsActive, false).SetProperty(c => c.DeactivatedAt, now).SetProperty(c => c.DeactivatedById, userId));
                code.Id = Guid.Empty;
                db.BusQrCodes.Add(code);
                await db.SaveChangesAsync();
                await tx.CommitAsync();
            });
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            db.ChangeTracker.Clear();
            return ServiceResult<BusQrCodeViewModel>.Conflict("Someone created a QR code for this bus at the same moment. Reload to see it.");
        }
        return ServiceResult<BusQrCodeViewModel>.Created(View(code, vehicle, token), "New QR code created. Print it and replace the old one in the bus; the old code no longer works.");
    }

    // Retires the vehicle's code; scanning it then says it is no longer in use.
    public async Task<ServiceResult<bool>> DeactivateAsync(Guid userId, bool isAdmin, Guid vehicleId)
    {
        var vehicle = await db.IndividualBuses.AsNoTracking().FirstOrDefaultAsync(v => v.Id == vehicleId);
        if (vehicle is null)
        {
            return ServiceResult<bool>.NotFound("Bus not found.");
        }
        if (!await CanManageAsync(userId, isAdmin, vehicle))
        {
            return ServiceResult<bool>.Forbidden("Only an admin or this bus's driver can turn off its QR code.");
        }
        var now = DateTime.UtcNow;
        var changed = await db.BusQrCodes.Where(c => c.IndividualBusId == vehicleId && c.IsActive)
            .ExecuteUpdateAsync(u => u.SetProperty(c => c.IsActive, false).SetProperty(c => c.DeactivatedAt, now).SetProperty(c => c.DeactivatedById, userId));
        return changed == 0
            ? ServiceResult<bool>.NotFound("This bus has no active QR code.")
            : ServiceResult<bool>.Ok(true, "QR code turned off. Scanning it no longer boards this bus.");
    }

    // The vehicles the caller may manage codes for, with their current codes: for a driver, the buses assigned to
    // them; for an admin, the vehicles of one route (routeId) since there are hundreds in all.
    public async Task<List<BusQrCodeViewModel>> ManageableAsync(Guid userId, bool isAdmin, Guid? routeId)
    {
        IQueryable<IndividualBus> vehicles = db.IndividualBuses.Include(v => v.Bus).AsNoTracking();
        if (isAdmin && routeId is { } route)
        {
            vehicles = vehicles.Where(v => v.BusId == route);
        }
        else
        {
            var assigned = db.DriverBusAssignments.Where(a => a.DriverId == userId && a.IsActive);
            vehicles = vehicles.Where(v => assigned.Any(a => a.IndividualBusId == v.Id || (a.IndividualBusId == null && a.BusId == v.BusId)));
            if (routeId is { } only)
            {
                vehicles = vehicles.Where(v => v.BusId == only);
            }
        }
        var list = await vehicles.OrderBy(v => v.BusCode).Take(100).ToListAsync();
        var ids = list.Select(v => v.Id).ToList();
        var codes = await db.BusQrCodes.AsNoTracking().Where(c => ids.Contains(c.IndividualBusId) && c.IsActive).ToDictionaryAsync(c => c.IndividualBusId);
        return list.Select(v => codes.TryGetValue(v.Id, out var c) ? View(c, v) : BusQrCodeViewModel.Without(v)).ToList();
    }

    // ---- Scanning and boarding (any signed-in rider) ----

    public async Task<ServiceResult<QrScanViewModel>> VerifyAsync(Guid userId, string? scanned, double? lat, double? lng)
    {
        var token = TokenFrom(scanned);
        if (token is null)
        {
            return ServiceResult<QrScanViewModel>.BadRequest("This is not a SmartCommute bus QR code. Scan the code shown inside the bus.");
        }
        var since = DateTime.UtcNow - ScanWindow;
        if (await db.BusQrScans.CountAsync(s => s.UserId == userId && s.CreatedAt > since) >= MaxScansPerWindow)
        {
            return new ServiceResult<QrScanViewModel>(StatusCodes.Status429TooManyRequests, "Too many scans in a short time. Please wait a few minutes and try again.");
        }

        var hash = Hash(token);
        var code = await db.BusQrCodes.Include(c => c.IndividualBus).ThenInclude(v => v!.Bus).AsNoTracking().FirstOrDefaultAsync(c => c.TokenHash == hash);
        if (code?.IndividualBus is not { } vehicle)
        {
            return ServiceResult<QrScanViewModel>.NotFound("This QR code does not belong to any SmartCommute bus. Scan the code shown inside the bus.");
        }
        if (!code.IsActive)
        {
            return new ServiceResult<QrScanViewModel>(StatusCodes.Status410Gone, "This QR code is no longer in use. Ask the driver for the bus's current code.");
        }
        if (!string.Equals(vehicle.Status, "active", StringComparison.OrdinalIgnoreCase))
        {
            return ServiceResult<QrScanViewModel>.Conflict($"Bus {vehicle.BusCode} is not in service right now, so it cannot be boarded.");
        }
        var route = await routes.ByIdAsync(vehicle.BusId);
        if (route is null || route.Stops.Count < 2)
        {
            return ServiceResult<QrScanViewModel>.Conflict("This bus has no route set up yet, so it cannot be boarded.");
        }

        var ticket = NewSecret();
        var hasLocation = TripService.ValidLat(lat, lng) is not null;
        var scan = new BusQrScan
        {
            UserId = userId,
            QrCodeId = code.Id,
            IndividualBusId = vehicle.Id,
            TicketHash = Hash(ticket),
            ExpiresAt = DateTime.UtcNow + TicketLifetime,
            Latitude = hasLocation ? lat : null,
            Longitude = hasLocation ? lng : null,
        };
        db.BusQrScans.Add(scan);
        await db.SaveChangesAsync();
        var now = DateTime.UtcNow;
        await db.BusQrCodes.Where(c => c.Id == code.Id)
            .ExecuteUpdateAsync(u => u.SetProperty(c => c.ScanCount, c => c.ScanCount + 1).SetProperty(c => c.LastScannedAt, now));

        string? nearest = null;
        double? nearestKm = null;
        if (hasLocation && TripService.NearestStop(route, lat!.Value, lng!.Value) is { } near && near.Km <= TripService.NearStopKm)
        {
            nearest = route.Stops[near.Index].Name;
            nearestKm = Math.Round(near.Km, 2);
        }
        var active = await trips.ActiveTripAsync(userId);
        return ServiceResult<QrScanViewModel>.Ok(new QrScanViewModel
        {
            ScanTicket = ticket,
            ExpiresAt = scan.ExpiresAt,
            Vehicle = new QrVehicleViewModel
            {
                Id = vehicle.Id.ToString(),
                BusCode = vehicle.BusCode,
                BusType = vehicle.BusType,
                TotalPassengerCapacity = vehicle.TotalPassengerCapacity,
                CurrentPassengerCount = vehicle.CurrentPassengerCount,
                SeatsFree = Math.Max(0, vehicle.TotalPassengerCapacity - vehicle.CurrentPassengerCount),
            },
            Route = route,
            NearestStop = nearest,
            NearestStopKm = nearestKm,
            ActiveTrip = active is null ? null : TripViewModel.From(active),
            OnThisBus = active?.IndividualBusId == vehicle.Id,
        }, "Bus found.");
    }

    public async Task<TripResult> BoardAsync(Guid userId, BoardByQrRequest request)
    {
        var ticket = request.ScanTicket?.Trim() ?? "";
        if (!SecretPattern().IsMatch(ticket))
        {
            return new TripResult(StatusCodes.Status400BadRequest, "Scan the bus QR code again to board.");
        }
        var hash = Hash(ticket);
        var scan = await db.BusQrScans.Include(s => s.QrCode).AsNoTracking().FirstOrDefaultAsync(s => s.TicketHash == hash && s.UserId == userId);
        if (scan is null)
        {
            return new TripResult(StatusCodes.Status404NotFound, "This scan is not valid. Scan the bus QR code again.");
        }
        if (scan.UsedAt is not null)
        {
            // The same confirmation sent twice (a double tap or a retry): answer with the trip it started.
            var started = scan.TripId is { } id ? await db.Trips.Include(t => t.IndividualBus).AsNoTracking().FirstOrDefaultAsync(t => t.Id == id) : null;
            return started is { Status: TripStatus.Active }
                ? new TripResult(StatusCodes.Status200OK, $"You are already on {started.BusName}.", started)
                : new TripResult(StatusCodes.Status409Conflict, "This scan was already used. Scan the bus QR code again to board.");
        }
        if (scan.ExpiresAt <= DateTime.UtcNow)
        {
            return new TripResult(StatusCodes.Status410Gone, "This scan has expired. Scan the bus QR code again.");
        }
        if (scan.QrCode is not { IsActive: true })
        {
            return new TripResult(StatusCodes.Status410Gone, "This QR code was turned off. Ask the driver for the bus's current code.");
        }

        var details = new BoardingDetails(BoardingMethods.Qr, request.Lat ?? scan.Latitude, request.Lng ?? scan.Longitude);
        TripResult result = new(StatusCodes.Status500InternalServerError, "Could not board. Please try again.");
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync();
            // Claim the ticket first: of two requests sent at once only one gets it, the other waits and finds it used.
            var claimed = await db.BusQrScans.Where(s => s.Id == scan.Id && s.UsedAt == null)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.UsedAt, DateTime.UtcNow));
            if (claimed == 0)
            {
                result = new TripResult(StatusCodes.Status409Conflict, "This scan was already used. Scan the bus QR code again to board.");
                return;
            }
            result = await trips.BoardAsync(userId, scan.IndividualBusId, request.BoardingStop, request.DestinationStop, details);
            if (!result.Ok || result.Trip is null)
            {
                // Nothing is kept, so the rider can fix the stop and confirm again with the same scan.
                await tx.RollbackAsync();
                return;
            }
            await db.BusQrScans.Where(s => s.Id == scan.Id).ExecuteUpdateAsync(u => u.SetProperty(s => s.TripId, result.Trip.Id));
            await tx.CommitAsync();
        });
        return result;
    }

    // ---- Helpers ----

    // The token in what the camera read: either the token itself or a link to the site with ?c=<token>.
    public static string? TokenFrom(string? scanned)
    {
        var text = scanned?.Trim() ?? "";
        if (text.Length is 0 or > 500)
        {
            return null;
        }
        if (Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
        {
            var query = QueryHelpers.ParseQuery(uri.Query);
            text = query.TryGetValue("c", out var c) ? c.ToString() : "";
        }
        return SecretPattern().IsMatch(text) ? text : null;
    }

    // 32 random bytes as base64url (43 characters).
    public static string NewSecret() => Base64UrlTextEncoder.Encode(RandomNumberGenerator.GetBytes(32));

    public static string Hash(string secret) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));

    [GeneratedRegex("^[A-Za-z0-9_-]{40,64}$")]
    private static partial Regex SecretPattern();

    private BusQrCodeViewModel View(BusQrCode code, IndividualBus vehicle, string? token = null) => new()
    {
        Id = code.Id.ToString(),
        VehicleId = vehicle.Id.ToString(),
        RouteId = vehicle.BusId.ToString(),
        BusCode = vehicle.BusCode,
        BusName = vehicle.Bus?.BusName,
        RouteNumber = vehicle.Bus?.RouteNumber,
        HasCode = true,
        Token = token ?? protector.Decrypt(code.TokenCipher),
        IsActive = code.IsActive,
        CreatedAt = code.CreatedAt,
        ScanCount = code.ScanCount,
        LastScannedAt = code.LastScannedAt,
    };

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
