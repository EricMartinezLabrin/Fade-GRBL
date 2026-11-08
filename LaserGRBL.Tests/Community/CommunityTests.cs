using LaserGRBL;
using LaserGRBL.Community;
using LaserGRBL.Layering;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Xunit;

namespace LaserGRBL.Tests.Community
{
    [Collection("Job execution")]
    public class CommunityTests
    {
        private const string Svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"20mm\" height=\"10mm\" viewBox=\"0 0 20 10\"><path fill=\"none\" stroke=\"#000000\" d=\"M0 0 L20 0 L20 10 Z\"/></svg>";
        private static CommunitySession Session(string id = "test-user")
        {
            return new CommunitySession { user = new CommunityUser { id = id, displayName = "Prueba", emailVerified = true }, libraryKey = Convert.ToBase64String(Enumerable.Repeat((byte)42, 32).ToArray()), accessToken = "old", refreshToken = "refresh", server = "https://community.invalid" };
        }
        private static LaserDocument Document()
        {
            return new LaserDocument { Layers = { new LaserLayer { Name = "Caja", Color = Color.Black, VectorItems = { new VectorItem { Paths = { new VectorPath { Closed = true, Points = { new LaserPoint(0, 0), new LaserPoint(20, 0), new LaserPoint(20, 10) } } } } } } } };
        }
        private static CommunityClient.WireResponse Response(int status, object data = null)
        { return new CommunityClient.WireResponse { Status = status, Body = data == null ? new byte[0] : CommunityJson.Bytes(data) }; }
        private static CommunityClient.WireResponse Error(int status, string code)
        { return Response(status, new { error = new { code, message = code } }); }

        // Isolates account/configuration in memory, directs any legacy Settings timer to a
        // throwaway directory, and never reads/writes a real community session.
        private sealed class Scope : IDisposable
        {
            private const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Static;
            private readonly object oldPath, oldSession, oldLoaded;
            private readonly Dictionary<string, object> settings;
            private readonly object oldKey;
            public readonly string Root = Path.Combine(Path.GetTempPath(), "fade-community-test-" + Guid.NewGuid().ToString("N"));
            public readonly RSACryptoServiceProvider Rsa = new RSACryptoServiceProvider(2048);
            public readonly CommunitySession Account = Session();
            public Scope()
            {
                Directory.CreateDirectory(Root); Rsa.PersistKeyInCsp = false;
                var path = typeof(GrblCore).GetField("mDataPath", Flags); oldPath = path.GetValue(null); path.SetValue(null, Root);
                settings = (Dictionary<string, object>)typeof(Settings).GetField("dic", Flags).GetValue(null);
                ((System.Threading.Timer)typeof(Settings).GetField("Timer", Flags).GetValue(null)).Change(Timeout.Infinite, Timeout.Infinite);
                settings.TryGetValue("Community.SigningKey", out oldKey); settings["Community.SigningKey"] = Rsa.ToXmlString(false);
                oldSession = typeof(CommunityRuntime).GetField("session", Flags).GetValue(null);
                oldLoaded = typeof(CommunityRuntime).GetField("loaded", Flags).GetValue(null);
                typeof(CommunityRuntime).GetField("session", Flags).SetValue(null, Account);
                typeof(CommunityRuntime).GetField("loaded", Flags).SetValue(null, true);
            }
            public CommunityReceipt Receipt(string svg = Svg)
            {
                var bytes = CommunityJson.Bytes(new CommunityEntitlement { accountId = Account.user.id, designId = "design-1", acquisitionId = "acq-1", sha256 = CommunitySecurity.Hash(Encoding.UTF8.GetBytes(svg)), licenseVersion = "physical-products-v1" });
                return new CommunityReceipt { payload = Convert.ToBase64String(bytes), signature = Convert.ToBase64String(Rsa.SignData(bytes, CryptoConfig.MapNameToOID("SHA256"))) };
            }
            public void Dispose()
            {
                if (oldKey == null) settings.Remove("Community.SigningKey"); else settings["Community.SigningKey"] = oldKey;
                ((System.Threading.Timer)typeof(Settings).GetField("Timer", Flags).GetValue(null)).Change(Timeout.Infinite, Timeout.Infinite);
                typeof(Settings).GetField("LastCause", Flags).SetValue(null, null);
                typeof(CommunityRuntime).GetField("session", Flags).SetValue(null, oldSession);
                typeof(CommunityRuntime).GetField("loaded", Flags).SetValue(null, oldLoaded);
                typeof(GrblCore).GetField("mDataPath", Flags).SetValue(null, oldPath);
                Rsa.Dispose(); Directory.Delete(Root, true);
            }
        }

