using BuildMind.Domain.Entities;

namespace BuildMind.Reports.Tests.Domain;

public class SurveyTests
{
    private static readonly DateTimeOffset UploadTime =
        new(2026, 11, 21, 10, 0, 0, TimeSpan.Zero);

    private static Survey Queued(
        long byteLength = 1024,
        string mediaType = "application/pdf",
        string? hash = null)
    {
        return Survey.CreateQueued(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "site-plan.pdf",
            mediaType,
            byteLength,
            hash ?? new string('a', 64),
            UploadTime);
    }

    [Fact]
    public void Queued_has_initial_state()
    {
        var survey = Queued();

        Assert.Equal(SurveyStatus.Queued, survey.Status);
        Assert.Equal(1L, survey.StateVersion);
        Assert.Null(survey.CompletedAtUtc);
        Assert.Null(survey.FailureCode);
        Assert.Null(survey.FailedAtUtc);
    }

    [Fact]
    public void Processing_and_completion_create_new_states()
    {
        var queued = Queued();
        var processing = queued.BeginProcessing(1);
        var completed = processing.Complete(2, UploadTime.AddMinutes(5));

        Assert.Equal(SurveyStatus.Processing, processing.Status);
        Assert.Equal(2L, processing.StateVersion);
        Assert.Equal(SurveyStatus.Completed, completed.Status);
        Assert.Equal(3L, completed.StateVersion);
        Assert.Equal(UploadTime.AddMinutes(5), completed.CompletedAtUtc);
        Assert.Equal(SurveyStatus.Queued, queued.Status);
    }

    [Fact]
    public void Queued_survey_can_fail()
    {
        var queued = Queued();
        var failed = queued.Fail(1, "UPLOAD_FAILED", UploadTime.AddMinutes(1));

        Assert.Equal(SurveyStatus.Failed, failed.Status);
        Assert.Equal(2L, failed.StateVersion);
        Assert.Equal("UPLOAD_FAILED", failed.FailureCode);
        Assert.Null(failed.CompletedAtUtc);
    }

    [Fact]
    public void Processing_survey_can_fail()
    {
        var processing = Queued().BeginProcessing(1);
        var failed = processing.Fail(2, "EXTRACTION_FAILED", UploadTime.AddMinutes(2));

        Assert.Equal(SurveyStatus.Failed, failed.Status);
        Assert.Equal(3L, failed.StateVersion);
        Assert.Equal("EXTRACTION_FAILED", failed.FailureCode);
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData("application/zip")]
    [InlineData("image/gif")]
    public void Unsupported_media_type_is_rejected(string mediaType)
    {
        Assert.Throws<ArgumentException>(() => Queued(mediaType: mediaType));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(6291457)]
    public void Invalid_file_size_is_rejected(long size)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Queued(byteLength: size));
    }

    [Fact]
    public void Maximum_file_size_is_accepted()
    {
        var survey = Queued(byteLength: Survey.MaxFileSizeBytes);

        Assert.Equal(6 * 1024 * 1024, survey.ByteLength);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("gggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggg")]
    public void Invalid_sha256_hash_is_rejected(string hash)
    {
        Assert.Throws<ArgumentException>(() => Queued(hash: hash));
    }

    [Fact]
    public void Null_sha256_hash_is_rejected()
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            Survey.CreateQueued(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                "site-plan.pdf",
                "application/pdf",
                1024,
                null!,
                UploadTime));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Invalid_failure_code_is_rejected(string? code)
    {
        var queued = Queued();

        Assert.ThrowsAny<ArgumentException>(
            () => queued.Fail(1, code!, UploadTime.AddMinutes(1)));
    }

    [Fact]
    public void Failure_code_length_is_limited_to_64()
    {
        var queued = Queued();

        Assert.Equal(
            new string('A', 64),
            queued.Fail(1, new string('A', 64), UploadTime.AddMinutes(1)).FailureCode);

        Assert.Throws<ArgumentException>(
            () => queued.Fail(1, new string('A', 65), UploadTime.AddMinutes(1)));
    }

    [Fact]
    public void Stale_state_version_is_rejected()
    {
        var queued = Queued();

        Assert.Throws<InvalidOperationException>(
            () => queued.BeginProcessing(2));
    }

    [Fact]
    public void Terminal_state_cannot_be_changed_again()
    {
        var completed = Queued()
            .BeginProcessing(1)
            .Complete(2, UploadTime.AddMinutes(5));

        Assert.Throws<InvalidOperationException>(
            () => completed.Fail(3, "LATE_FAILURE", UploadTime.AddMinutes(6)));
    }

    [Fact]
    public void Completion_before_upload_time_is_rejected()
    {
        var processing = Queued().BeginProcessing(1);

        Assert.Throws<ArgumentException>(
            () => processing.Complete(2, UploadTime.AddMinutes(-1)));
    }

    [Fact]
    public void Filename_longer_than_255_characters_is_rejected()
    {
        Assert.Throws<ArgumentException>(() =>
            Survey.CreateQueued(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                new string('a', 256),
                "application/pdf",
                1024,
                new string('a', 64),
                UploadTime));
    }
}
