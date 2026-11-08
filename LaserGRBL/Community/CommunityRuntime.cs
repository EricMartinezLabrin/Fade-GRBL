using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using LaserGRBL.Layering;
using System.Linq;

namespace LaserGRBL.Community
{
    public static class CommunityRuntime
    {
        private static CommunitySession session;
        private static bool loaded;
        public static string Root { get { return Path.Combine(GrblCore.DataPath, "Community"); } }
        public static string Server { get { return Settings.GetObject("Community.Server", ""); } }
        public static string SigningKey { get { return Settings.GetObject("Community.SigningKey", ""); } }
        private static string SessionPath { get { return Path.Combine(Root, "session.bin"); } }
        public static CommunitySession Session
        {
            get
            {
                if (!loaded)
                {
                    loaded = true;
                    try
                    {
                        if (File.Exists(SessionPath)) session = CommunityJson.Read<CommunitySession>(Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(SessionPath), null, DataProtectionScope.CurrentUser)));
                        if (session != null && !string.Equals(session.server?.TrimEnd('/'), Server.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)) session = null;
                    }
                    catch (Exception error) when (error is IOException || error is InvalidDataException || error is CryptographicException || error is ArgumentException || error is System.Runtime.Serialization.SerializationException) { session = null; }
                }
                return session;
            }
        }
        public static CommunityClient Client() { return new CommunityClient(Server, Session, SaveSession); }
        public static void SaveSession(CommunitySession value)
        {
            if (value == null) throw new ArgumentNullException("value");
            CommunitySecurity.AtomicWrite(SessionPath, ProtectedData.Protect(CommunityJson.Bytes(value), null, DataProtectionScope.CurrentUser));
            session = value; loaded = true;
        }
        public static void Logout()
        {
            if (File.Exists(SessionPath)) File.Delete(SessionPath);
            session = null; loaded = true;
        }
        public static CommunitySession RequireSession()
        {
            if (Session?.user == null) throw new InvalidOperationException(CommunityText.Get("LoginRequired"));
            return Session;
        }
        public static bool IsProtected(LaserDocument document)
        {
            return document != null && document.Layers.SelectMany(l => l.VectorItems).SelectMany(i => i.Paths).Any(p => p.Acquisition != null);
        }
        public static void DemandExport(LaserDocument document)
        {
            if (IsProtected(document)) throw new InvalidOperationException(CommunityText.Get("ExportBlocked"));
        }
        public static void ValidateAccess(LaserDocument document)
        {
            if (!IsProtected(document)) return;
            var current = RequireSession();
            foreach (var receipt in document.Layers.SelectMany(l => l.VectorItems).SelectMany(i => i.Paths).Where(p => p.Acquisition != null).Select(p => p.Acquisition).GroupBy(r => r.payload + "." + r.signature).Select(g => g.First()))
                CommunitySecurity.Verify(receipt, current.user.id, SigningKey);
        }
        public static void Configure(string server, string publicKey)
        {
            new CommunityClient(server, null, s => { });
            if (!string.IsNullOrWhiteSpace(publicKey)) using (var rsa = new RSACryptoServiceProvider())
            {
                rsa.PersistKeyInCsp = false; rsa.FromXmlString(publicKey);
                if (!rsa.PublicOnly || rsa.KeySize < 2048) throw new ArgumentException("A public RSA key of at least 2048 bits is required.");
            }
            if (!string.Equals(Server.TrimEnd('/'), server.TrimEnd('/'), StringComparison.OrdinalIgnoreCase) || SigningKey != publicKey) Logout();
            Settings.SetObject("Community.Server", new Uri(server).GetLeftPart(UriPartial.Authority));
            Settings.SetObject("Community.SigningKey", publicKey);
        }
    }
}
