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
    public DbSet<OfferTransaction> OfferTransactions => Set<OfferTransaction>();
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
            e.ToTable(t =>
            {
                t.HasCheckConstraint("ck_users_role", "role IN ('commuter', 'driver', 'admin')");
                t.HasCheckConstraint("ck_users_friend_code_format", "friend_code ~ '^[A-Z0-9]{5}$'");
            });
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
            e.HasIndex(x => new { x.FromUserId, x.Status });
            // At most one pending request per pair in either direction is enforced by the expression index
            // ix_friend_requests_one_pending_per_pair, created in the AddFriendRequestFlow migration.
            e.ToTable(t =>
            {
                t.HasCheckConstraint("ck_friend_requests_not_self", "from_user_id <> to_user_id");
                t.HasCheckConstraint("ck_friend_requests_status", "status IN ('pending', 'accepted', 'rejected')");
            });
        });

        b.Entity<Friendship>(e =>
        {
            e.HasKey(x => new { x.User1Id, x.User2Id });
            e.HasOne(x => x.User1).WithMany().HasForeignKey(x => x.User1Id).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.User2).WithMany().HasForeignKey(x => x.User2Id).OnDelete(DeleteBehavior.Cascade);
            e.ToTable(t => t.HasCheckConstraint("ck_friendships_ordered_pair", "user1id < user2id"));
        });

        b.Entity<Bus>(e =>
        {
            // The route number identifies a route, so the seeder can tell which starter routes already exist.
            e.HasIndex(x => x.RouteNumber).IsUnique();
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
            // The database itself refuses a negative balance, whatever code path tries to write one.
            e.ToTable(t =>
            {
                t.HasCheckConstraint("ck_wallets_balance_not_negative", "balance >= 0");
                t.HasCheckConstraint("ck_wallets_gems_not_negative", "gems >= 0");
            });
        });

        b.Entity<WalletTransaction>(e =>
        {
            e.HasIndex(x => new { x.WalletId, x.CreatedAt });
            e.HasIndex(x => x.TripId).IsUnique().HasFilter("trip_id IS NOT NULL");
        });

        b.Entity<Offer>(e =>
        {
            e.HasIndex(x => x.UserId).IsUnique();
            e.ToTable(t =>
            {
                t.HasCheckConstraint("ck_offers_cashback_not_negative", "cashback >= 0");
                t.HasCheckConstraint("ck_offers_coupon_not_negative", "coupon >= 0");
                t.HasCheckConstraint("ck_offers_discount_not_negative", "discount >= 0");
            });
        });

        b.Entity<OfferTransaction>(e =>
        {
            e.HasOne(x => x.Offer).WithMany().HasForeignKey(x => x.OfferId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Trip).WithMany().HasForeignKey(x => x.TripId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.WalletTransaction).WithMany().HasForeignKey(x => x.WalletTransactionId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => new { x.UserId, x.CreatedAt });
            e.HasIndex(x => new { x.UserId, x.Reference }).IsUnique().HasFilter("reference IS NOT NULL")
                .HasDatabaseName("ix_offer_transactions_one_per_reference");
            e.HasIndex(x => new { x.TripId, x.Kind }).IsUnique().HasFilter("trip_id IS NOT NULL")
                .HasDatabaseName("ix_offer_transactions_one_kind_per_trip");
            e.ToTable(t =>
            {
                t.HasCheckConstraint("ck_offer_transactions_kind", "kind IN ('cashback', 'coupon', 'discount')");
                t.HasCheckConstraint("ck_offer_transactions_reason", "reason IN ('welcome', 'opening', 'granted', 'adjusted', 'redeemed', 'trip')");
            });
        });

        b.Entity<GpayAccount>(e =>
        {
            e.HasIndex(x => x.UserId).IsUnique();
            e.Property(x => x.Version).IsRowVersion();
        });

        b.Entity<Trip>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.CreatedAt });
            // A rider has at most one active trip; the database enforces it even under simultaneous requests.
            e.HasIndex(x => x.UserId).IsUnique().HasFilter("status = 'active'").HasDatabaseName("ix_trips_one_active_per_user");
            e.HasOne(x => x.IndividualBus).WithMany().HasForeignKey(x => x.IndividualBusId).OnDelete(DeleteBehavior.SetNull);
            e.Property(x => x.Version).IsRowVersion();
        });

        b.Entity<FareRecord>(e =>
        {
            e.HasIndex(x => new { x.Source, x.Destination });
            e.HasIndex(x => new { x.UserId, x.CreatedAt });
        });

        b.Entity<RidePost>(e =>
        {
            e.HasMany(x => x.Requests).WithOne(x => x.RidePost).HasForeignKey(x => x.RidePostId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.Status, x.CreatedAt });
            // The open-rides list: only open, undeleted posts, newest first.
            e.HasIndex(x => x.CreatedAt).HasFilter("status = 'active' AND deleted_at IS NULL").HasDatabaseName("ix_ride_posts_open");
            e.HasIndex(x => new { x.UserId, x.CreatedAt });
            e.ToTable(t =>
            {
                t.HasCheckConstraint("ck_ride_posts_status", "status IN ('active', 'completed')");
                t.HasCheckConstraint("ck_ride_posts_gender_preference", "gender_preference IN ('Any', 'Male', 'Female')");
                t.HasCheckConstraint("ck_ride_posts_max_participants", "max_participants BETWEEN 2 AND 20");
                t.HasCheckConstraint("ck_ride_posts_places_not_blank", @"source ~ '\S' AND destination ~ '\S'");
            });
        });

        b.Entity<RideRequest>(e =>
        {
            e.HasOne(x => x.Requester).WithMany().HasForeignKey(x => x.RequesterId).OnDelete(DeleteBehavior.Cascade);
            // One live (pending or accepted) request per rider per ride. A rejected rider may ask again, which adds a
            // new row, so rejected rows are left out of the uniqueness.
            e.HasIndex(x => new { x.RidePostId, x.RequesterId }).IsUnique().HasFilter("status <> 'rejected'")
                .HasDatabaseName("ix_ride_requests_one_live_per_rider");
            e.HasIndex(x => new { x.RequesterId, x.CreatedAt });
            e.ToTable(t => t.HasCheckConstraint("ck_ride_requests_status", "status IN ('pending', 'accepted', 'rejected')"));
        });

        b.Entity<FavoriteBus>(e => e.HasIndex(x => new { x.UserId, x.BusId }).IsUnique());

        b.Entity<Review>(e =>
        {
            e.HasMany(x => x.Replies).WithOne(x => x.Review).HasForeignKey(x => x.ReviewId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.BusId, x.CreatedAt });
            // The rating summary only reads rated reviews.
            e.HasIndex(x => new { x.BusId, x.Rating }).HasFilter("rating IS NOT NULL").HasDatabaseName("ix_reviews_rated");
            e.ToTable(t => t.HasCheckConstraint("ck_reviews_rating_range", "rating IS NULL OR rating BETWEEN 1 AND 5"));
        });

        b.Entity<ReviewReply>(e => e.HasIndex(x => new { x.ReviewId, x.CreatedAt }));

        b.Entity<ReviewReaction>(e =>
        {
            e.HasKey(x => new { x.ReviewId, x.UserId });
            e.HasOne(x => x.Review).WithMany().HasForeignKey(x => x.ReviewId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Rating>(e => e.HasIndex(x => new { x.BusId, x.UserId }).IsUnique());

        b.Entity<Notification>(e =>
        {
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            // Deleting the sender's account keeps the alerts they sent, just without the link to them.
            e.HasOne(x => x.Sender).WithMany().HasForeignKey(x => x.SenderId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => new { x.UserId, x.IsRead, x.CreatedAt });
            e.HasIndex(x => new { x.UserId, x.CreatedAt });
            e.ToTable(t => t.HasCheckConstraint("ck_notifications_category",
                "category IN ('general', 'account', 'friend', 'ride', 'reward', 'sos')"));
        });

        b.Entity<Chat>(e =>
        {
            e.HasIndex(x => new { x.User1Id, x.User2Id }).IsUnique();
            e.HasOne(x => x.User1).WithMany().HasForeignKey(x => x.User1Id).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.User2).WithMany().HasForeignKey(x => x.User2Id).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Messages).WithOne(x => x.Chat).HasForeignKey(x => x.ChatId).OnDelete(DeleteBehavior.Cascade);
            e.ToTable(t => t.HasCheckConstraint("ck_chats_ordered_pair", "user1id < user2id"));
        });

        b.Entity<ChatMessage>(e =>
        {
            e.HasOne(x => x.Sender).WithMany().HasForeignKey(x => x.SenderId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.ChatId, x.CreatedAt });
            // Unread counts only look at unread rows, so they stay fast however long a conversation gets.
            e.HasIndex(x => new { x.ChatId, x.SenderId }).HasFilter("read = false").HasDatabaseName("ix_chat_messages_unread");
            e.ToTable(t => t.HasCheckConstraint("ck_chat_messages_text_not_blank", @"text ~ '\S'"));
        });

        b.Entity<Verification>(e => e.HasIndex(x => new { x.UserId, x.CreatedAt }));
    }
}
