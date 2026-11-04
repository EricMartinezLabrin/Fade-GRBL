using System.Drawing;

namespace LaserGRBL
{
    public sealed class FadeColorScheme : IColorScheme
    {
        public FadeColorScheme(bool dark) { IsDark = dark; }
        public bool IsDark { get; private set; }
        public ColorScheme.Scheme Scheme => IsDark ? ColorScheme.Scheme.FadeDark : ColorScheme.Scheme.FadeLight;
        public Color FormBackColor => IsDark ? Color.FromArgb(41, 46, 54) : Color.White;
        public Color FormForeColor => IsDark ? Color.FromArgb(246, 247, 249) : Color.FromArgb(57, 62, 70);
        public Color PreviewBackColor => IsDark ? Color.FromArgb(24, 28, 34) : Color.White;
        public Color PreviewText => IsDark ? Color.FromArgb(188, 195, 205) : Color.FromArgb(98, 106, 118);
        public Color PreviewRuler => PreviewText;
        public Color PreviewGrid => IsDark ? Color.FromArgb(48, 55, 65) : Color.FromArgb(214, 218, 225);
        public Color PreviewGridMinor => IsDark ? Color.FromArgb(36, 43, 52) : Color.FromArgb(237, 240, 244);
        public Color PreviewJobRange => PreviewText;
        public Color PreviewFirstMovement => PreviewText;
        public Color PreviewOtherMovement => Color.FromArgb(124, 132, 145);
        public Color PreviewLaserPower => FormForeColor;
        public Color PreviewCross => Color.FromArgb(246, 180, 42);
        public Color PreviewCommandOK => IsDark ? Color.FromArgb(108, 233, 166) : Color.FromArgb(24, 121, 78);
        public Color PreviewCommandKO => IsDark ? Color.FromArgb(249, 112, 102) : Color.FromArgb(180, 35, 24);
        public Color PreviewCommandWait => Color.FromArgb(124, 132, 145);
        public Color PreviewCrossCursor => PreviewText;
        public Color LogBackColor => FormBackColor;
        public Color LogLeftCOMMAND => FormForeColor;
        public Color LogLeftSTARTUP => PreviewCommandOK;
        public Color LogLeftALARM => PreviewCommandKO;
        public Color LogLeftCONFIG => PreviewText;
        public Color LogLeftFEEDBACK => LinkColor;
        public Color LogLeftPOSITION => PreviewText;
        public Color LogLeftOTHERS => PreviewText;
        public Color LogRightGOOD => PreviewCommandOK;
        public Color LogRightBAD => PreviewCommandKO;
        public Color LogRightOTHERS => FormForeColor;
        public Color TextBoxColorOverride => FormForeColor;
        public Color LinkColor => IsDark ? Color.FromArgb(132, 173, 255) : Color.FromArgb(23, 92, 211);
        public Color VisitedLinkColor => LinkColor;
        public Color ControlsBorder => Color.FromArgb(124, 132, 145);
        public Color ControlsBackDisabled => IsDark ? Color.FromArgb(50, 56, 66) : Color.FromArgb(232, 235, 239);
        public Color DisabledButtons => PreviewText;
        public Color PressedButtons => Color.FromArgb(212, 148, 18);
    }
}
