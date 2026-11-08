using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using LaserGRBL.Layering;

namespace LaserGRBL.Community
{
    public sealed class PersonalLibrary
    {
        private readonly CommunitySession account;
        private readonly string directory;
        private readonly Func<CommunityUpload, byte[], byte[]> transfer;
        public PersonalLibrary(CommunitySession session, string storageRoot = null, Func<CommunityUpload, byte[], byte[]> objectTransfer = null)
        {
            if (session?.user == null) throw new ArgumentNullException("session");
            account = session;
            transfer = objectTransfer ?? CommunityClient.Transfer;
            directory = Path.Combine(storageRoot ?? CommunityRuntime.Root, CommunitySecurity.Hash(Encoding.UTF8.GetBytes(session.server + "\n" + session.user.id)));
        }
        private string PathFor(string id)
        {
            Guid parsed; if (!Guid.TryParseExact(id, "N", out parsed)) throw new InvalidDataException("Invalid library ID.");
            return Path.Combine(directory, id + ".fbl");
        }
        public List<LibraryEntry> List(out int unreadable)
        {
            unreadable = 0; var result = new List<LibraryEntry>();
            if (!Directory.Exists(directory)) return result;
            foreach (var file in Directory.GetFiles(directory, "*.fbl"))
            {
                try { result.Add(Read(Path.GetFileNameWithoutExtension(file)).entry); }
                catch (Exception e) when (e is IOException || e is InvalidDataException || e is System.Security.Cryptography.CryptographicException || e is ArgumentException || e is System.Runtime.Serialization.SerializationException) { unreadable++; }
            }
            return result.OrderBy(e => e.title).ToList();
        }
        public LibraryBundle Read(string id)
        {
            if (new FileInfo(PathFor(id)).Length > 100000000) throw new InvalidDataException("Library entry exceeds limit.");
            var result = Decode(File.ReadAllBytes(PathFor(id)));
            if (result?.entry == null || result.entry.id != id) throw new InvalidDataException("Invalid library entry.");
            return result;
        }
        public byte[] Encode(LibraryBundle bundle) { return CommunitySecurity.Seal(CommunityJson.Bytes(bundle), account.libraryKey); }
        public LibraryBundle Decode(byte[] bytes) { return CommunityJson.Read<LibraryBundle>(Encoding.UTF8.GetString(CommunitySecurity.Open(bytes, account.libraryKey))); }
        public void Write(LibraryBundle bundle)
        {
            if (bundle?.entry == null || string.IsNullOrWhiteSpace(bundle.entry.title) || bundle.entry.title.Length > 120 || (bundle.entry.tags != null && (bundle.entry.tags.Length > 20 || bundle.entry.tags.Any(t => string.IsNullOrWhiteSpace(t) || t.Length > 40)))) throw new InvalidDataException(CommunityText.Get("InvalidMetadata"));
            bundle.entry.updatedAt = DateTime.UtcNow.ToString("o");
            var bytes = Encode(bundle);
            if (bytes.Length > 100000000) throw new InvalidDataException("Library entry exceeds 100 MB limit.");
            CommunitySecurity.AtomicWrite(PathFor(bundle.entry.id), bytes);
        }
        public LibraryEntry Save(LaserDocument document, string title, string[] tags)
        {
            var bundle = new LibraryBundle { entry = new LibraryEntry { title = title, tags = tags, containsAcquired = CommunityRuntime.IsProtected(document) }, project = Convert.ToBase64String(LayerProjectSerializer.ToBytes(document)), thumbnail = CommunitySvg.Thumbnail(document) };
            Write(bundle); return bundle.entry;
        }
        public LibraryEntry Acquire(CommunityContent content, string publicKey)
        {
            var claim = CommunitySecurity.Verify(content.receipt, account.user.id, publicKey, content.svg);
            if (claim.acquisitionId != content.id || claim.designId != content.designId) throw new InvalidDataException("Acquisition ID mismatch.");
            int unreadable; var existing = List(out unreadable).FirstOrDefault(e => e.acquisitionId == content.id);
            if (existing != null) return existing;
            var document = CommunitySvg.Import(content.svg);
            foreach (var path in document.Layers.SelectMany(l => l.VectorItems).SelectMany(i => i.Paths)) path.Acquisition = content.receipt;
            var bundle = new LibraryBundle { entry = new LibraryEntry { title = content.title, acquisitionId = content.id, designId = claim.designId, containsAcquired = true }, project = Convert.ToBase64String(LayerProjectSerializer.ToBytes(document)), thumbnail = CommunitySvg.Thumbnail(document) };
            Write(bundle); return bundle.entry;
        }
        public LaserDocument Open(string id) { return LayerProjectSerializer.FromBytes(Convert.FromBase64String(Read(id).project)); }
        public void Delete(string id) { File.Delete(PathFor(id)); }
        public LibraryEntry Backup(string id, CommunityClient client)
        {
            var bundle = Read(id);
            if (bundle.entry.acquisitionId != null) throw new InvalidOperationException(CommunityText.Get("AcquiredNoBackup"));
            byte[] bytes = Encode(bundle);
            if (bytes.Length > 100000000) throw new InvalidDataException("Backup exceeds 100 MB limit.");
            var upload = client.Request<CommunityUpload>("POST", "uploads", new { purpose = "library", clientEntryId = bundle.entry.id, libraryId = bundle.entry.remoteId, version = bundle.entry.version, sizeBytes = bytes.LongLength, sha256 = CommunitySecurity.Hash(bytes), contentType = "application/octet-stream" }, true, Guid.NewGuid().ToString("N"));
            try
            {
                transfer(upload, bytes);
                var remote = client.Request<LibraryEntry>("POST", "uploads/" + Uri.EscapeDataString(upload.id) + "/complete", new { title = bundle.entry.title, tags = bundle.entry.tags }, true, upload.id);
                bundle.entry.remoteId = remote.id; bundle.entry.version = remote.version; Write(bundle); return bundle.entry;
            }
            catch
            {
                try { client.Request<object>("DELETE", "uploads/" + Uri.EscapeDataString(upload.id)); } catch (Exception) { /* Reservations also expire on server. */ }
                throw;
            }
        }
        public LibraryEntry Restore(LibraryEntry remote, CommunityClient client)
        {
            var ticket = client.Request<CommunityUpload>("POST", "library/" + Uri.EscapeDataString(remote.id) + "/content");
            var bundle = Decode(transfer(ticket, null));
            // Validate project and entitlements before replacing a local copy.
            LayerProjectSerializer.FromBytes(Convert.FromBase64String(bundle.project));
            bundle.entry.remoteId = remote.id; bundle.entry.version = remote.version; Write(bundle); return bundle.entry;
        }
    }
}
