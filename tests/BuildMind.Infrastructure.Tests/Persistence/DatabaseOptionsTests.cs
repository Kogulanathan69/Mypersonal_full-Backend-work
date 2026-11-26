using BuildMind.Infrastructure.Configuration;
using BuildMind.Infrastructure.Tests.Support;

namespace BuildMind.Infrastructure.Tests.Persistence;

/// <summary>Typed database configuration (BE-M4-D02a). All connection strings here are synthetic.</summary>
public sealed class DatabaseOptionsTests
{
    internal const string Secret = SyntheticTestEndpoints.SyntheticSecret;

    internal const string ValidConnection =
        "Server=127.0.0.1;Port=1;Database=buildmind_synthetic;User ID=bm_synthetic;SslMode=VerifyFull;Password=" + Secret;

    [Fact]
    public void Defaults_are_disabled_and_valid_without_a_connection_string()
    {
        var options = new DatabaseOptions();

        Assert.False(options.Enabled);
        Assert.Equal(30, options.CommandTimeoutSeconds);
        Assert.Empty(options.Validate(null));
    }

    [Fact]
    public void Disabled_database_does_not_inspect_the_connection_string()
    {
        Assert.Empty(new DatabaseOptions().Validate("this is not a connection string " + Secret));
    }

    [Fact]
    public void Enabled_database_requires_a_connection_string()
    {
        var failures = new DatabaseOptions { Enabled = true }.Validate(null);

        Assert.Contains(failures, f => f.StartsWith("ConnectionStrings:BuildMind is required", StringComparison.Ordinal));
    }

    [Fact]
    public void Valid_enabled_configuration_is_accepted()
    {
        Assert.Empty(new DatabaseOptions { Enabled = true }.Validate(ValidConnection));
    }

    [Theory]
    [InlineData("Server=127.0.0.1;Database=buildmind_synthetic;User ID=root;Password=" + Secret, "must not use the root account")]
    [InlineData("Server=127.0.0.1;Database=buildmind_synthetic;User ID=ROOT;Password=" + Secret, "must not use the root account")]
    [InlineData("Server=127.0.0.1;User ID=bm_synthetic;Password=" + Secret, "must specify a database")]
    [InlineData("Database=buildmind_synthetic;User ID=bm_synthetic;Password=" + Secret, "must specify a server")]
    [InlineData("Server=127.0.0.1;Database=buildmind_synthetic;Password=" + Secret, "must specify a user")]
    [InlineData("Server=127.0.0.1;Database=buildmind_synthetic;User ID=bm_synthetic;Password=" + Secret + ";Persist Security Info=true", "Persist Security Info")]
    [InlineData("Server=127.0.0.1;Database=buildmind_synthetic;User ID=bm_synthetic;Password=" + Secret + ";Allow Load Local Infile=true", "Allow Load Local Infile")]
    [InlineData("Server=127.0.0.1;Database=buildmind_synthetic;User ID=bm_synthetic;Password=" + Secret + ";NotARealOption=" + Secret, "is not a valid MySQL connection string")]
    public void Unsafe_or_incomplete_connection_strings_are_rejected_without_echoing_them(string connection, string expected)
    {
        var failures = new DatabaseOptions { Enabled = true }.Validate(connection);

        Assert.Contains(failures, f => f.Contains(expected, StringComparison.Ordinal));
        Assert.All(failures, f =>
        {
            Assert.DoesNotContain(Secret, f, StringComparison.Ordinal);
            Assert.DoesNotContain("127.0.0.1", f, StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData("")]
    [InlineData(";SslMode=Disabled")]
    [InlineData(";SslMode=Preferred")]
    public void Connections_without_mandatory_tls_are_rejected(string sslSetting)
    {
        var connection = "Server=127.0.0.1;Database=buildmind_synthetic;User ID=bm_synthetic;Password=" + Secret + sslSetting;

        var failures = new DatabaseOptions { Enabled = true }.Validate(connection);

        Assert.Contains(failures, f => f.Contains("must set SslMode", StringComparison.Ordinal));
        Assert.All(failures, f => Assert.DoesNotContain(Secret, f, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Required")]
    [InlineData("VerifyCA")]
    [InlineData("VerifyFull")]
    public void Encrypted_connections_are_accepted(string sslMode)
    {
        var connection = "Server=127.0.0.1;Database=buildmind_synthetic;User ID=bm_synthetic;Password=" + Secret + ";SslMode=" + sslMode;

        Assert.Empty(new DatabaseOptions { Enabled = true }.Validate(connection));
    }

    [Theory]
    [InlineData("db.internal", "Required")]
    [InlineData("db.internal", "VerifyCA")]
    [InlineData("10.0.0.5", "Required")]
    [InlineData("localhost,db.internal", "Required")]
    public void Non_loopback_servers_require_server_identity_verification(string server, string sslMode)
    {
        var connection = $"Server={server};Database=buildmind_synthetic;User ID=bm_synthetic;Password={Secret};SslMode={sslMode}";

        var failures = new DatabaseOptions { Enabled = true }.Validate(connection);

        Assert.Contains(failures, f => f.Contains("VerifyFull outside local development", StringComparison.Ordinal));
        Assert.All(failures, f =>
        {
            Assert.DoesNotContain(Secret, f, StringComparison.Ordinal);
            Assert.DoesNotContain("db.internal", f, StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData("db.internal")]
    [InlineData("127.0.0.1")]
    public void Verified_identity_is_accepted_for_any_server(string server)
    {
        var connection = $"Server={server};Database=buildmind_synthetic;User ID=bm_synthetic;Password={Secret};SslMode=VerifyFull";

        Assert.Empty(new DatabaseOptions { Enabled = true }.Validate(connection, requireServerIdentityVerification: true));
    }

    [Theory]
    [InlineData("Required")]
    [InlineData("VerifyCA")]
    public void Production_requires_identity_verification_even_on_loopback(string sslMode)
    {
        var connection = $"Server=127.0.0.1;Database=buildmind_synthetic;User ID=bm_synthetic;Password={Secret};SslMode={sslMode}";

        var failures = new DatabaseOptions { Enabled = true }.Validate(connection, requireServerIdentityVerification: true);

        Assert.Contains(failures, f => f.Contains("VerifyFull", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(301)]
    public void Out_of_range_command_timeouts_are_rejected(int seconds)
    {
        var failures = new DatabaseOptions { CommandTimeoutSeconds = seconds }.Validate(null);

        Assert.Contains(failures, f => f.StartsWith("Database:CommandTimeoutSeconds", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(300)]
    public void Boundary_command_timeouts_are_accepted(int seconds)
    {
        Assert.Empty(new DatabaseOptions { CommandTimeoutSeconds = seconds }.Validate(null));
    }
}
