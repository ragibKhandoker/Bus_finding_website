# SmartCommute server (ASP.NET Core MVC + Neon)

Backend for the SmartCommute Dhaka web client (`../web-client`) and Flutter app (`../client`).
It keeps the same URLs and JSON shapes the clients already use, so neither client needs changes
beyond pointing at this server.

## Project structure (MVC)

| Folder | Role |
| --- | --- |
| `Models/` | **M** — database entities (User, Bus, Trip, Wallet, RidePost, Review, Chat, ...) |
| `ViewModels/` | **V** — request validation and the JSON response shapes the clients read |
| `Views/` | **V** — Razor status page served at `/` |
| `Controllers/` | **C** — one controller per feature, mapping URLs to actions |
| `Services/` | Shared business logic: JWT, wallet ledger, notifications, maps, image storage |
| `Data/` | EF Core `AppDbContext`, migrations, and seed data (12 Dhaka bus routes) |

## Run locally

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

1. Create a project on [Neon](https://neon.tech) and copy its connection string.
2. Copy `appsettings.Local.example.json` to `appsettings.Local.json` and fill in:
   - `ConnectionStrings:DefaultConnection`: the Neon string (the `postgresql://...` form works as is)
   - `Jwt:Secret`: any random string of 32+ characters
   - `Admin:Email` / `Admin:Password`: optional; creates an admin account for verification review
3. Run:

   ```sh
   dotnet run
   ```

The server listens on `http://localhost:3000` (what both clients use by default). On first start it
creates every table in Neon and seeds the bus routes. Open `http://localhost:3000` to check that the
database is connected.

`appsettings.Local.json` is git-ignored. Do not put real secrets in `appsettings.json`.

## Database changes

After changing a model:

```sh
dotnet tool restore
dotnet ef migrations add <Name> -o Data/Migrations
```

Migrations are applied automatically when the server starts.

## Deploy on Render

`../render.yaml` builds `server/Dockerfile`. In the Render dashboard set
`ConnectionStrings__DefaultConnection`, `Jwt__Secret`, and `Cors__AllowedOrigins__0` (your web client URL).

## Notes

- Uploaded verification images are saved under `wwwroot/uploads`. Render's free disk is wiped on
  each deploy, so use cloud storage if those images must last.
- The clients call ride share, reviews, ratings, fares and notifications **without a token**, so the
  server trusts the `userId` in those requests. When a token is sent, it must match that `userId`.
  Sending the token from the clients for these calls would close that gap.
- GPay is in-app stored value only; no real payment provider is connected.
- Rewards (gems per trip, welcome offers, fare per km, GPay starting balance) are set in the
  `Rewards` section of `appsettings.json`.
