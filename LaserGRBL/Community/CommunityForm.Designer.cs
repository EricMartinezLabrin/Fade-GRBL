using System.Drawing;
using System.Windows.Forms;

namespace LaserGRBL.Community
{
    public partial class CommunityForm
    {
        private TabControl pages;
        private FlowLayoutPanel libraryActions, marketActions, profileActions;
        private ListBox localList, remoteList, marketList;
        private TextBox librarySearch, search, category, designer, profileName, profileBio;
        private Label status, quota, profileEmail;
        private PictureBox preview, marketPreview;
        private TextBox detail;
        private ComboBox searchKind, libraryKind;
        private FlowLayoutPanel header;
        private static FlowLayoutPanel Actions()
        {
            return new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(8), MinimumSize = new Size(0, 48) };
        }
        private static TextBox Field(string accessibleName, int width = 160)
        {
            return new TextBox { Width = width, AccessibleName = accessibleName, Margin = new Padding(4, 8, 4, 4) };
        }
        private static void Caption(FlowLayoutPanel panel, string key)
        {
            panel.Controls.Add(new Label { Text = CommunityText.Get(key), AutoSize = true, Margin = new Padding(8, 12, 4, 4) });
        }
        private void InitializeComponent()
        {
            Text = CommunityText.Get("Community"); Name = "CommunityForm";
            Font = UserControls.FadeTheme.BodyFont; AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(940, 650); MinimumSize = new Size(600, 420); StartPosition = FormStartPosition.CenterParent;
            header = Actions(); pages = new TabControl { Dock = DockStyle.Fill };
            status = new Label { Dock = DockStyle.Bottom, Height = 52, Padding = new Padding(12), AutoEllipsis = true, AccessibleName = CommunityText.Get("Status") };
            Controls.Add(pages); Controls.Add(header); Controls.Add(status);
            var library = new TabPage(CommunityText.Get("Library")) { AutoScroll = true, AutoScrollMinSize = new Size(0, 500) };
            var market = new TabPage(CommunityText.Get("Marketplace")) { AutoScroll = true, AutoScrollMinSize = new Size(0, 540) };
            var profile = new TabPage(CommunityText.Get("Profile")) { AutoScroll = true };
            pages.TabPages.AddRange(new[] { library, market, profile });
            libraryActions = Actions(); marketActions = Actions(); profileActions = Actions();
            localList = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, AccessibleName = CommunityText.Get("LocalDesigns") };
            remoteList = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, AccessibleName = CommunityText.Get("CloudDesigns") };
            librarySearch = Field(CommunityText.Get("Search"), 210);
            Caption(libraryActions, "Search"); libraryActions.Controls.Add(librarySearch);
            libraryKind = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 155, AccessibleName = CommunityText.Get("LibraryFilter"), Margin = new Padding(4, 8, 4, 4) };
            libraryKind.Items.AddRange(new[] { CommunityText.Get("AllDesigns"), CommunityText.Get("OwnDesigns"), CommunityText.Get("Acquired") }); libraryKind.SelectedIndex = 0; libraryActions.Controls.Add(libraryKind);
            var split = new SplitContainer { Dock = DockStyle.Fill, Size = new Size(900, 300), Orientation = Orientation.Vertical, SplitterDistance = 450 };
            split.Panel1.Controls.Add(localList); split.Panel1.Controls.Add(new Label { Text = CommunityText.Get("LocalDesigns"), Dock = DockStyle.Top, Height = 32 });
            split.Panel2.Controls.Add(remoteList); split.Panel2.Controls.Add(new Label { Text = CommunityText.Get("CloudDesigns"), Dock = DockStyle.Top, Height = 32 });
            quota = new Label { Text = CommunityText.Get("QuotaUnknown"), Dock = DockStyle.Bottom, Height = 36, Padding = new Padding(8) };
            preview = new PictureBox { Dock = DockStyle.Bottom, Height = 80, SizeMode = PictureBoxSizeMode.Zoom, AccessibleName = CommunityText.Get("Preview") };
            library.Controls.Add(split); library.Controls.Add(preview); library.Controls.Add(quota); library.Controls.Add(libraryActions);
            search = Field(CommunityText.Get("Search"), 170); category = Field(CommunityText.Get("Category"), 100); designer = Field(CommunityText.Get("DesignerId"), 120);
            searchKind = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130, AccessibleName = CommunityText.Get("SearchType"), Margin = new Padding(4, 8, 4, 4) };
            searchKind.Items.AddRange(new[] { CommunityText.Get("Designs"), CommunityText.Get("Designers") }); searchKind.SelectedIndex = 0;
            Caption(marketActions, "Search"); marketActions.Controls.Add(search); marketActions.Controls.Add(searchKind);
            Caption(marketActions, "Category"); marketActions.Controls.Add(category);
            marketList = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, AccessibleName = CommunityText.Get("Results") };
            detail = new TextBox { Dock = DockStyle.Bottom, Height = 130, Multiline = true, ScrollBars = ScrollBars.Vertical, ReadOnly = true, AccessibleName = CommunityText.Get("Details") };
            marketPreview = new PictureBox { Dock = DockStyle.Bottom, Height = 100, SizeMode = PictureBoxSizeMode.Zoom, AccessibleName = CommunityText.Get("Preview") };
            market.Controls.Add(marketList); market.Controls.Add(marketPreview); market.Controls.Add(detail); market.Controls.Add(marketActions);
            profileName = Field(CommunityText.Get("DisplayName"), 260); profileBio = Field(CommunityText.Get("Bio"), 420); profileBio.Multiline = true; profileBio.Height = 130;
            profileEmail = new Label { AutoSize = true, Margin = new Padding(8) };
            var fields = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(16) };
            fields.Controls.Add(profileEmail); Caption(fields, "DisplayName"); fields.Controls.Add(profileName); Caption(fields, "Bio"); fields.Controls.Add(profileBio);
            profile.Controls.Add(fields); profile.Controls.Add(profileActions);
        }
    }
}
