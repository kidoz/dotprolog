using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace DotProlog.Packages.Tests;

public sealed class ArchiveDownloadTests
{
    [Theory]
    [InlineData("same-host")]
    [InlineData("other-host")]
    [InlineData("success")]
    public async Task HTTPS_downloads_reject_redirects_and_accept_direct_responses(string mode)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddHours(1));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var host = mode == "other-host" ? "localhost" : "127.0.0.1";
        var location = $"https://{host}:{port}/next.zip";
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        stop.CancelAfter(TimeSpan.FromSeconds(15));
        var requests = 0;
        var server = Serve();
        var path = Path.Combine(Path.GetTempPath(), "propack-download-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var handler = new HttpClientHandler
            {
                UseProxy = false,
                ServerCertificateCustomValidationCallback = (_, presented, _, _) =>
                    presented?.Thumbprint == certificate.Thumbprint,
            };
            var uri = new Uri($"https://127.0.0.1:{port}/package.zip");
            if (mode == "success")
            {
                await PackageFiles.DownloadArchive(uri, path, handler);
                Assert.Equal("archive bytes", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
            }
            else
            {
                var error = await Assert.ThrowsAsync<HttpRequestException>(async () =>
                    await PackageFiles.DownloadArchive(uri, path, handler)
                );
                Assert.Equal(HttpStatusCode.Found, error.StatusCode);
                Assert.False(File.Exists(path));
            }
            Assert.Equal(1, Volatile.Read(ref requests));
        }
        finally
        {
            await stop.CancelAsync();
            await server;
            File.Delete(path);
        }

        async Task Serve()
        {
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    using var client = await listener.AcceptTcpClientAsync(stop.Token);
                    using var tls = new SslStream(client.GetStream());
                    await tls.AuthenticateAsServerAsync(
                        new SslServerAuthenticationOptions { ServerCertificate = certificate },
                        stop.Token
                    );
                    using var reader = new StreamReader(tls, Encoding.ASCII, leaveOpen: true);
                    while (await reader.ReadLineAsync(stop.Token) is { Length: > 0 }) { }
                    var count = Interlocked.Increment(ref requests);
                    var response =
                        count == 1 && mode != "success"
                            ? $"HTTP/1.1 302 Found\r\nLocation: {location}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"
                            : "HTTP/1.1 200 OK\r\nContent-Length: 13\r\nConnection: close\r\n\r\narchive bytes";
                    await tls.WriteAsync(Encoding.ASCII.GetBytes(response), stop.Token);
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        }
    }
}
