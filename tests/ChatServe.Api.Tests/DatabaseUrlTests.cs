using ChatServe.Api.Infrastructure;
using Npgsql;

namespace ChatServe.Api.Tests;

public class DatabaseUrlTests
{
    [Fact]
    public void Neon_style_url_becomes_npgsql_connection_string_with_ssl()
    {
        var cs = DatabaseUrl.ToConnectionString(
            "postgresql://app_user:p%40ss@ep-cool-name-123456-pooler.ap-southeast-1.aws.neon.tech/neondb?sslmode=require&channel_binding=require");

        var b = new NpgsqlConnectionStringBuilder(cs);
        Assert.Equal("ep-cool-name-123456-pooler.ap-southeast-1.aws.neon.tech", b.Host);
        Assert.Equal(5432, b.Port);
        Assert.Equal("neondb", b.Database);
        Assert.Equal("app_user", b.Username);
        Assert.Equal("p@ss", b.Password);
        Assert.Equal(SslMode.Require, b.SslMode);
    }

    [Fact]
    public void Key_value_connection_string_is_returned_unchanged()
    {
        const string cs = "Host=localhost;Port=5432;Database=chatserve;Username=postgres;Password=postgres";
        Assert.Equal(cs, DatabaseUrl.ToConnectionString(cs));
    }
}
