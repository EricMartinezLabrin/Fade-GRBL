using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace LaserGRBL.Community
{
    public static class CommunitySecurity
    {
        public static string Hash(byte[] bytes)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
        // Portable account envelope: magic, random IV, AES-256-CBC ciphertext, HMAC-SHA256.
        // Separate encryption/authentication keys derived from the random account key.
        public static byte[] Seal(byte[] value, string key)
        {
            byte[] material;
            var source = Convert.FromBase64String(key);
            if (source.Length != 32) throw new CryptographicException("Invalid account key.");
            using (var sha = SHA512.Create()) material = sha.ComputeHash(source);
            using (var aes = Aes.Create())
            using (var output = new MemoryStream())
            {
                aes.Key = material.Take(32).ToArray(); aes.GenerateIV();
                output.Write(Encoding.ASCII.GetBytes("FMP1"), 0, 4); output.Write(aes.IV, 0, 16);
                using (var encryptor = aes.CreateEncryptor())
                {
                    byte[] cipher = encryptor.TransformFinalBlock(value, 0, value.Length);
                    output.Write(cipher, 0, cipher.Length);
                }
                using (var hmac = new HMACSHA256(material.Skip(32).ToArray()))
                {
                    byte[] tag = hmac.ComputeHash(output.ToArray()); output.Write(tag, 0, tag.Length);
                }
                return output.ToArray();
            }
        }
        public static byte[] Open(byte[] value, string key)
        {
            if (value.Length < 68 || Encoding.ASCII.GetString(value, 0, 4) != "FMP1") throw new InvalidDataException("Invalid protected project.");
            byte[] material;
            var source = Convert.FromBase64String(key);
            if (source.Length != 32) throw new CryptographicException("Invalid account key.");
            using (var sha = SHA512.Create()) material = sha.ComputeHash(source);
            using (var hmac = new HMACSHA256(material.Skip(32).ToArray()))
            {
                byte[] tag = hmac.ComputeHash(value, 0, value.Length - 32); int difference = 0;
                for (int i = 0; i < 32; i++) difference |= tag[i] ^ value[value.Length - 32 + i];
                if (difference != 0) throw new CryptographicException("Protected project authentication failed.");
            }
            using (var aes = Aes.Create())
            {
                aes.Key = material.Take(32).ToArray(); aes.IV = value.Skip(4).Take(16).ToArray();
                using (var decryptor = aes.CreateDecryptor()) return decryptor.TransformFinalBlock(value, 20, value.Length - 52);
            }
        }
        public static CommunityEntitlement Verify(CommunityReceipt receipt, string accountId, string signingKey, string svg = null)
        {
            if (receipt == null || string.IsNullOrWhiteSpace(signingKey)) throw new CryptographicException(CommunityText.Get("MissingSigningKey"));
            byte[] payload = Convert.FromBase64String(receipt.payload);
            using (var rsa = new RSACryptoServiceProvider())
            {
                rsa.PersistKeyInCsp = false; rsa.FromXmlString(signingKey);
                if (!rsa.VerifyData(payload, CryptoConfig.MapNameToOID("SHA256"), Convert.FromBase64String(receipt.signature))) throw new CryptographicException("Invalid acquisition signature.");
            }
            var claim = CommunityJson.Read<CommunityEntitlement>(Encoding.UTF8.GetString(payload));
            if (claim == null || claim.accountId != accountId || string.IsNullOrEmpty(claim.designId) || string.IsNullOrEmpty(claim.acquisitionId) || claim.licenseVersion != "physical-products-v1") throw new CryptographicException("Invalid acquisition owner or license.");
            if (svg != null && !string.Equals(claim.sha256, Hash(Encoding.UTF8.GetBytes(svg)), StringComparison.Ordinal)) throw new CryptographicException("Acquisition content hash mismatch.");
            return claim;
        }
        public static void AtomicWrite(string path, byte[] value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temporary, value);
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
