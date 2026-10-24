using Microsoft.EntityFrameworkCore;
using SmartCommute.Server.Models;

namespace SmartCommute.Server.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();
    public DbSet<FriendRequest> FriendRequests => Set<FriendRequest>();
    public DbSet<Friendship> Friendships => Set<Friendship>();
    public DbSet<Bus> Buses => Set<Bus>();
    public DbSet<BusStop> BusStops => Set<BusStop>();
    public DbSet<IndividualBus> IndividualBuses => Set<IndividualBus>();
    public DbSet<BusBoarding> BusBoardings => Set<BusBoarding>();
    public DbSet<Wallet> Wallets => Set<Wallet>();
    public DbSet<WalletTransaction> WalletTransactions => Set<WalletTransaction>();
    public DbSet<Offer> Offers => Set<Offer>();
    public DbSet<GpayAccount> GpayAccounts => Set<GpayAccount>();
    public DbSet<Trip> Trips => Set<Trip>();
    public DbSet<FareRecord> Fares => Set<FareRecord>();
    public DbSet<RidePost> RidePosts => Set<RidePost>();
    public DbSet<RideRequest> RideRequests => Set<RideRequest>();
    public DbSet<FavoriteBus> FavoriteBuses => Set<FavoriteBus>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<ReviewReply> ReviewReplies => Set<ReviewReply>();
    public DbSet<ReviewReaction> ReviewReactions => Set<ReviewReaction>();
    public DbSet<Rating> Ratings => Set<Rating>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<Chat> Chats => Set<Chat>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<Verification> Verifications => Set<Verification>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        builder.Properties<decimal>().HavePrecision(12, 2);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.HasIndex(x => x.Email).IsUnique();
            e.HasIndex(x => x.FriendCode).IsUnique();
            e.ToTable(t => t.HasCheckConstraint("ck_users_role", "role IN ('commuter', 'driver', 'admin')"));
        });

        b.Entity<UserSession>(e =>
        {
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.UserId, x.ExpiresAt });
        });

        b.Entity<FriendRequest>(e =>
        {
            e.HasOne(x => x.FromUser).WithMany().HasForeignKey(x => x.FromUserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.ToUser).WithMany().HasForeignKey(x => x.ToUserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.ToUserId, x.Status });
        });

        b.Entity<Friendship>(e =>
        {
            e.HasKey(x => new { x.UserId, x.FriendId });
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Friend).WithMany().HasForeignKey(x => x.FriendId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Bus>(e =>
        {
            e.HasMany(x => x.Stops).WithOne(x => x.Bus).HasForeignKey(x => x.BusId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.IndividualBuses).WithOne(x => x.Bus).HasForeignKey(x => x.BusId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<BusStop>(e =>
        {
            e.HasIndex(x => new { x.BusId, x.Order }).IsUnique();
            e.HasIndex(x => x.Name);
        });

        b.Entity<IndividualBus>(e =>
        {
            e.HasIndex(x => x.BusCode).IsUnique();
            e.Property(x => x.Version).IsRowVersion();
        });

        b.Entity<BusBoarding>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.EndedAt });
        });

        b.Entity<Wallet>(e =>
        {
            e.HasIndex(x => x.UserId).IsUnique();
            e.HasOne(x => x.User).WithOne(u => u.Wallet).HasForeignKey<Wallet>(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Version).IsRowVersion();
        });

        b.Entity<WalletTransaction>(e => e.HasIndex(x => new { x.WalletId, x.CreatedAt }));

        b.Entity<Offer>(e => e.HasIndex(x => x.UserId).IsUnique());

        b.Entity<GpayAccount>(e =>
        {
            e.HasIndex(x => x.UserId).IsUnique();
            e.Property(x => x.Version).IsRowVersion();
        });

        b.Entity<Trip>(e => e.HasIndex(x => new { x.UserId, x.CreatedAt }));

        b.Entity<FareRecord>(e => e.HasIndex(x => new { x.Source, x.Destination }));

        b.Entity<RidePost>(e =>
        {
            e.HasMany(x => x.Requests).WithOne(x => x.RidePost).HasForeignKey(x => x.RidePostId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.Status, x.CreatedAt });
        });

        b.Entity<RideRequest>(e =>
        {
            e.HasOne(x => x.Requester).WithMany().HasForeignKey(x => x.RequesterId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.RidePostId, x.RequesterId });
        });

        b.Entity<FavoriteBus>(e => e.HasIndex(x => new { x.UserId, x.BusId }).IsUnique());

        b.Entity<Review>(e =>
        {
            e.HasMany(x => x.Replies).WithOne(x => x.Review).HasForeignKey(x => x.ReviewId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.BusId, x.CreatedAt });
        });

        b.Entity<ReviewReaction>(e =>
        {
            e.HasKey(x => new { x.ReviewId, x.UserId });
            e.HasOne(x => x.Review).WithMany().HasForeignKey(x => x.ReviewId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Rating>(e => e.HasIndex(x => new { x.BusId, x.UserId }).IsUnique());

        b.Entity<Notification>(e => e.HasIndex(x => new { x.UserId, x.IsRead, x.CreatedAt }));

        b.Entity<Chat>(e =>
        {
            e.HasIndex(x => new { x.User1Id, x.User2Id }).IsUnique();
            e.HasOne(x => x.User1).WithMany().HasForeignKey(x => x.User1Id).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.User2).WithMany().HasForeignKey(x => x.User2Id).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Messages).WithOne(x => x.Chat).HasForeignKey(x => x.ChatId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ChatMessage>(e =>
        {
            e.HasOne(x => x.Sender).WithMany().HasForeignKey(x => x.SenderId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.ChatId, x.CreatedAt });
        });

        b.Entity<Verification>(e => e.HasIndex(x => new { x.UserId, x.CreatedAt }));
    }
}
