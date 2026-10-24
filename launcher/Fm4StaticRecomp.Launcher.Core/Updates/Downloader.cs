using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace Fm4StaticRecomp.Launcher.Core.Updates;

public sealed class ChecksumMismatchException(string message) : IOException(message);

public sealed class Downloader(HttpClient http)
{
    public async Task DownloadAsync(Uri url, string destination, string expectedSha256, IProgress<double>? progress,
        CancellationToken cancellation)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
        var partial = destination + ".partial";
        var have = File.Exists(partial) ? new FileInfo(partial).Length : 0;
        var first = await SendAsync(url, have, cancellation);
        // A partial file that is already whole (closed while it was being hashed) gets 416.
        if (have > 0 && first.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            first.Dispose();
            File.Delete(partial);
            have = 0;
            first = await SendAsync(url, 0, cancellation);
        }
        using var response = first;
        response.EnsureSuccessStatusCode();
        // A server that ignores Range sends the whole file with 200: start over.
        var append = have > 0 && response.StatusCode == HttpStatusCode.PartialContent;
        if (!append) have = 0;
        var total = have + (response.Content.Headers.ContentLength ?? 0);
        await using (var output = new FileStream(partial, append ? FileMode.Append : FileMode.Create, FileAccess.Write))
        await using (var input = await response.Content.ReadAsStreamAsync(cancellation))
        {
            var buffer = new byte[1 << 16];
            int read;
            while ((read = await input.ReadAsync(buffer, cancellation)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), cancellation);
                have += read;
                if (total > 0) progress?.Report((double)have / total);
            }
        }
        string actual;
        await using (var check = File.OpenRead(partial))
            actual = Convert.ToHexString(await SHA256.HashDataAsync(check, cancellation));
        if (!actual.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(partial);
            throw new ChecksumMismatchException($"{Path.GetFileName(destination)} failed its checksum; it was deleted.");
        }
        File.Move(partial, destination, overwrite: true);
    }

    private async Task<HttpResponseMessage> SendAsync(Uri url, long from, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue(ReleaseClient.UserAgent, "1"));
        if (from > 0) request.Headers.Range = new RangeHeaderValue(from, null);
        return await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);
    }
}
