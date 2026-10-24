using Npgsql;

namespace SmartCommute.Server.Data;

public static class ConnectionStringHelper
{
    // Neon's dashboard hands out URLs like postgresql://user:pass@host/db?sslmode=require,
    // while Npgsql expects "Host=...;Username=...". Accept either form.
    public static string ToNpgsql(string connectionString)
    {
        if (!connectionString.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) &&
            !connectionString.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            return connectionString;
        }

        var uri = new Uri(connectionString);
        var userInfo = uri.UserInfo.Split(':', 2);
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort || uri.Port <= 0 ? 5432 : uri.Port,
            Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
            Username = Uri.UnescapeDataString(userInfo[0]),
            Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : null,
            SslMode = SslMode.Require,
        };

        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        if (Enum.TryParse<SslMode>(query["sslmode"]?.Replace("-", ""), ignoreCase: true, out var sslMode))
        {
            builder.SslMode = sslMode;
        }
        if (Enum.TryParse<ChannelBinding>(query["channel_binding"], ignoreCase: true, out var channelBinding))
        {
            builder.ChannelBinding = channelBinding;
        }

        return builder.ConnectionString;
    }
}
