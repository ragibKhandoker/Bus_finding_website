using SmartCommute.Server.Models;
using SmartCommute.Server.Services;

namespace SmartCommute.Tests;

// Unit tests that need no database.
public class FriendCodeTests
{
    [Fact]
    public void Generated_codes_are_five_uppercase_letters_or_digits()
    {
        for (var i = 0; i < 2000; i++)
        {
            var code = FriendCodeGenerator.Generate();
            Assert.Equal(5, code.Length);
            Assert.All(code, c => Assert.True(c is >= 'A' and <= 'Z' or >= '0' and <= '9', $"unexpected character '{c}' in {code}"));
            Assert.Equal(code, FriendCodeGenerator.Normalize(code));
        }
    }

    [Fact]
    public void Generated_codes_are_spread_out()
    {
        // 36^5 is about 60 million codes, so 500 draws should essentially never repeat.
        var codes = Enumerable.Range(0, 500).Select(_ => FriendCodeGenerator.Generate()).ToList();
        Assert.True(codes.Distinct().Count() >= codes.Count - 1);
    }

    [Theory]
    [InlineData("R7K2M", "R7K2M")]
    [InlineData(" r7k2m ", "R7K2M")]
    [InlineData("abc12", "ABC12")]
    [InlineData("ABCD", null)]
    [InlineData("ABCDEF", null)]
    [InlineData("AB-12", null)]
    [InlineData("ÄBC12", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Normalize_accepts_only_five_alphanumerics(string? input, string? expected)
    {
        Assert.Equal(expected, FriendCodeGenerator.Normalize(input));
    }

    [Fact]
    public void Friendship_order_is_the_same_whichever_way_round()
    {
        for (var i = 0; i < 100; i++)
        {
            var a = Guid.NewGuid();
            var b = Guid.NewGuid();
            var (first, second) = Friendship.Order(a, b);
            Assert.Equal((first, second), Friendship.Order(b, a));
            Assert.True(string.CompareOrdinal(first.ToString(), second.ToString()) < 0);
        }
    }
}
