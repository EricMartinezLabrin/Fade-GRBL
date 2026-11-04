using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Drawing;

namespace LaserGRBL.UserControls
{
    public interface IGrblPanel
    {
        void SetCore(GrblCore core);
        void TimerUpdate();
        void OnColorChange();
        PointF MachineToClient(PointF point);
        PointF ClientToMachine(PointF point);
        void SetSelectionBounds(RectangleF? bounds);
        void SetSelectionContours(IEnumerable<Layering.VectorPath> paths);
        void SetLayerEditing(bool enabled);

    }
}
