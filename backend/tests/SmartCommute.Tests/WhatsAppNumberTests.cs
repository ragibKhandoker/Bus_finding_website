using SmartCommute.Server.Models;
using SmartCommute.Server.Services;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Tests;

public class WhatsAppNumberTests
{
    [Theory]
    [InlineData("01712-345678", "8801712345678")]
    [InlineData("+880 1712 345678", "8801712345678")]
    [InlineData("8801912345678", "8801912345678")]
    [InlineData("00880 1812345678", "8801812345678")]
    [InlineData("+44 7911 123456", "447911123456")]
    [InlineData("   ", "")]
    [InlineData(null, "")]
    public void Accepts_local_and_international_numbers(string? input, string expected) => Assert.Equal(expected, WhatsAppNumber.Normalize(input));

    [Theory]
    [InlineData("01212345678")]   // not a Bangladeshi mobile prefix
    [InlineData("0171234567")]    // one digit short
    [InlineData("+88017123456789")]
    [InlineData("01712abc678")]
    [InlineData("1712+345678")]
    [InlineData("12345")]
    public void Refuses_what_is_not_a_number(string input) => Assert.Null(WhatsAppNumber.Normalize(input));

    private static RidePost Ride(string contact, Guid rider) => new()
    {
        Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Source = "Mirpur 10", Destination = "Motijheel", MaxParticipants = 3,
        Status = RideStatus.Active, ContactPreference = contact, DepartureAt = DateTime.UtcNow.AddHours(2),
        Requests = [new RideRequest { RequesterId = rider, Status = RideRequestStatus.Accepted, Requester = new User { Id = rider, Name = "Rider", WhatsApp = "8801812345678" } }],
    };

    [Fact]
    public void Host_and_approved_rider_see_each_others_WhatsApp_and_nobody_else_does()
    {
        var rider = Guid.NewGuid();
        var post = Ride(RideContactPreferences.Both, rider);
        var now = DateTime.UtcNow;

        var asRider = RidePostViewModel.From(post, new RideViewer(rider, post.Requests[0], RideUserStats.None, "01700000000", false, now, "8801712345678"));
        Assert.Equal("8801712345678", asRider.Contact!.WhatsApp);
        Assert.Null(asRider.Participants[0].WhatsApp);

        var asHost = RidePostViewModel.From(post, new RideViewer(post.UserId, null, RideUserStats.None, null, false, now));
        Assert.Equal("8801812345678", asHost.Participants[0].WhatsApp);
        Assert.True(asHost.Participants[0].ContactVisible);

        var stranger = RidePostViewModel.From(post, new RideViewer(Guid.NewGuid(), null, RideUserStats.None, null, false, now));
        Assert.Null(stranger.Contact);
        Assert.Null(stranger.Participants[0].WhatsApp);
        Assert.False(stranger.Participants[0].ContactVisible);
    }

    [Fact]
    public void A_chat_only_host_keeps_their_WhatsApp_private()
    {
        var rider = Guid.NewGuid();
        var post = Ride(RideContactPreferences.Chat, rider);
        var view = RidePostViewModel.From(post, new RideViewer(rider, post.Requests[0], RideUserStats.None, "01700000000", false, DateTime.UtcNow, "8801712345678"));
        Assert.Null(view.Contact!.WhatsApp);
        Assert.Null(view.Contact.Phone);
        Assert.True(view.Contact.ChatOnly);
    }
}
