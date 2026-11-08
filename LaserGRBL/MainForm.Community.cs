using System;
using System.Windows.Forms;

namespace LaserGRBL
{
    public partial class MainForm
    {
        private Community.CommunityForm communityWindow;
        private void InitializeCommunityMenu()
        {
            var menu = new ToolStripMenuItem(Community.CommunityText.Get("Community"));
            string[] labels = { "Library", "Marketplace", "Profile" };
            for (int index = 0; index < labels.Length; index++)
            {
                int page = index;
                var item = new ToolStripMenuItem(Community.CommunityText.Get(labels[index]));
                item.Click += (s, e) =>
                {
                    if (communityWindow == null || communityWindow.IsDisposed) communityWindow = new Community.CommunityForm(Core);
                    if (!communityWindow.Visible) communityWindow.Show(this);
                    communityWindow.Activate(); communityWindow.OpenPage(page);
                };
                menu.DropDownItems.Add(item);
            }
            MMn.Items.Insert(Math.Max(0, MMn.Items.Count - 1), menu);
        }
    }
}
