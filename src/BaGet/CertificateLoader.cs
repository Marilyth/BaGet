using System.IO;
using System.Security.Cryptography.X509Certificates;

namespace BaGet
{
    /// <summary>
    /// A class that loads a certificate from a file and keeps it up-to-date.
    /// </summary>
    public class CertificateLoader
    {
        private readonly string certLocation;
        private readonly FileSystemWatcher fileWatcher;
        private X509Certificate2 certificate;

        /// <summary>
        /// Initializes a new instance of the <see cref="CertificateLoader"/> class.
        /// A certificate loader that loads the certificate at the specified location and keeps it up-to-date.
        /// </summary>
        /// <param name="certLocation">The location of the certificate.</param>
        public CertificateLoader(string certLocation)
        {
            this.certLocation = certLocation;
            fileWatcher = new FileSystemWatcher(Path.GetDirectoryName(certLocation), Path.GetFileName(certLocation));
            fileWatcher.Changed += (sender, e) => LoadCertificate();
            fileWatcher.Created += (sender, e) => LoadCertificate();
            fileWatcher.Renamed += (sender, e) => LoadCertificate();

            fileWatcher.EnableRaisingEvents = true;

            LoadCertificate();
        }

        /// <summary>
        /// Returns the current, most up-to-date certificate at the specified location.
        /// </summary>
        /// <returns>The current certificate.</returns>
        public X509Certificate2 GetCertificate() => certificate;

        private void LoadCertificate()
        {
            if (File.Exists(certLocation))
            {
                certificate?.Dispose();
                certificate = new X509Certificate2(certLocation);
            }
        }
    }
}
