using Npgsql;

namespace ChatServe.Api.Infrastructure;

public static class DatabaseUrl
{
    /// <summary>
    /// Railway/Render "postgres://user:pass@host:port/db" URL dete hain; Npgsql ko key=value format chahiye.
    /// Agar value pehle se key=value ho to waisi hi wapas.
    /// </summary>
    public static string ToConnectionString(string value)
    {
        if (!value.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) &&
            !value.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
            return value;

        var uri = new Uri(value);
        var userInfo = uri.UserInfo.Split(':', 2);
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 5432,
            Database = uri.AbsolutePath.TrimStart('/'),
            Username = Uri.UnescapeDataString(userInfo[0]),
            Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : null,
        };

        if (uri.Query.Contains("sslmode=require", StringComparison.OrdinalIgnoreCase))
            builder.SslMode = SslMode.Require;

        return builder.ConnectionString;
    }
}
