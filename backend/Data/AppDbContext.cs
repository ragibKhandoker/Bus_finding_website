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
    public DbSet<BusPhoto> BusPhotos => Set<BusPhoto>();
    public DbSet<BusBoarding> BusBoardings => Set<BusBoarding>();
    public DbSet<Wallet> Wallets => Set<Wallet>();
    public DbSet<WalletTransaction> WalletTransactions => Set<WalletTransaction>();
    public DbSet<Offer> Offers => Set<Offer>();
    public DbSet<OfferTransaction> OfferTransactions => Set<OfferTransaction>();
    public DbSet<GpayAccount> GpayAccounts => Set<GpayAccount>();
    public DbSet<GpayTransaction> GpayTransactions => Set<GpayTransaction>();
    public DbSet<Trip> Trips => Set<Trip>();
    public DbSet<FareRecord> Fares => Set<FareRecord>();
    public DbSet<RidePost> RidePosts => Set<RidePost>();
    public DbSet<RideRequest> RideRequests => Set<RideRequest>();
    public DbSet<RideMessage> RideMessages => Set<RideMessage>();
    public DbSet<RideReview> RideReviews => Set<RideReview>();
    public DbSet<RideReport> RideReports => Set<RideReport>();
    public DbSet<UserBlock> UserBlocks => Set<UserBlock>();
    public DbSet<FavoriteBus> FavoriteBuses => Set<FavoriteBus>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<ReviewReply> ReviewReplies => Set<ReviewReply>();
    public DbSet<ReviewReaction> ReviewReactions => Set<ReviewReaction>();
    public DbSet<Rating> Ratings => Set<Rating>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<Chat> Chats => Set<Chat>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<Verification> Verifications => Set<Verification>();
    public DbSet<VerificationImage> VerificationImages => Set<VerificationImage>();
    public DbSet<PasswordResetRequest> PasswordResetRequests => Set<PasswordResetRequest>();
    public DbSet<SupportMessage> SupportMessages => Set<SupportMessage>();
    public DbSet<FavoriteCollection> FavoriteCollections => Set<FavoriteCollection>();
    public DbSet<FavoriteCollectionBus> FavoriteCollectionBuses => Set<FavoriteCollectionBus>();
    public DbSet<BusWatchState> BusWatchStates => Set<BusWatchState>();
    public DbSet<BusFareHistory> BusFareHistory => Set<BusFareHistory>();
    public DbSet<BusAvailabilityAlert> BusAvailabilityAlerts => Set<BusAvailabilityAlert>();
    public DbSet<BusReminder> BusReminders => Set<BusReminder>();
    public DbSet<RecentlyViewedBus> RecentlyViewedBuses => Set<RecentlyViewedBus>();

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
            e.ToTable(t => t.HasCheckConstraint("ck_gpay_accounts_balance_not_negative", "balance >= 0"));
        });

        b.Entity<GpayTransaction>(e =>
        {
            e.HasOne(x => x.Account).WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Trip).WithMany().HasForeignKey(x => x.TripId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.WalletTransaction).WithMany().HasForeignKey(x => x.WalletTransactionId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => new { x.UserId, x.CreatedAt });
            // A trip's fare is paid with GPay at most once.
            e.HasIndex(x => x.TripId).IsUnique().HasFilter("trip_id IS NOT NULL").HasDatabaseName("ix_gpay_transactions_one_per_trip");
            e.HasIndex(x => new { x.UserId, x.Reference }).IsUnique().HasFilter("reference IS NOT NULL")
                .HasDatabaseName("ix_gpay_transactions_one_per_reference");
            e.ToTable(t => t.HasCheckConstraint("ck_gpay_transactions_type",
                "type IN ('opening', 'wallet-recharge', 'trip-payment', 'payment', 'top-up')"));
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
            e.Property(x => x.CostPerPerson).HasPrecision(10, 2);
            e.HasIndex(x => new { x.Status, x.CreatedAt });
            // The open-rides list: only open, undeleted posts, newest first.
            e.HasIndex(x => x.CreatedAt).HasFilter("status = 'active' AND deleted_at IS NULL").HasDatabaseName("ix_ride_posts_open");
            // RideLifecycle's expiry and reminder checks: live rides by departure.
            e.HasIndex(x => x.DepartureAt).HasFilter("status IN ('active', 'full') AND deleted_at IS NULL").HasDatabaseName("ix_ride_posts_live_departure");
            e.HasIndex(x => new { x.UserId, x.CreatedAt });
            e.ToTable(t =>
            {
                t.HasCheckConstraint("ck_ride_posts_status", "status IN ('active', 'full', 'completed', 'cancelled', 'expired')");
                t.HasCheckConstraint("ck_ride_posts_gender_preference", "gender_preference IN ('Any', 'Male', 'Female')");
                t.HasCheckConstraint("ck_ride_posts_max_participants", "max_participants BETWEEN 2 AND 20");
                t.HasCheckConstraint("ck_ride_posts_vehicle_type", "vehicle_type IN ('Car', 'Bike', 'CNG', 'Other')");
                t.HasCheckConstraint("ck_ride_posts_contact_preference", "contact_preference IN ('chat', 'phone', 'both')");
                t.HasCheckConstraint("ck_ride_posts_cost", "cost_per_person IS NULL OR cost_per_person >= 0");
            });
        });

        b.Entity<RideRequest>(e =>
        {
            e.HasOne(x => x.Requester).WithMany().HasForeignKey(x => x.RequesterId).OnDelete(DeleteBehavior.Cascade);
            // One live (pending or approved) request per rider per ride. A rider who was rejected or who cancelled
            // may ask again, which adds a new row, so those rows are left out of the uniqueness.
            e.HasIndex(x => new { x.RidePostId, x.RequesterId }).IsUnique().HasFilter("status IN ('pending', 'accepted')")
                .HasDatabaseName("ix_ride_requests_one_live_per_rider");
            e.HasIndex(x => new { x.RequesterId, x.CreatedAt });
            e.ToTable(t => t.HasCheckConstraint("ck_ride_requests_status", "status IN ('pending', 'accepted', 'rejected', 'cancelled')"));
        });

        b.Entity<RideMessage>(e =>
        {
            e.HasOne(x => x.RidePost).WithMany().HasForeignKey(x => x.RidePostId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Sender).WithMany().HasForeignKey(x => x.SenderId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.RidePostId, x.CreatedAt });
        });

        b.Entity<RideReview>(e =>
        {
            e.HasOne(x => x.RidePost).WithMany().HasForeignKey(x => x.RidePostId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Reviewer).WithMany().HasForeignKey(x => x.ReviewerId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.ReviewedUser).WithMany().HasForeignKey(x => x.ReviewedUserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.RidePostId, x.ReviewerId, x.ReviewedUserId }).IsUnique();
            e.HasIndex(x => new { x.ReviewedUserId, x.CreatedAt });
            e.ToTable(t =>
            {
                t.HasCheckConstraint("ck_ride_reviews_rating", "rating BETWEEN 1 AND 5");
                t.HasCheckConstraint("ck_ride_reviews_not_self", "reviewer_id <> reviewed_user_id");
            });
        });

        b.Entity<RideReport>(e =>
        {
            e.HasOne(x => x.RidePost).WithMany().HasForeignKey(x => x.RidePostId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Reporter).WithMany().HasForeignKey(x => x.ReporterId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.ReportedUser).WithMany().HasForeignKey(x => x.ReportedUserId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => new { x.Status, x.CreatedAt });
            e.HasIndex(x => new { x.ReporterId, x.CreatedAt });
            e.ToTable(t =>
            {
                t.HasCheckConstraint("ck_ride_reports_status", "status IN ('open', 'reviewed', 'dismissed')");
                t.HasCheckConstraint("ck_ride_reports_target", "ride_post_id IS NOT NULL OR reported_user_id IS NOT NULL");
            });
        });

        b.Entity<UserBlock>(e =>
        {
            e.HasOne(x => x.Blocker).WithMany().HasForeignKey(x => x.BlockerId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Blocked).WithMany().HasForeignKey(x => x.BlockedId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.BlockerId, x.BlockedId }).IsUnique();
            e.HasIndex(x => x.BlockedId);
            e.ToTable(t => t.HasCheckConstraint("ck_user_blocks_not_self", "blocker_id <> blocked_id"));
        });

        b.Entity<FavoriteBus>(e => e.HasIndex(x => new { x.UserId, x.BusId }).IsUnique());

        // ---- Smart Favourite Bus module ----
        b.Entity<FavoriteCollection>(e =>
        {
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.UserId, x.Name }).IsUnique();
            e.ToTable(t => t.HasCheckConstraint("ck_favorite_collections_name", "length(btrim(name)) > 0"));
        });

        b.Entity<FavoriteCollectionBus>(e =>
        {
            e.HasKey(x => new { x.CollectionId, x.FavoriteBusId });
            e.HasOne(x => x.Collection).WithMany(c => c.Buses).HasForeignKey(x => x.CollectionId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.FavoriteBus).WithMany().HasForeignKey(x => x.FavoriteBusId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.FavoriteBusId);
        });

        b.Entity<BusWatchState>(e =>
        {
            e.HasKey(x => x.BusId);
            e.HasOne(x => x.Bus).WithOne().HasForeignKey<BusWatchState>(x => x.BusId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<BusFareHistory>(e =>
        {
            e.HasOne(x => x.Bus).WithMany().HasForeignKey(x => x.BusId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.BusId, x.RecordedAt });
        });

        b.Entity<BusAvailabilityAlert>(e =>
        {
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Bus).WithMany().HasForeignKey(x => x.BusId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.UserId, x.BusId, x.FromStop, x.ToStop }).IsUnique();
            e.HasIndex(x => x.IsActive);
        });

        b.Entity<BusReminder>(e =>
        {
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Bus).WithMany().HasForeignKey(x => x.BusId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.UserId, x.DepartureAt });
            // The monitor's query: enabled reminders not sent yet.
            e.HasIndex(x => x.DepartureAt).HasFilter("is_enabled AND sent_at IS NULL").HasDatabaseName("ix_bus_reminders_due");
            e.ToTable(t => t.HasCheckConstraint("ck_bus_reminders_lead_minutes", "lead_minutes IN (15, 30, 60)"));
        });

        b.Entity<RecentlyViewedBus>(e =>
        {
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Bus).WithMany().HasForeignKey(x => x.BusId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.UserId, x.BusId }).IsUnique();
            e.HasIndex(x => new { x.UserId, x.ViewedAt });
        });

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
                "category IN ('general', 'account', 'friend', 'ride', 'reward', 'sos', 'bus-favourite', 'bus-fare', 'bus-availability', 'bus-reminder', 'bus-route')"));
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

        b.Entity<Verification>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.CreatedAt });
            e.HasIndex(x => new { x.Status, x.CreatedAt });
            // One submission waiting for review per user at a time.
            e.HasIndex(x => x.UserId).IsUnique().HasFilter("status = 'hold'").HasDatabaseName("ix_verifications_one_pending_per_user");
            // An uploaded image backs one submission only.
            e.HasIndex(x => x.ImageId).IsUnique().HasFilter("image_id IS NOT NULL");
            e.HasOne(x => x.Image).WithMany().HasForeignKey(x => x.ImageId).OnDelete(DeleteBehavior.SetNull);
            e.ToTable(t =>
            {
                t.HasCheckConstraint("ck_verifications_status", "status IN ('hold', 'approved', 'rejected')");
                t.HasCheckConstraint("ck_verifications_document_type", "document_type IN ('student_pass', 'nid')");
            });
        });

        b.Entity<BusPhoto>(e =>
        {
            e.HasOne(x => x.Bus).WithMany().HasForeignKey(x => x.BusId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.UploadedBy).WithMany().HasForeignKey(x => x.UploadedById).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => x.BusId).IsUnique();
            e.HasIndex(x => x.StorageKey).IsUnique();
        });

        b.Entity<VerificationImage>(e =>
        {
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.UserId, x.CreatedAt });
            e.HasIndex(x => x.StorageKey).IsUnique();
        });

        b.Entity<PasswordResetRequest>(e =>
        {
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.HandledBy).WithMany().HasForeignKey(x => x.HandledById).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => new { x.Status, x.CreatedAt });
            // One request waiting for an admin per user at a time.
            e.HasIndex(x => x.UserId).IsUnique().HasFilter("status = 'pending'").HasDatabaseName("ix_password_reset_requests_one_pending_per_user");
            e.ToTable(t => t.HasCheckConstraint("ck_password_reset_requests_status",
                "status IN ('pending', 'sent', 'completed', 'dismissed')"));
        });

        b.Entity<SupportMessage>(e =>
        {
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            // Deleting an admin's account keeps their replies, just without the link to them.
            e.HasOne(x => x.Sender).WithMany().HasForeignKey(x => x.SenderId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => new { x.UserId, x.CreatedAt });
            // Unread counts only look at unread rows.
            e.HasIndex(x => new { x.UserId, x.FromAdmin }).HasFilter("read_at IS NULL").HasDatabaseName("ix_support_messages_unread");
            e.ToTable(t => t.HasCheckConstraint("ck_support_messages_text_not_blank", @"text ~ '\S'"));
        });
    }
}
