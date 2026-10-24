using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using PeerSharp.Core;

namespace Peerfluence.Tests;

internal sealed class SslTorrentTestData : IDisposable
{
    public const string Name = "ssl-test.bin";
    public byte[] Content { get; } = new byte[1024];
    public X509Certificate2 Authority { get; }
    public X509Certificate2 Certificate { get; }
    public TorrentFile Metadata { get; }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5350", Justification = "BEP 3 requires SHA-1 piece hashes.")]
    public SslTorrentTestData()
    {
        using var issuerKey = RSA.Create(2048);
        var issuer = new CertificateRequest("CN=Peerfluence Test CA", issuerKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        issuer.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        issuer.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
        Authority = issuer.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(7));
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=" + Name, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName(Name);
        request.CertificateExtensions.Add(names.Build());
        using var signed = request.Create(Authority, DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddDays(1), RandomNumberGenerator.GetBytes(16));
        Certificate = signed.CopyWithPrivateKey(key);
        var pem = Encoding.UTF8.GetBytes(Authority.ExportCertificatePem());
        using var bytes = new MemoryStream();
        bytes.Write(Encoding.UTF8.GetBytes($"d4:infod6:lengthi1024e4:name{Encoding.UTF8.GetByteCount(Name)}:{Name}12:piece lengthi16384e6:pieces20:"));
        bytes.Write(SHA1.HashData(Content));
        bytes.Write(Encoding.UTF8.GetBytes($"8:ssl-cert{pem.Length}:"));
        bytes.Write(pem);
        bytes.Write("ee"u8);
        Metadata = TorrentFile.Parse(bytes.ToArray());
    }

    public void Dispose()
    {
        Certificate.Dispose();
        Authority.Dispose();
    }
}
