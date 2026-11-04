using System;
using System.Drawing;
using System.Windows.Forms;

namespace LaserGRBL.Layering
{
    internal sealed class LayerColorPickerForm : Form
    {
        private readonly Panel preview = new Panel();
        private readonly Label rgb = new Label();
        private readonly Timer eyedropperTimer = new Timer();
        private readonly Bitmap samplePixel = new Bitmap(1, 1);
        private Color selectedColor;
        private Color colorBeforeEyedropper;
        private bool eyedropperActive;

        public Color SelectedColor { get { return selectedColor; } }

        private LayerColorPickerForm(Color initialColor)
        {
            selectedColor = Color.FromArgb(255, initialColor.R, initialColor.G, initialColor.B);
            Text = LayerText.Get("ChooseLayerColor");
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(370, 190);
            BackColor = ColorScheme.FormBackColor;
            ForeColor = ColorScheme.FormForeColor;

            preview.SetBounds(20, 20, 330, 55);
            preview.Name = "LayerColorPreview";
            preview.BorderStyle = BorderStyle.FixedSingle;
            rgb.SetBounds(20, 82, 330, 22);
            rgb.TextAlign = ContentAlignment.MiddleCenter;
            rgb.ForeColor = ForeColor;

            Button palette = new Button { Left = 20, Top = 112, Width = 155, Height = 30, Text = LayerText.Get("Palette") };
            Button eyedropper = new Button { Left = 195, Top = 112, Width = 155, Height = 30, Text = LayerText.Get("Eyedropper") };
            Button ok = new Button { Left = 195, Top = 152, Width = 75, Text = "OK", DialogResult = DialogResult.OK };
            Button cancel = new Button { Left = 275, Top = 152, Width = 75, Text = LayerText.Get("Cancel"), DialogResult = DialogResult.Cancel };
            palette.Click += ChooseFromPalette;
            eyedropper.Click += ChooseFromScreen;
            eyedropperTimer.Interval = 40;
            eyedropperTimer.Tick += UpdateEyedropperPreview;
            MouseDown += ConfirmEyedropper;
            KeyDown += CancelEyedropper;
            FormClosed += (s, e) => StopEyedropper(false);

            Controls.AddRange(new Control[] { preview, rgb, palette, eyedropper, ok, cancel });
            AcceptButton = ok;
            CancelButton = cancel;
            RefreshColor();
        }

        public static bool TryChoose(IWin32Window owner, Color initialColor, out Color color)
        {
            using (LayerColorPickerForm form = new LayerColorPickerForm(initialColor))
            {
                bool accepted = form.ShowDialog(owner) == DialogResult.OK;
                color = accepted ? form.SelectedColor : initialColor;
                return accepted;
            }
        }

        private void ChooseFromPalette(object sender, EventArgs e)
        {
            using (ColorDialog dialog = new ColorDialog { Color = selectedColor, FullOpen = true, AnyColor = true })
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    selectedColor = Color.FromArgb(255, dialog.Color.R, dialog.Color.G, dialog.Color.B);
                    RefreshColor();
                }
        }

        private void ChooseFromScreen(object sender, EventArgs e)
        {
            if (eyedropperActive) return;
            colorBeforeEyedropper = selectedColor;
            eyedropperActive = true;
            Cursor = Cursors.Cross;
            Capture = true;
            eyedropperTimer.Start();
            UpdateEyedropperPreview(null, EventArgs.Empty);
        }

        private void RefreshColor()
        {
            preview.BackColor = selectedColor;
            rgb.Text = string.Format("#{0:X2}{1:X2}{2:X2}    RGB ({0}, {1}, {2})", selectedColor.R, selectedColor.G, selectedColor.B);
        }

        private void UpdateEyedropperPreview(object sender, EventArgs e)
        {
            if (!eyedropperActive) return;
            try
            {
                Point screenPoint = Control.MousePosition;
                using (Graphics graphics = Graphics.FromImage(samplePixel))
                    graphics.CopyFromScreen(screenPoint, Point.Empty, new Size(1, 1), CopyPixelOperation.SourceCopy);
                Color pixel = samplePixel.GetPixel(0, 0);
                selectedColor = Color.FromArgb(255, pixel.R, pixel.G, pixel.B);
                RefreshColor();
            }
            catch { }
        }

        private void ConfirmEyedropper(object sender, MouseEventArgs e)
        {
            if (!eyedropperActive || e.Button != MouseButtons.Left) return;
            UpdateEyedropperPreview(null, EventArgs.Empty);
            StopEyedropper(true);
        }

        private void CancelEyedropper(object sender, KeyEventArgs e)
        {
            if (!eyedropperActive || e.KeyCode != Keys.Escape) return;
            StopEyedropper(false);
            e.Handled = true;
        }

        private void StopEyedropper(bool keepColor)
        {
            if (!eyedropperActive) return;
            eyedropperActive = false;
            eyedropperTimer.Stop();
            Capture = false;
            Cursor = Cursors.Default;
            if (!keepColor)
            {
                selectedColor = colorBeforeEyedropper;
                RefreshColor();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                eyedropperTimer.Dispose();
                samplePixel.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
