namespace SmartCommute.Server.Extensions;

public static class QueryExtensions
{
    // Escapes the wildcard characters a user might type so ILIKE matches them literally.
    public static string EscapeLike(this string value) =>
        value.Trim().Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");

    // Builds a case-insensitive "contains" pattern for ILIKE.
    public static string ContainsPattern(this string value) => "%" + value.EscapeLike() + "%";
}
