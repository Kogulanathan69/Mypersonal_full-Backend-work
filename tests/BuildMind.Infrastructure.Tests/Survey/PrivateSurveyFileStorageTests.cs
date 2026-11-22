using BuildMind.Application.Features.Survey.Services;
using BuildMind.Infrastructure.Survey;

namespace BuildMind.Infrastructure.Tests.Survey;

public sealed class PrivateSurveyFileStorageTests
{
    [Fact]
    public async Task Storage_publishes_exact_bytes_and_deletes_only_the_requested_blob()
    {
        var directory = Directory.CreateTempSubdirectory("buildmind-survey-");
        try
        {
            var storage = new PrivateSurveyFileStorage(directory.FullName);
            var bytes = "%PDF-isolated-test"u8.ToArray();
            var first = await storage.StoreAsync(new MemoryStream(bytes), "application/pdf", default);
            var second = await storage.StoreAsync(new MemoryStream(bytes), "application/pdf", default);
            Assert.NotEqual(first, second);
            Assert.Equal(2, directory.GetFiles("*.blob").Length);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(directory.FullName, first.ToString("N") + ".blob")));
            await storage.DeleteAsync(first, default);
            await storage.DeleteAsync(first, default);
            Assert.Single(directory.GetFiles());
            await storage.DeleteAsync(second, default);
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public async Task Oversize_empty_and_cancelled_writes_leave_no_file()
    {
        var directory = Directory.CreateTempSubdirectory("buildmind-survey-");
        try
        {
            var storage = new PrivateSurveyFileStorage(directory.FullName);
            await Assert.ThrowsAsync<InvalidDataException>(() => storage.StoreAsync(
                new MemoryStream(new byte[SurveyUploadValidator.MaxFileSizeBytes + 1]), "application/pdf", default));
            await Assert.ThrowsAsync<InvalidDataException>(() => storage.StoreAsync(Stream.Null, "application/pdf", default));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => storage.StoreAsync(
                Stream.Null, "application/pdf", new CancellationToken(canceled: true)));
            Assert.Empty(directory.GetFiles());
        }
        finally { directory.Delete(recursive: true); }
    }
}
