using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BuildMind.Infrastructure.Persistence;

/// <summary>
/// Model-wide MySQL conventions owned by M4 (BE-M4-D02b). They are not a feature schema, only the default
/// storage of common CLR types.
/// </summary>
/// <remarks>
/// <para>CI on MySQL 8.4.13: the provider mapped <see cref="DateTimeOffset"/> to <c>datetime</c>, losing fractional
/// seconds; with <c>datetime(6)</c> alone the microseconds were STILL lost, so the provider's own
/// <see cref="DateTimeOffset"/> path drops them.</para>
/// <para>Policy: every timestamp is stored as UTC in <c>datetime(6)</c>. <see cref="DateTimeOffset"/> values are
/// converted to their UTC <see cref="DateTime"/> (the instant is kept, the original offset is not) and are read
/// back with offset +00:00. <see cref="DateTime"/> values must already be UTC: Local/Unspecified values are
/// rejected rather than stored as ambiguous wall-clock times.</para>
/// </remarks>
internal static class MySqlModelConventions
{
    public const string TimestampColumnType = "datetime(6)";

    public static void Apply(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);

        // Pre-convention configuration of a value type also applies to its nullable form.
        configurationBuilder.Properties<DateTimeOffset>()
            .HaveColumnType(TimestampColumnType)
            .HaveConversion<UtcDateTimeOffsetConverter>();
        configurationBuilder.Properties<DateTime>()
            .HaveColumnType(TimestampColumnType)
            .HaveConversion<UtcDateTimeConverter>();
    }
}

/// <summary>Stores a <see cref="DateTimeOffset"/> as its UTC instant; reads it back with a zero offset.</summary>
internal sealed class UtcDateTimeOffsetConverter() : ValueConverter<DateTimeOffset, DateTime>(
    value => value.UtcDateTime,
    stored => new DateTimeOffset(DateTime.SpecifyKind(stored, DateTimeKind.Utc), TimeSpan.Zero));

/// <summary>Accepts only UTC <see cref="DateTime"/> values; reads them back as UTC.</summary>
internal sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
    value => UtcTimestamps.RequireUtc(value),
    stored => DateTime.SpecifyKind(stored, DateTimeKind.Utc));

internal static class UtcTimestamps
{
    public static DateTime RequireUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc
            ? value
            : throw new ArgumentException(
                "Only UTC DateTime values can be persisted; use DateTimeOffset or DateTimeKind.Utc.", nameof(value));
}
