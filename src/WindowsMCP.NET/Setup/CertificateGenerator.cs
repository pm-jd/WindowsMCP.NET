using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace WindowsMcpNet.Setup;

public static class CertificateGenerator
{
    public static (string certPath, string password) Generate(string baseDirectory)
    {
        var password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var certPath = Path.Combine(baseDirectory, "cert.pfx");

        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=WindowsMCP.NET",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddDnsName(Dns.GetHostName());
        sanBuilder.AddDnsName("localhost");
        sanBuilder.AddIpAddress(IPAddress.Loopback);

        foreach (var ip in GetLocalIpAddresses())
        {
            sanBuilder.AddIpAddress(ip);
        }

        request.CertificateExtensions.Add(sanBuilder.Build());
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, critical: true));
        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], critical: false));

        var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddYears(2));
        var pfxBytes = cert.Export(X509ContentType.Pfx, password);
        File.WriteAllBytes(certPath, pfxBytes);

        // Install into Windows Trusted Root CA store so clients accept the certificate
        InstallToTrustedRoot(cert);

        return (certPath, password);
    }

    private static void InstallToTrustedRoot(X509Certificate2 cert)
    {
        try
        {
            using var store = new X509Store(StoreName.Root, StoreLocation.LocalMachine);
            store.Open(OpenFlags.ReadWrite);
            store.Add(cert);
            store.Close();
            Console.WriteLine("  Certificate installed to Trusted Root CA store.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  Warning: Could not install certificate to trust store: {ex.Message}");
            Console.Error.WriteLine("  Clients may reject the self-signed certificate.");
            Console.Error.WriteLine("  Run as Administrator to install automatically, or import cert.pfx manually.");
        }
    }

    private static IEnumerable<IPAddress> GetLocalIpAddresses()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(ni => ni.OperationalStatus == OperationalStatus.Up)
            .SelectMany(ni => ni.GetIPProperties().UnicastAddresses)
            .Where(ua => ua.Address.AddressFamily == AddressFamily.InterNetwork)
            .Select(ua => ua.Address);
    }
}
