using BuildMind.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BuildMind.Infrastructure.Tests.Persistence;

/// <summary>
/// M4 MySQL model conventions (BE-M4-D02b). CI on MySQL 8.4.13 showed the provider loses microseconds both with
/// its default <c>datetime</c> and with <c>datetime(6)</c> on the DateTimeOffset path; every timestamp is stored as
/// a UTC <c>datetime(6)</c> through a converter. No connection is opened here.
/// </summary>
public sealed class MySqlModelConventionsTests
{
    private static readonly DateTimeOffset Instant =
        new DateTimeOffset(2026, 11, 18, 10, 20, 30, TimeSpan.Zero).AddTicks(1_234_560);

    [Theory]
    [InlineData(nameof(ConventionProbe.OccurredAt))]
    [InlineData(nameof(ConventionProbe.OptionalAt))]
    [InlineData(nameof(ConventionProbe.UtcStamp))]
    [InlineData(nameof(ConventionProbe.OptionalUtcStamp))]
    public void Timestamps_are_stored_as_datetime6_through_a_converter(string property)
    {
        using var context = new ConventionProbeContext();

        var mapped = context.Model.FindEntityType(typeof(ConventionProbe))!.FindProperty(property)!;

        Assert.Equal("datetime(6)", mapped.GetColumnType());
        Assert.NotNull(mapped.GetValueConverter());
    }

    [Fact]
    public void DateTimeOffset_is_stored_as_the_utc_instant_with_full_precision()
    {
        var converter = new UtcDateTimeOffsetConverter();

        var stored = (DateTime)converter.ConvertToProvider(Instant)!;

        Assert.Equal(DateTimeKind.Utc, stored.Kind);
        Assert.Equal(Instant.UtcTicks, stored.Ticks);
    }

    [Fact]
    public void Equivalent_instants_from_different_offsets_store_identically_and_read_back_as_utc()
    {
        var converter = new UtcDateTimeOffsetConverter();
        var colombo = Instant.ToOffset(TimeSpan.FromHours(5.5));
        var newYork = Instant.ToOffset(TimeSpan.FromHours(-5));

        var fromColombo = (DateTime)converter.ConvertToProvider(colombo)!;
        var fromNewYork = (DateTime)converter.ConvertToProvider(newYork)!;
        var readBack = (DateTimeOffset)converter.ConvertFromProvider(fromColombo)!;

        Assert.Equal(fromColombo, fromNewYork);
        Assert.Equal(Instant, readBack);                 // same instant, microseconds intact
        Assert.Equal(TimeSpan.Zero, readBack.Offset);    // always UTC on read
        Assert.Equal(Instant.UtcTicks, readBack.UtcTicks);
    }

    [Fact]
    public void Stored_values_are_read_back_as_utc_even_when_the_driver_returns_unspecified_kind()
    {
        var stored = new DateTime(Instant.UtcTicks, DateTimeKind.Unspecified);

        var readBack = (DateTimeOffset)new UtcDateTimeOffsetConverter().ConvertFromProvider(stored)!;

        Assert.Equal(Instant, readBack);
        Assert.Equal(TimeSpan.Zero, readBack.Offset);
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void Ambiguous_datetime_values_are_rejected(DateTimeKind kind)
    {
        var converter = new UtcDateTimeConverter();

        Assert.Throws<ArgumentException>(() => converter.ConvertToProvider(new DateTime(Instant.UtcTicks, kind)));
    }

    [Fact]
    public void Utc_datetime_values_keep_full_precision()
    {
        var value = new DateTime(Instant.UtcTicks, DateTimeKind.Utc);

        var stored = (DateTime)new UtcDateTimeConverter().ConvertToProvider(value)!;
        var readBack = (DateTime)new UtcDateTimeConverter().ConvertFromProvider(new DateTime(stored.Ticks, DateTimeKind.Unspecified))!;

        Assert.Equal(value.Ticks, readBack.Ticks);
        Assert.Equal(DateTimeKind.Utc, readBack.Kind);
    }

    [Fact]
    public void BuildMind_context_applies_the_conventions()
    {
        var method = typeof(BuildMindDbContext).GetMethod(
            "ConfigureConventions",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        Assert.NotNull(method);
        Assert.Equal(typeof(BuildMindDbContext), method!.DeclaringType);
    }

    private sealed class ConventionProbe
    {
        public int Id { get; set; }

        public DateTimeOffset OccurredAt { get; set; }

        public DateTimeOffset? OptionalAt { get; set; }

        public DateTime UtcStamp { get; set; }

        public DateTime? OptionalUtcStamp { get; set; }
    }

    private sealed class ConventionProbeContext : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
            optionsBuilder.UseMySQL("Server=127.0.0.1;Port=1;Database=buildmind_synthetic;User ID=bm_synthetic;SslMode=Required");

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
            MySqlModelConventions.Apply(configurationBuilder);

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<ConventionProbe>();
    }
}