        [Theory]
        [InlineData("http://community.invalid")]
        [InlineData("https://community.invalid/api/v1")]
        [InlineData("https://name:password@community.invalid")]
        [InlineData("https://community.invalid?token=secret")]
        public void Client_InvalidOrigin_RejectsWithoutNetwork(string address)
        { Assert.Throws<ArgumentException>(() => new CommunityClient(address, null, s => { })); }

        [Fact]
        public void Client_Login_SendsCredentialsWithoutBearerAndPersistsSession()
        {
            CommunitySession saved = null;
            var client = new CommunityClient("https://community.invalid", null, s => saved = s, wire =>
            {
                Assert.Equal("/api/v1/auth/login", wire.Uri.AbsolutePath); Assert.Null(wire.Authorization);
                Assert.Contains("persona@example.org", Encoding.UTF8.GetString(wire.Body)); return Response(200, Session());
            });
            client.Login("persona@example.org", "test password"); Assert.NotNull(saved); Assert.Equal("test-user", saved.user.id);
        }
        [Fact]
        public void Client_ExpiredToken_RenewsOnceAndPreservesIdempotency()
        {
            int count = 0;
            var client = new CommunityClient("https://community.invalid", Session(), s => { }, wire =>
            {
                count++;
                if (count == 1) { Assert.Equal("Bearer old", wire.Authorization); return Error(401, "TOKEN_EXPIRED"); }
                if (count == 2) { Assert.Null(wire.Authorization); Assert.EndsWith("auth/refresh", wire.Uri.AbsolutePath); var fresh = Session(); fresh.accessToken = "new"; return Response(200, fresh); }
                Assert.Equal("Bearer new", wire.Authorization); Assert.Equal("one-acquisition", wire.IdempotencyKey); return Response(200, new { usedBytes = 42, limitBytes = 200000000, reservedBytes = 0 });
            });
            var result = client.Request<CommunityQuota>("POST", "acquisitions", new { designId = "design-1" }, true, "one-acquisition");
            Assert.Equal(3, count); Assert.Equal(42, result.usedBytes);
        }
        [Fact]
        public void Client_SecondUnauthorized_DoesNotRetryForever()
        {
            int count = 0;
            var client = new CommunityClient("https://community.invalid", Session(), s => { }, wire => { count++; return count == 2 ? Response(200, Session()) : Error(401, "SESSION_REVOKED"); });
            Assert.Equal("SESSION_REVOKED", Assert.Throws<CommunityError>(() => client.Request<object>("GET", "me")).Code); Assert.Equal(3, count);
        }
        [Theory]
        [InlineData(507, "QUOTA_EXCEEDED")]
        [InlineData(409, "VERSION_CONFLICT")]
        [InlineData(503, "STORAGE_UNAVAILABLE")]
        public void Client_ServerFailure_PreservesStructuredError(int status, string code)
        {
            var client = new CommunityClient("https://community.invalid", Session(), s => { }, wire => Error(status, code));
            var error = Assert.Throws<CommunityError>(() => client.Request<object>("GET", "library")); Assert.Equal(code, error.Code); Assert.Equal(status, error.Status);
        }
        [Fact]
        public void Client_PublicRequest_DoesNotRequireLogin()
        {
            var client = new CommunityClient("https://community.invalid", null, s => { }, wire => { Assert.Null(wire.Authorization); return Response(204); });
            client.Request<object>("POST", "auth/register", new { email = "test@example.org" }, false);
            Assert.Equal("AUTH_REQUIRED", Assert.Throws<CommunityError>(() => client.Request<object>("GET", "designs")).Code);
        }
        [Fact]
        public void Client_Pagination_ParsesCursorAndMetadata()
        {
            var client = new CommunityClient("https://community.invalid", Session(), s => { }, wire => Response(200, new { items = new[] { new { id = "one", title = "Caja México", version = 3 } }, nextCursor = "page-2" }));
            var page = client.Request<CommunityPage<LibraryEntry>>("GET", "library?limit=1"); Assert.Equal("page-2", page.nextCursor); Assert.Equal(3, page.items.Single().version);
        }
        [Fact]
        public void Json_QuotesUnicodeAndNewlines_RoundTrips()
        {
            var original = new LibraryEntry { title = "México \"árbol\"\n\\nuevo", tags = new[] { "niñez" } };
            var result = CommunityJson.Read<LibraryEntry>(CommunityJson.Write(original)); Assert.Equal(original.title, result.title); Assert.Equal(original.tags, result.tags);
        }
        [Fact]
        public void Cipher_PortableEnvelope_RejectsTamperingAndWrongKey()
        {
            string key = Session().libraryKey; var data = Encoding.UTF8.GetBytes("private geometría");
            var first = CommunitySecurity.Seal(data, key); var second = CommunitySecurity.Seal(data, key);
            Assert.NotEqual(first, second); Assert.Equal(data, CommunitySecurity.Open(first, key));
            first[25] ^= 1; Assert.Throws<CryptographicException>(() => CommunitySecurity.Open(first, key));
            Assert.Throws<CryptographicException>(() => CommunitySecurity.Open(second, Convert.ToBase64String(new byte[32])));
        }
        [Fact]
        public void Receipt_WrongOwnerOrGeometryOrSignature_Rejects()
        {
            using (var scope = new Scope())
            {
                var receipt = scope.Receipt(); var key = scope.Rsa.ToXmlString(false);
                Assert.Equal("design-1", CommunitySecurity.Verify(receipt, scope.Account.user.id, key, Svg).designId);
                Assert.Throws<CryptographicException>(() => CommunitySecurity.Verify(receipt, "another-user", key, Svg));
                Assert.Throws<CryptographicException>(() => CommunitySecurity.Verify(receipt, scope.Account.user.id, key, Svg + " "));
                receipt.signature = Convert.ToBase64String(new byte[256]); Assert.Throws<CryptographicException>(() => CommunitySecurity.Verify(receipt, scope.Account.user.id, key));
            }
        }
        [Theory]
        [InlineData("<svg xmlns='http://www.w3.org/2000/svg'><script>alert(1)</script></svg>")]
        [InlineData("<!DOCTYPE svg [<!ENTITY x SYSTEM 'file:///secret'>]><svg xmlns='http://www.w3.org/2000/svg'>&x;</svg>")]
        [InlineData("<svg xmlns='http://www.w3.org/2000/svg'><path onload='alert(1)'/></svg>")]
        [InlineData("<svg xmlns='http://www.w3.org/2000/svg'><image href='https://invalid/image'/></svg>")]
        [InlineData("<svg xmlns='http://www.w3.org/2000/svg'><path fill='url (https://invalid)'/></svg>")]
        [InlineData("<svg xmlns='http://www.w3.org/2000/svg'><use href='#x'/></svg>")]
        public void Svg_UnsafeContent_RejectsBeforeImport(string svg)
        { Assert.ThrowsAny<Exception>(() => CommunitySvg.Validate(svg)); }
        [Fact]
        public void Svg_Export_AppliesAffineTransformsPreservesGroupAndDimensions()
        {
            var doc = Document(); var layer = doc.Layers[0]; layer.ScaleX = 2; layer.ScaleY = 3; layer.TranslateX = 15;
            var group = new CanvasGroup { Name = "Caja" }; doc.Groups.Add(group); layer.VectorItems[0].Paths[0].GroupId = group.Id;
            string exported = CommunitySvg.Export(doc); Assert.Contains("width=\"40mm\"", exported); Assert.Contains("height=\"30mm\"", exported); Assert.Contains("g" + group.Id.ToString("N"), exported);
            var restored = CommunitySvg.Import(exported); var bounds = LayerGeometry.TransformedBounds(restored.Layers[0].VectorItems[0].Paths[0], restored.Layers[0]);
            Assert.InRange(bounds.Width, 39.9f, 40.1f); Assert.InRange(bounds.Height, 29.9f, 30.1f); Assert.All(restored.Layers, l => Assert.False(l.OutputEnabled));
        }
        [Fact]
        public void Svg_RasterDocument_RejectsInsteadOfOmittingImage()
        { var doc = Document(); doc.Layers.Add(new LaserLayer { ContentKind = LayerContentKind.Raster }); Assert.Throws<InvalidOperationException>(() => CommunitySvg.Export(doc)); }
        [Fact]
        public void Library_LocalRoundTrip_PreservesTextAndMachineSettings()
        {
            using (var scope = new Scope())
            {
                var doc = Document(); doc.Layers[0].SpeedMmPerMinute = 1234; doc.Layers[0].VectorItems[0].Text = new TextObject { Content = "México" };
                var library = new PersonalLibrary(scope.Account, scope.Root); var entry = library.Save(doc, "Caja", new[] { "madera" });
                var restored = library.Open(entry.id); Assert.Equal(1234, restored.Layers[0].SpeedMmPerMinute); Assert.Equal("México", restored.Layers[0].VectorItems[0].Text.Content);
                var other = new PersonalLibrary(Session("another-account"), scope.Root); int unreadable; Assert.Empty(other.List(out unreadable));
                Assert.Throws<InvalidDataException>(() => library.Read("../../escape"));
            }
        }
        [Fact]
        public void Library_CorruptEntry_ReportsAndPreservesFile()
        {
            using (var scope = new Scope())
            {
                var library = new PersonalLibrary(scope.Account, scope.Root); var entry = library.Save(Document(), "Caja", new string[0]);
                string path = Directory.GetFiles(scope.Root, "*.fbl", SearchOption.AllDirectories).Single(); File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
                int unreadable; Assert.Empty(library.List(out unreadable)); Assert.Equal(1, unreadable); Assert.True(File.Exists(path));
            }
        }
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Library_FailedBackup_KeepsLocalAndCancelsIncompleteReservation(bool putSucceeds)
        {
            using (var scope = new Scope())
            {
                bool canceled = false;
                var library = new PersonalLibrary(scope.Account, scope.Root, (ticket, body) => { if (!putSucceeds) throw new IOException("Truncated PUT"); return new byte[0]; });
                var entry = library.Save(Document(), "Caja", new string[0]);
                var client = new CommunityClient(scope.Account.server, scope.Account, s => { }, wire =>
                {
                    if (wire.Method == "DELETE") { canceled = true; return Response(204); }
                    if (wire.Uri.AbsolutePath.EndsWith("/complete")) return Error(409, "UPLOAD_INCOMPLETE");
                    return Response(200, new CommunityUpload { id = "upload-1", method = "PUT", url = "https://objects.invalid/file" });
                });
                Assert.ThrowsAny<Exception>(() => library.Backup(entry.id, client)); Assert.True(canceled); Assert.Null(library.Read(entry.id).entry.remoteId); Assert.NotNull(library.Open(entry.id));
            }
        }
        [Fact]
        public void Library_QuotaExhausted_KeepsLocalWithoutObjectTransfer()
        {
            using (var scope = new Scope())
            {
                var library = new PersonalLibrary(scope.Account, scope.Root, (ticket, body) => throw new Exception("Must not upload"));
                var entry = library.Save(Document(), "Caja", new string[0]);
                var client = new CommunityClient(scope.Account.server, scope.Account, s => { }, wire => Error(507, "QUOTA_EXCEEDED"));
                Assert.Equal("QUOTA_EXCEEDED", Assert.Throws<CommunityError>(() => library.Backup(entry.id, client)).Code); Assert.NotNull(library.Open(entry.id));
            }
        }
        [Fact]
        public void Acquisition_CopyHistorySaveAndCompile_RetainsProtection()
        {
            using (var scope = new Scope())
            {
                var library = new PersonalLibrary(scope.Account, scope.Root);
                var content = new CommunityContent { id = "acq-1", designId = "design-1", title = "Caja", svg = Svg, receipt = scope.Receipt() };
                var entry = library.Acquire(content, scope.Rsa.ToXmlString(false)); Assert.Equal(entry.id, library.Acquire(content, scope.Rsa.ToXmlString(false)).id);
                var doc = library.Open(entry.id); var original = doc.Layers[0];
                var clipboard = new CanvasClipboard(); clipboard.CopyObjects(doc, new[] { new VectorSelection(original, original.VectorItems[0], original.VectorItems[0].Paths[0]) });
                var pasted = Document(); clipboard.Paste(pasted, false); CanvasGroups.MergeColors(pasted);
                Assert.True(CommunityRuntime.IsProtected(pasted)); Assert.Throws<InvalidOperationException>(() => CommunitySvg.Export(pasted));
                var history = new CanvasHistory(); history.Reset(pasted); pasted.Layers[0].TranslateX = 30; history.Commit(pasted, new CanvasSelectionState());
                CanvasSelectionState selection; var undone = history.Restore(false, out selection); Assert.True(CommunityRuntime.IsProtected(undone));
                byte[] bytes = LayerProjectSerializer.ToBytes(pasted); Assert.Equal("FMP1", Encoding.ASCII.GetString(bytes, 0, 4)); Assert.True(CommunityRuntime.IsProtected(LayerProjectSerializer.FromBytes(bytes)));
                Assert.True(LayeredJobCompiler.Compile(pasted, 1000, "M4", "M5").ExportRestricted);
                pasted.PassOrder = JobPassOrder.ByObject; Assert.True(LayeredJobCompiler.Compile(pasted, 1000, "M4", "M5").ExportRestricted);
                var file = new GrblFile(); typeof(GrblFile).GetMethod("LoadCommandsWithProtection", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(file, new object[] { new[] { new GrblCommand("G1 X1") }, "protected", false, true });
                Assert.Throws<InvalidOperationException>(() => file.SaveGCODE(Path.Combine(scope.Root, "escape.nc"), false, false, false, 1, false, null)); Assert.False(File.Exists(Path.Combine(scope.Root, "escape.nc")));
            }
        }
        [Fact]
        public void ProtectedProject_WrongAccountKey_CannotOpen()
        {
            using (var scope = new Scope())
            {
                var doc = Document(); doc.Layers[0].VectorItems[0].Paths[0].Acquisition = scope.Receipt();
                byte[] bytes = LayerProjectSerializer.ToBytes(doc); scope.Account.libraryKey = Convert.ToBase64String(new byte[32]);
                Assert.Throws<CryptographicException>(() => LayerProjectSerializer.FromBytes(bytes));
            }
        }
        [Fact]
        public void Library_BackupAndRestore_OnSecondComputerPreservesProject()
        {
            using (var scope = new Scope())
            {
                byte[] stored = null;
                var source = new PersonalLibrary(scope.Account, Path.Combine(scope.Root, "pc-one"), (ticket, body) => { stored = body; return new byte[0]; });
                var local = source.Save(Document(), "Caja", new[] { "madera" });
                var remote = new LibraryEntry { id = "remote-1", title = "Caja", version = 1 };
                var client = new CommunityClient(scope.Account.server, scope.Account, s => { }, wire =>
                {
                    if (wire.Uri.AbsolutePath.EndsWith("/complete")) return Response(200, remote);
                    return Response(200, new CommunityUpload { id = "upload-1", url = "https://objects.invalid/file", method = wire.Uri.AbsolutePath.EndsWith("/content") ? "GET" : "PUT" });
                });
                Assert.Equal("remote-1", source.Backup(local.id, client).remoteId); Assert.NotNull(stored);
                var target = new PersonalLibrary(scope.Account, Path.Combine(scope.Root, "pc-two"), (ticket, body) => stored);
                var restored = target.Restore(remote, client); Assert.Equal(local.id, restored.id); Assert.Equal(1, restored.version);
                Assert.Equal(3, target.Open(local.id).Layers[0].VectorItems[0].Paths[0].Points.Count);
            }
        }
        [Fact]
        public void Library_TruncatedRestore_DoesNotReplaceExistingCopy()
        {
            using (var scope = new Scope())
            {
                var library = new PersonalLibrary(scope.Account, scope.Root, (ticket, body) => new byte[] { 1, 2, 3 });
                var local = library.Save(Document(), "Original", new string[0]);
                var client = new CommunityClient(scope.Account.server, scope.Account, s => { }, wire => Response(200, new CommunityUpload { method = "GET", url = "https://objects.invalid/file" }));
                Assert.Throws<InvalidDataException>(() => library.Restore(new LibraryEntry { id = "remote-1", version = 2 }, client));
                Assert.Equal("Original", library.Read(local.id).entry.title); Assert.NotNull(library.Open(local.id));
            }
        }
        [Fact]
        public void Svg_TextContours_ExportsGeometryWithoutFontDependency()
        {
            var doc = new LaserDocument();
            var layer = new LaserLayer { Color = Color.Black }; doc.Layers.Add(layer);
            layer.VectorItems.Add(CanvasObjects.Text(new TextObject { Content = "México", FontFamily = "Arial", SizeMm = 10 }, PointF.Empty));
            var svg = CommunitySvg.Export(doc); Assert.Contains("<path", svg); Assert.DoesNotContain("<text", svg); Assert.DoesNotContain("font-family", svg);
            Assert.NotEmpty(CommunitySvg.Import(svg).Layers);
        }
        [Fact]
        public void Client_RefreshChangesAccount_RejectsBeforePersisting()
        {
            bool persisted = false;
            var client = new CommunityClient("https://community.invalid", Session(), s => persisted = true, wire => wire.Uri.AbsolutePath.EndsWith("/refresh") ? Response(200, Session("other-user")) : Error(401, "TOKEN_EXPIRED"));
            Assert.Throws<InvalidDataException>(() => client.Request<object>("GET", "library")); Assert.False(persisted); Assert.Equal("test-user", client.Session.user.id);
        }
        [Fact]
        public void OwnProject_SaveWithoutSession_PreservesLegacyGzip()
        {
            using (var scope = new Scope())
            {
                typeof(CommunityRuntime).GetField("session", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, null);
                byte[] bytes = LayerProjectSerializer.ToBytes(Document()); Assert.Equal(0x1f, bytes[0]); Assert.Equal(0x8b, bytes[1]); Assert.Single(LayerProjectSerializer.FromBytes(bytes).Layers);
                Assert.NotEmpty(CommunitySvg.Export(Document()));
            }
        }
        [Fact]
        public void Response_ExceedsLimit_RejectsIncompleteTransfer()
        { using (var stream = new MemoryStream(new byte[100])) Assert.Throws<InvalidDataException>(() => CommunityClient.ReadLimited(stream, 99)); }
        [Fact]
        public void CommunityWindow_OpensWithoutNetwork_UsesRealControls()
        {
            using (var scope = new Scope())
            {
                Exception error = null;
                var thread = new Thread(() =>
                {
                    try
                    {
                        using (var form = new CommunityForm(null))
                        {
                            form.Show(); form.PerformLayout();
                            var pages = (TabControl)typeof(CommunityForm).GetField("pages", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
                            Assert.Equal(3, pages.TabCount); Assert.True(pages.Width > 0); form.Close();
                        }
                    }
                    catch (Exception e) { error = e; }
                }); thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
                if (error != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
            }
        }
    }
}
