using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using LaserGRBL;
using LaserGRBL.Layering;
using LaserGRBL.UserControls;
using Xunit;

namespace LaserGRBL.Tests.Layering
{
    [Collection("Job execution")]
    public class FadeDesignTests
    {
        private static IEnumerable<Control> Descendants(Control parent)
        {
            foreach (Control child in parent.Controls)
            { yield return child; foreach (Control nested in Descendants(child)) yield return nested; }
        }
        private static void Sta(Action action)
        {
            Exception error = null;
            var thread = new Thread(() => { try { action(); } catch (Exception e) { error = e; } });
            thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
            if (error != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
        }

        [Theory]
        [InlineData(ColorScheme.Scheme.FadeLight)]
        [InlineData(ColorScheme.Scheme.FadeDark)]
        public void Theme_ControlsAndColorPreview_PreserveValuesAndUseReadableSelection(ColorScheme.Scheme scheme)
        {
            Sta(() =>
            {
                var previous = ColorScheme.CurrentScheme;
                try
                {
                    ColorScheme.CurrentScheme = scheme;
                    using (var form = new Form())
                    {
                        var field = new TextBox { Text = "1234" };
                        var swatch = new Panel { Name = "LayerColorPreview", BackColor = Color.Magenta };
                        var grid = new DataGridView(); grid.Columns.Add("Name", "Name"); grid.Rows.Add("Layer");
                        var button = new Button { Text = "Iniciar" }; int clicks = 0; button.Click += (s, e) => clicks++;
                        form.Controls.AddRange(new Control[] { field, swatch, grid, button });
                        FadeTheme.Apply(form); FadeTheme.SetButtonRole(button, FadeButtonRole.Primary);
                        Assert.Equal("1234", field.Text); Assert.Equal(Color.Magenta, swatch.BackColor);
                        Assert.Equal(ColorScheme.BrandAmber, button.BackColor); Assert.Equal(ColorScheme.BrandGraphite, button.ForeColor);
                        Assert.Equal(ColorScheme.SelectionBackground, grid.DefaultCellStyle.SelectionBackColor);
                        Assert.Equal(ColorScheme.FormForeColor, grid.DefaultCellStyle.SelectionForeColor);
                        Assert.False(grid.EnableHeadersVisualStyles); Assert.Equal(0, clicks);
                        Assert.Equal(FlatStyle.Flat, button.FlatStyle);
                        button.Enabled = false;
                        Assert.Equal(ColorScheme.ControlsBackDisabled, button.BackColor);
                        Assert.Equal(ColorScheme.DisabledButtons, button.ForeColor);
                        button.Enabled = true; Assert.Equal(ColorScheme.BrandAmber, button.BackColor);
                    }
                }
                finally { ColorScheme.CurrentScheme = previous; }
            });
        }

        [Theory]
        [InlineData(ColorScheme.Scheme.FadeLight)]
        [InlineData(ColorScheme.Scheme.FadeDark)]
        public void JobPanel_ReparentedAtWindowBottom_KeepsStopAndPauseVisible(ColorScheme.Scheme scheme)
        {
            Sta(() =>
            {
                var previous = ColorScheme.CurrentScheme;
                try
                {
                    ColorScheme.CurrentScheme = scheme;
                    Thread.CurrentThread.CurrentUICulture = new System.Globalization.CultureInfo("es-MX");
                    using (var form = new Form { ClientSize = new Size(1024, 720) })
                    using (var connection = new ConnectLogForm())
                    {
                        var bottom = connection.CreateDesignJobPanel();
                        form.Controls.Add(connection); connection.Dock = DockStyle.Fill;
                        form.Controls.Add(bottom); bottom.Dock = DockStyle.Bottom;
                        FadeTheme.Apply(form); form.Show(); form.PerformLayout(); bottom.PerformLayout();
                        Assert.Same(form, bottom.Parent);
                        var table = bottom.Controls.OfType<TableLayoutPanel>().Single();
                        var buttons = Descendants(table).OfType<Button>().ToList();
                        Assert.Contains(buttons, b => b.Text == "Iniciar"); Assert.Contains(buttons, b => b.Text == "Detener");
                        Assert.All(buttons, b => Assert.True(b.Right <= b.Parent.ClientSize.Width));
                        var stop = buttons.Single(b => b.Text == "Detener"); Assert.True(stop.Width >= 80);
                        string directory = Environment.GetEnvironmentVariable("FADE_DESIGN_SNAPSHOTS");
                        if (!string.IsNullOrEmpty(directory))
                        {
                            Directory.CreateDirectory(directory);
                            using (var bitmap = new Bitmap(form.Width, form.Height))
                            { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size)); bitmap.Save(Path.Combine(directory, scheme + "-controls.png")); }
                        }
                    }
                }
                finally { ColorScheme.CurrentScheme = previous; }
            });
        }

    }
}
