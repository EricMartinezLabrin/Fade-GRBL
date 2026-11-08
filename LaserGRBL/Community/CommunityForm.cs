using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using LaserGRBL.Layering;
using LaserGRBL.UserControls;

namespace LaserGRBL.Community
{
    public partial class CommunityForm : Form
    {
        private readonly GrblCore core;
        private bool busy;
        private string nextCursor;
        private List<LibraryEntry> localEntries = new List<LibraryEntry>();
        private CommunityClient Client { get { return CommunityRuntime.Client(); } }
        private PersonalLibrary Library { get { return new PersonalLibrary(CommunityRuntime.RequireSession()); } }
        public CommunityForm(GrblCore machine)
        {
            core = machine; InitializeComponent();
            Add(header, "Configure", Configure); Add(header, "Login", Login); Add(header, "Register", Register); Add(header, "Recovery", Recovery); Add(header, "Logout", Logout);
            Add(libraryActions, "RefreshLocal", RefreshLocal);
            Add(libraryActions, "SaveCurrent", SaveCurrent); Add(libraryActions, "ImportSvg", ImportOwn);
            Add(libraryActions, "Open", OpenLocal); Add(libraryActions, "Backup", Backup); Add(libraryActions, "CloudRefresh", CloudList);
            Add(libraryActions, "InsertLocal", () => { EditingAllowed(); core.LayerController.InsertLibraryDocument(Library.Open(Local().id)); return Task.CompletedTask; });
            Add(libraryActions, "EditLocal", EditLocal);
            Add(libraryActions, "Restore", Restore); Add(libraryActions, "DeleteCloud", DeleteCloud); Add(libraryActions, "DeleteLocal", DeleteLocal);
            Add(marketActions, "Search", () => Search(false)); Add(marketActions, "More", () => Search(true));
            Add(marketActions, "Details", Details); Add(marketActions, "DesignerCatalog", DesignerCatalog);
            Add(marketActions, "AllDesigners", async () => { designer.Clear(); await Search(false); });
            Add(marketActions, "Acquire", Acquire); Add(marketActions, "Publish", Publish); Add(marketActions, "EditPublication", EditPublication);
            Add(marketActions, "Withdraw", Withdraw); Add(marketActions, "Report", Report); Add(marketActions, "MyAcquisitions", MyAcquisitions);
            Add(profileActions, "Refresh", RefreshProfile); Add(profileActions, "SaveProfile", SaveProfile); Add(profileActions, "VerifyEmail", VerifyEmail); Add(profileActions, "ResendVerification", ResendVerification);
            librarySearch.TextChanged += (s, e) => FilterLocal();
            libraryKind.SelectedIndexChanged += (s, e) => FilterLocal();
            localList.SelectedIndexChanged += (s, e) => ShowPreview();
            marketList.SelectedIndexChanged += (s, e) => ShowDetails();
            FormClosing += (s, e) => { if (busy) { e.Cancel = true; status.Text = CommunityText.Get("Busy"); } };
            FormClosed += (s, e) => { if (preview.Image != null) preview.Image.Dispose(); if (marketPreview.Image != null) marketPreview.Image.Dispose(); };
            Shown += (s, e) => FitScreen(this);
            FadeTheme.Apply(this);
        }
        private void Add(FlowLayoutPanel parent, string key, Func<Task> action)
        {
            var button = new Button { Text = CommunityText.Get(key), AutoSize = true, MinimumSize = new Size(80, 36), Margin = new Padding(4), AccessibleName = CommunityText.Get(key) };
            if (key == "SaveCurrent" || key == "Acquire" || key == "SaveProfile" || key == "Login") FadeTheme.SetButtonRole(button, FadeButtonRole.Primary);
            button.Click += async (s, e) => await Run(action); parent.Controls.Add(button);
        }
        private async Task Run(Func<Task> action)
        {
            if (busy) return;
            busy = true; status.Text = CommunityText.Get("Busy");
            try { await action(); if (!IsDisposed && status.Text == CommunityText.Get("Busy")) status.Text = CommunityText.Get("Ready"); }
            catch (Exception e) { if (!IsDisposed) status.Text = e.Message; }
            finally { busy = false; }
        }
        public async void OpenPage(int page)
        {
            pages.SelectedIndex = page;
            await Run(async () =>
            {
                if (string.IsNullOrWhiteSpace(CommunityRuntime.Server)) await Configure();
                if (CommunityRuntime.Session == null) { status.Text = CommunityText.Get("LoginRequired"); return; }
                await RefreshLocal(); SetProfile(CommunityRuntime.Session.user);
                if (page == 1) await Search(false);
            });
        }
        private async Task<string[]> Ask(string title, string[] labels, string[] values = null, int secret = -1)
        {
            var completion = new TaskCompletionSource<string[]>();
            var form = new Form { Text = CommunityText.Get(title), StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(510, Math.Min(660, 130 + labels.Length * 65)), MinimumSize = new Size(330, 240), AutoScaleMode = AutoScaleMode.Dpi, Font = FadeTheme.BodyFont };
            var fields = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(16) };
            var boxes = new List<TextBox>();
            for (int i = 0; i < labels.Length; i++)
            {
                fields.Controls.Add(new Label { Text = CommunityText.Get(labels[i]), AutoSize = true, MaximumSize = new Size(440, 0) });
                var box = new TextBox { Text = values != null && i < values.Length ? values[i] ?? "" : "", Width = 440, AccessibleName = CommunityText.Get(labels[i]), UseSystemPasswordChar = i == secret, Margin = new Padding(0, 4, 0, 12) };
                fields.Controls.Add(box); boxes.Add(box);
            }
            var buttons = Actions(); buttons.Dock = DockStyle.Bottom;
            var ok = new Button { Text = CommunityText.Get("Accept"), AutoSize = true, Height = 36 };
            var cancel = new Button { Text = CommunityText.Get("Cancel"), AutoSize = true, Height = 36 };
            string[] result = null;
            ok.Click += (s, e) => { result = boxes.Select(b => b.Text).ToArray(); form.Close(); };
            cancel.Click += (s, e) => form.Close(); buttons.Controls.Add(ok); buttons.Controls.Add(cancel);
            form.Controls.Add(fields); form.Controls.Add(buttons); form.AcceptButton = ok; form.CancelButton = cancel;
            form.FormClosed += (s, e) => { completion.TrySetResult(result); form.Dispose(); };
            form.Resize += (s, e) =>
            {
                int width = Math.Max(200, fields.ClientSize.Width - 45);
                foreach (var box in boxes) box.Width = width;
                foreach (Control label in fields.Controls) if (label is Label) label.MaximumSize = new Size(width, 0);
            };
            form.Shown += (s, e) => FitScreen(form);
            FadeTheme.Apply(form); form.Show(this); return await completion.Task;
        }
        private static void FitScreen(Form form)
        {
            var area = Screen.FromControl(form).WorkingArea;
            form.MinimumSize = new Size(Math.Min(form.MinimumSize.Width, area.Width), Math.Min(form.MinimumSize.Height, area.Height));
            form.Size = new Size(Math.Min(form.Width, area.Width), Math.Min(form.Height, area.Height));
            form.Location = new Point(Math.Max(area.Left, Math.Min(form.Left, area.Right - form.Width)), Math.Max(area.Top, Math.Min(form.Top, area.Bottom - form.Height)));
        }
        private void EditingAllowed()
        {
            if (core == null || core.JobEditingLocked || !core.CanLoadNewFile) throw new InvalidOperationException(CommunityText.Get("EditingLocked"));
        }
        private async Task Configure()
        {
            if (core != null && core.JobEditingLocked) throw new InvalidOperationException(CommunityText.Get("EditingLocked"));
            if (CommunityRuntime.IsProtected(core?.LayerController.Document)) throw new InvalidOperationException(CommunityText.Get("CloseProtectedFirst"));
            var values = await Ask("Configure", new[] { "Server", "SigningKey" }, new[] { CommunityRuntime.Server, CommunityRuntime.SigningKey });
            if (values == null) return;
            core?.LayerController.ClearCommunityHistory();
            CommunityRuntime.Configure(values[0].Trim(), values[1].Trim()); await RefreshLocal();
        }
        private async Task Login()
        {
            if (core != null && core.JobEditingLocked) throw new InvalidOperationException(CommunityText.Get("EditingLocked"));
            if (CommunityRuntime.IsProtected(core?.LayerController.Document)) throw new InvalidOperationException(CommunityText.Get("CloseProtectedFirst"));
            var values = await Ask("Login", new[] { "Email", "Password" }, null, 1); if (values == null) return;
            var client = Client; await Task.Run(() => client.Login(values[0].Trim(), values[1]));
            core?.LayerController.ClearCommunityHistory();
            await RefreshLocal(); SetProfile(CommunityRuntime.Session.user);
        }
        private async Task Register()
        {
            var values = await Ask("Register", new[] { "Email", "Password", "DisplayName" }, null, 1); if (values == null) return;
            await Task.Run(() => Client.Request<object>("POST", "auth/register", new { email = values[0].Trim(), password = values[1], displayName = values[2].Trim() }, false));
            await Login();
        }
        private async Task Recovery()
        {
            var values = await Ask("Recovery", new[] { "Email" }); if (values == null) return;
            await Task.Run(() => Client.Request<object>("POST", "auth/password/forgot", new { email = values[0].Trim() }, false));
            values = await Ask("ResetPassword", new[] { "Token", "NewPassword" }, null, 1); if (values == null) return;
            await Task.Run(() => Client.Request<object>("POST", "auth/password/reset", new { token = values[0].Trim(), password = values[1] }, false));
        }
        private async Task Logout()
        {
            if (CommunityRuntime.Session == null) return;
            if (core != null && core.JobEditingLocked) throw new InvalidOperationException(CommunityText.Get("EditingLocked"));
            if (CommunityRuntime.IsProtected(core?.LayerController.Document))
            {
                var answer = await Ask("SaveBeforeLogout", new[] { "Confirmation" }, new[] { "" });
                if (answer == null || answer[0] != "GUARDAR") return;
                await SaveCurrent();
                EditingAllowed(); core.LayerController.NewDocument();
            }
            var client = Client;
            try { await Task.Run(() => client.Request<object>("POST", "auth/logout", new { refreshToken = client.Session.refreshToken }, false)); }
            finally { core?.LayerController.ClearCommunityHistory(); CommunityRuntime.Logout(); localList.Items.Clear(); remoteList.Items.Clear(); marketList.Items.Clear(); profileName.Clear(); profileBio.Clear(); profileEmail.Text = ""; }
        }
        private static string[] Tags(string value) { return value.Split(',').Select(t => t.Trim()).Where(t => t.Length > 0).Distinct().ToArray(); }
        private async Task RefreshLocal()
        {
            if (CommunityRuntime.Session == null) { localEntries.Clear(); FilterLocal(); return; }
            var library = Library;
            var result = await Task.Run(() => { int unreadable; var entries = library.List(out unreadable); return Tuple.Create(entries, unreadable); });
            if (IsDisposed) return;
            localEntries = result.Item1; FilterLocal();
            if (result.Item2 > 0) status.Text = string.Format(CommunityText.Get("CorruptEntries"), result.Item2);
        }
        private void FilterLocal()
        {
            var query = librarySearch.Text.Trim(); localList.Items.Clear();
            foreach (var entry in localEntries.Where(e => (libraryKind.SelectedIndex == 0 || (libraryKind.SelectedIndex == 2) == (e.containsAcquired || e.acquisitionId != null)) && ((e.title ?? "") + " " + string.Join(" ", e.tags ?? new string[0])).IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0)) localList.Items.Add(entry);
        }
        private LibraryEntry Local() { return localList.SelectedItem as LibraryEntry ?? throw new InvalidOperationException(CommunityText.Get("SelectDesign")); }
        private LibraryEntry Remote() { return remoteList.SelectedItem as LibraryEntry ?? throw new InvalidOperationException(CommunityText.Get("SelectDesign")); }
        private CommunityDesign Design() { return marketList.SelectedItem as CommunityDesign ?? throw new InvalidOperationException(CommunityText.Get("SelectDesign")); }
        private async Task SaveCurrent()
        {
            CommunityRuntime.RequireSession(); EditingAllowed();
            var document = core.LayerController.Document;
            if (document == null) throw new InvalidOperationException(CommunityText.Get("SelectDesign"));
            var fields = await Ask("SaveCurrent", new[] { "Title", "Tags" }); if (fields == null) throw new OperationCanceledException(CommunityText.Get("Canceled"));
            EditingAllowed(); Library.Save(core.LayerController.Document.Clone(), fields[0].Trim(), Tags(fields[1])); core.LayerController.MarkLibrarySaved(); await RefreshLocal();
        }
        private async Task ImportOwn()
        {
            CommunityRuntime.RequireSession(); EditingAllowed();
            // File picker is only used while the machine is idle.
            using (var picker = new OpenFileDialog { Filter = "SVG|*.svg", CheckFileExists = true })
            {
                if (picker.ShowDialog(this) != DialogResult.OK) return;
                if (new FileInfo(picker.FileName).Length > CommunitySvg.MaximumBytes) throw new InvalidDataException(CommunityText.Get("InvalidSvg"));
                var document = CommunitySvg.Import(File.ReadAllText(picker.FileName));
                var fields = await Ask("ImportSvg", new[] { "Title", "Tags" }, new[] { Path.GetFileNameWithoutExtension(picker.FileName), "" });
                if (fields == null) return; Library.Save(document, fields[0].Trim(), Tags(fields[1])); await RefreshLocal();
            }
        }
        private Task OpenLocal()
        {
            EditingAllowed(); var document = Library.Open(Local().id);
            if (core.LayerController.Document?.IsDirty == true) throw new InvalidOperationException(CommunityText.Get("SaveChangesFirst"));
            core.LayerController.OpenLibraryDocument(document); return Task.CompletedTask;
        }
        private async Task Backup() { var id = Local().id; var library = Library; var client = Client; await Task.Run(() => library.Backup(id, client)); await RefreshLocal(); await CloudList(); }
        private async Task EditLocal()
        {
            var entry = Local(); var library = Library; var bundle = await Task.Run(() => library.Read(entry.id));
            var fields = await Ask("EditLocal", new[] { "Title", "Tags" }, new[] { entry.title, string.Join(", ", entry.tags ?? new string[0]) }); if (fields == null) return;
            bundle.entry.title = fields[0].Trim(); bundle.entry.tags = Tags(fields[1]); await Task.Run(() => library.Write(bundle)); await RefreshLocal();
        }
        private async Task CloudList()
        {
            var client = Client;
            var entries = await Task.Run(() => AllPages<LibraryEntry>(client, "library"));
            var usage = await Task.Run(() => client.Request<CommunityQuota>("GET", "me/quota"));
            remoteList.Items.Clear(); foreach (var entry in entries) remoteList.Items.Add(entry);
            quota.Text = string.Format(CommunityText.Get("Quota"), usage.usedBytes / 1000000.0, usage.reservedBytes / 1000000.0, usage.limitBytes / 1000000.0);
        }
        private static List<T> AllPages<T>(CommunityClient client, string path)
        {
            var result = new List<T>(); string cursor = null; var seen = new HashSet<string>();
            do
            {
                var page = client.Request<CommunityPage<T>>("GET", path + "?limit=50" + (cursor == null ? "" : "&cursor=" + Uri.EscapeDataString(cursor)));
                result.AddRange(page.items); cursor = page.nextCursor;
                if (cursor != null && !seen.Add(cursor)) throw new InvalidDataException("Repeated API cursor.");
            } while (cursor != null);
            return result;
        }
        private async Task Restore()
        {
            var remote = Remote(); var library = Library; var client = Client;
            var confirmation = await Ask("Restore", new[] { "ReplaceConfirmation" }); if (confirmation == null || confirmation[0] != "RESTAURAR") return;
            await Task.Run(() => library.Restore(remote, client)); await RefreshLocal();
        }
        private async Task DeleteCloud()
        {
            var entry = Remote(); var confirmation = await Ask("DeleteCloud", new[] { "DeleteConfirmation" });
            if (confirmation == null || confirmation[0] != "ELIMINAR") return;
            await Task.Run(() => Client.Request<object>("DELETE", "library/" + Uri.EscapeDataString(entry.id), new { version = entry.version })); await CloudList();
        }
        private async Task DeleteLocal()
        {
            var entry = Local(); var confirmation = await Ask("DeleteLocal", new[] { "DeleteConfirmation" });
            if (confirmation == null || confirmation[0] != "ELIMINAR") return;
            Library.Delete(entry.id); await RefreshLocal();
        }
        private async void ShowPreview()
        {
            if (preview.Image != null) { preview.Image.Dispose(); preview.Image = null; }
            try
            {
                var entry = localList.SelectedItem as LibraryEntry; if (entry == null) return;
                var library = Library; string owner = CommunityRuntime.Session.user.id;
                var thumbnail = await Task.Run(() => library.Read(entry.id).thumbnail);
                if (IsDisposed || CommunityRuntime.Session?.user?.id != owner || (localList.SelectedItem as LibraryEntry)?.id != entry.id) return;
                byte[] bytes = Convert.FromBase64String(thumbnail);
                if (bytes.Length > 262144) throw new InvalidDataException("Thumbnail exceeds limit.");
                using (var stream = new MemoryStream(bytes))
                using (var image = Image.FromStream(stream))
                {
                    if (image.Width > 1024 || image.Height > 1024) throw new InvalidDataException("Thumbnail dimensions exceed limit.");
                    preview.Image = new Bitmap(image);
                }
            }
            catch (Exception error) { if (!IsDisposed) status.Text = error.Message; }
        }
        private string searchPath;
        private async Task Search(bool more)
        {
            CommunityRuntime.RequireSession();
            if (more && nextCursor == null) return;
            if (!more)
            {
                searchPath = (searchKind.SelectedIndex == 1 ? "designers" : "designs") + "?limit=30&q=" + Uri.EscapeDataString(search.Text.Trim()) + "&category=" + Uri.EscapeDataString(category.Text.Trim()) + "&designerId=" + Uri.EscapeDataString(designer.Text.Trim());
                nextCursor = null;
            }
            string path = searchPath + (more ? "&cursor=" + Uri.EscapeDataString(nextCursor) : "");
            if (searchPath.StartsWith("designers?", StringComparison.Ordinal))
            {
                var page = await Task.Run(() => Client.Request<CommunityPage<CommunityUser>>("GET", path));
                if (!more) marketList.Items.Clear(); foreach (var item in page.items) marketList.Items.Add(item); nextCursor = page.nextCursor;
            }
            else
            {
                var page = await Task.Run(() => Client.Request<CommunityPage<CommunityDesign>>("GET", path));
                if (!more) marketList.Items.Clear(); foreach (var item in page.items) marketList.Items.Add(item); nextCursor = page.nextCursor;
            }
        }
        private void ShowDetails()
        {
            if (marketPreview.Image != null) { marketPreview.Image.Dispose(); marketPreview.Image = null; }
            var item = marketList.SelectedItem as CommunityDesign;
            marketPreview.Visible = !string.IsNullOrWhiteSpace(item?.thumbnail);
            if (!string.IsNullOrWhiteSpace(item?.thumbnail))
            {
                try
                {
                    var bytes = Convert.FromBase64String(item.thumbnail);
                    if (bytes.Length > 262144) throw new InvalidDataException("Thumbnail exceeds limit.");
                    using (var stream = new MemoryStream(bytes))
                    using (var image = Image.FromStream(stream))
                    {
                        if (image.Width > 1024 || image.Height > 1024) throw new InvalidDataException("Thumbnail dimensions exceed limit.");
                        marketPreview.Image = new Bitmap(image);
                    }
                }
                catch (Exception error) { status.Text = error.Message; }
            }
            if (item != null) detail.Text = string.Join(Environment.NewLine, item.title, item.description, item.category + " · " + string.Join(", ", item.tags ?? new string[0]), CommunityText.Get("License"));
            else { var person = marketList.SelectedItem as CommunityUser; detail.Text = person == null ? "" : person.displayName + Environment.NewLine + person.bio; }
        }
        private async Task Details()
        {
            if (marketList.SelectedItem is CommunityUser)
            {
                var person = (CommunityUser)marketList.SelectedItem;
                var full = await Task.Run(() => Client.Request<CommunityUser>("GET", "designers/" + Uri.EscapeDataString(person.id)));
                detail.Text = full.displayName + Environment.NewLine + full.bio; return;
            }
            var selected = Design(); var item = await Task.Run(() => Client.Request<CommunityDesign>("GET", "designs/" + Uri.EscapeDataString(selected.id)));
            int index = marketList.SelectedIndex; marketList.Items[index] = item; marketList.SelectedIndex = index; ShowDetails();
        }
        private async Task DesignerCatalog()
        {
            var user = marketList.SelectedItem as CommunityUser; var item = marketList.SelectedItem as CommunityDesign;
            designer.Text = user?.id ?? item?.designer?.id ?? "";
            if (designer.Text.Length == 0) throw new InvalidOperationException(CommunityText.Get("SelectDesign"));
            searchKind.SelectedIndex = 0; search.Clear(); await Search(false);
        }
        private async Task Acquire()
        {
            var selected = Design();
            var agreement = await Ask("Acquire", new[] { "LicenseAgreement" }); if (agreement == null || agreement[0] != "ACEPTO") return;
            var client = Client; var library = Library;
            var result = await Task.Run(() => client.Request<CommunityContent>("POST", "acquisitions", new { designId = selected.id, licenseVersion = "physical-products-v1", accepted = true }, true, "acquire-" + selected.id));
            string signingKey = CommunityRuntime.SigningKey;
            await Task.Run(() => library.Acquire(result, signingKey)); await RefreshLocal(); pages.SelectedIndex = 0;
        }
        private async Task Publish()
        {
            EditingAllowed(); var session = CommunityRuntime.RequireSession();
            if (!session.user.emailVerified) throw new InvalidOperationException(CommunityText.Get("VerifyFirst"));
            var document = core.LayerController.Document?.Clone();
            if (document == null) throw new InvalidOperationException(CommunityText.Get("SelectDesign"));
            if (document.Layers.Any(l => l.ContentKind == LayerContentKind.Raster))
            {
                var selection = core.LayerController.Selection;
                var chosen = new CanvasClipboard();
                if (selection.Objects) chosen.CopyObjects(document, document.Layers.SelectMany(l => l.VectorItems.SelectMany(i => i.Paths.Where(p => selection.Paths.Contains(p.Id)).Select(p => new VectorSelection(l, i, p)))));
                else chosen.CopyLayers(document, document.Layers.Where(l => selection.Layers.Contains(l.Id) && l.ContentKind == LayerContentKind.Vector));
                var subset = new LaserDocument(); chosen.Paste(subset, false); document = subset;
            }
            string svg = CommunitySvg.Export(document);
            var values = await Ask("Publish", new[] { "Title", "Description", "Category", "Tags", "OwnershipAgreement" });
            if (values == null || values[4] != "SOY AUTOR") return;
            await Task.Run(() => Client.Request<CommunityDesign>("POST", "designs", new { title = values[0].Trim(), description = values[1], category = values[2].Trim(), tags = Tags(values[3]), svg, licenseVersion = "physical-products-v1", ownershipConfirmed = true, price = 0 }, true, Guid.NewGuid().ToString("N")));
            designer.Text = session.user.id; searchKind.SelectedIndex = 0; search.Clear(); await Search(false);
        }
        private async Task EditPublication()
        {
            var item = Design(); if (item.designer?.id != CommunityRuntime.RequireSession().user.id) throw new InvalidOperationException(CommunityText.Get("OwnerOnly"));
            var values = await Ask("EditPublication", new[] { "Title", "Description", "Category", "Tags" }, new[] { item.title, item.description, item.category, string.Join(", ", item.tags ?? new string[0]) }); if (values == null) return;
            await Task.Run(() => Client.Request<CommunityDesign>("PATCH", "designs/" + Uri.EscapeDataString(item.id), new { version = item.version, title = values[0], description = values[1], category = values[2], tags = Tags(values[3]) })); await Search(false);
        }
        private async Task Withdraw()
        {
            var item = Design(); if (item.designer?.id != CommunityRuntime.RequireSession().user.id) throw new InvalidOperationException(CommunityText.Get("OwnerOnly"));
            var values = await Ask("Withdraw", new[] { "DeleteConfirmation" }); if (values == null || values[0] != "ELIMINAR") return;
            await Task.Run(() => Client.Request<object>("DELETE", "designs/" + Uri.EscapeDataString(item.id), new { version = item.version })); await Search(false);
        }
        private async Task Report()
        {
            var item = Design(); var values = await Ask("Report", new[] { "Reason" }); if (values == null) return;
            await Task.Run(() => Client.Request<object>("POST", "reports", new { designId = item.id, reason = values[0] }, true, Guid.NewGuid().ToString("N")));
        }
        private async Task MyAcquisitions()
        {
            var client = Client; var library = Library;
            var entries = await Task.Run(() => AllPages<CommunityContent>(client, "acquisitions"));
            foreach (var entry in entries)
            {
                var content = await Task.Run(() => client.Request<CommunityContent>("POST", "acquisitions/" + Uri.EscapeDataString(entry.id) + "/content"));
                string signingKey = CommunityRuntime.SigningKey;
                await Task.Run(() => library.Acquire(content, signingKey));
            }
            await RefreshLocal(); pages.SelectedIndex = 0;
        }
        private void SetProfile(CommunityUser user)
        {
            profileName.Text = user.displayName; profileBio.Text = user.bio;
            profileEmail.Text = user.email + " · " + CommunityText.Get(user.emailVerified ? "Verified" : "Unverified");
        }
        private async Task RefreshProfile()
        {
            var user = await Task.Run(() => Client.Request<CommunityUser>("GET", "me"));
            var session = CommunityRuntime.RequireSession(); session.user = user; CommunityRuntime.SaveSession(session); SetProfile(user);
        }
        private async Task SaveProfile()
        {
            string name = profileName.Text.Trim(), bio = profileBio.Text;
            await Task.Run(() => Client.Request<CommunityUser>("PATCH", "me", new { displayName = name, bio })); await RefreshProfile();
        }
        private async Task VerifyEmail()
        {
            var values = await Ask("VerifyEmail", new[] { "Token" }); if (values == null) return;
            await Task.Run(() => Client.Request<object>("POST", "auth/email/verify", new { token = values[0].Trim() })); await RefreshProfile();
        }
        private async Task ResendVerification() { await Task.Run(() => Client.Request<object>("POST", "auth/email/resend")); }
    }
}
