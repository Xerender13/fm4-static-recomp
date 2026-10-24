using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Fm4StaticRecomp.Launcher.Core.Updates;

namespace Fm4StaticRecomp.Launcher.Tests.Updates;

public class DownloaderTests
{
    private const string Url = "https://example.test/f.zip";
    private static readonly byte[] Data = Enumerable.Range(0, 100_000).Select(i => (byte)i).ToArray();
    private static string Sha => Convert.ToHexString(SHA256.HashData(Data));

    private static HttpResponseMessage Serve(HttpRequestMessage request, bool honourRange)
    {
        var from = honourRange && request.Headers.Range?.Ranges.First().From is { } f ? (int)f : 0;
        var response = new HttpResponseMessage(from > 0 ? HttpStatusCode.PartialContent : HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Data[from..]),
        };
        if (from > 0) response.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, Data.Length - 1, Data.Length);
        return response;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Resumes_a_partial_file(bool serverHonoursRange)
    {
        using var temp = new TempDir();
        var destination = Path.Combine(temp.Path, "f.zip");
        File.WriteAllBytes(destination + ".partial", Data[..40_000]);
        var handler = new FakeHandler();
        handler.Routes[Url] = r => Serve(r, serverHonoursRange);
        await new Downloader(new HttpClient(handler)).DownloadAsync(new Uri(Url), destination, Sha, null, CancellationToken.None);
        Assert.Equal(Data, File.ReadAllBytes(destination));
        Assert.False(File.Exists(destination + ".partial"));
        Assert.Equal(40_000, handler.Requests.Single().Headers.Range!.Ranges.First().From);
    }

    [Fact]
    public async Task A_complete_partial_file_answered_416_starts_over()
    {
        using var temp = new TempDir();
        var destination = Path.Combine(temp.Path, "f.zip");
        File.WriteAllBytes(destination + ".partial", Data);
        var handler = new FakeHandler();
        handler.Routes[Url] = r => r.Headers.Range is null
            ? Serve(r, true)
            : new HttpResponseMessage(HttpStatusCode.RequestedRangeNotSatisfiable);
        await new Downloader(new HttpClient(handler)).DownloadAsync(new Uri(Url), destination, Sha, null, CancellationToken.None);
        Assert.Equal(Data, File.ReadAllBytes(destination));
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Refuses_a_wrong_checksum()
    {
        using var temp = new TempDir();
        var destination = Path.Combine(temp.Path, "f.zip");
        var handler = new FakeHandler();
        handler.Routes[Url] = r => Serve(r, true);
        await Assert.ThrowsAsync<ChecksumMismatchException>(() =>
            new Downloader(new HttpClient(handler)).DownloadAsync(new Uri(Url), destination, "00", null, CancellationToken.None));
        Assert.False(File.Exists(destination));
        Assert.False(File.Exists(destination + ".partial"));
    }
}
