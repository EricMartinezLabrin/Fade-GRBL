using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Xml.Serialization;
using System.Globalization;

namespace LaserGRBL.Layering
{
    public enum LayerMode { Line, Fill, OffsetFill, Image }
    public enum LayerContentKind { Vector, Raster }

    [Serializable]
    public class LaserPoint
    {
        public double X { get; set; }
        public double Y { get; set; }

        public LaserPoint() { }
        public LaserPoint(double x, double y) { X = x; Y = y; }
        public LaserPoint Clone() { return new LaserPoint(X, Y); }
    }

    [Serializable]
    public class VectorPath
    {
        public List<LaserPoint> Points { get; set; } = new List<LaserPoint>();
        public bool Closed { get; set; }

        public VectorPath Clone()
        {
            return new VectorPath { Closed = Closed, Points = Points.Select(p => p.Clone()).ToList() };
        }
    }

    [Serializable]
    public class VectorItem
    {
        public string SourceName { get; set; }
        public List<VectorPath> Paths { get; set; } = new List<VectorPath>();

        public VectorItem Clone()
        {
            return new VectorItem { SourceName = SourceName, Paths = Paths.Select(p => p.Clone()).ToList() };
        }
    }

    [Serializable]
    public class RasterSetting
    {
        public string Key { get; set; }
        public string TypeName { get; set; }
        public string Value { get; set; }

        public static RasterSetting From(string key, object value)
        {
            if (value == null) return null;
            Type type = value.GetType();
            return new RasterSetting
            {
                Key = key,
                TypeName = type.AssemblyQualifiedName,
                Value = type.IsEnum ? value.ToString() : Convert.ToString(value, CultureInfo.InvariantCulture)
            };
        }

        public object ToValue()
        {
            Type type = Type.GetType(TypeName, false);
            bool scalar = type == typeof(string) || type == typeof(bool) || type == typeof(byte) || type == typeof(short) || type == typeof(int) || type == typeof(long) || type == typeof(float) || type == typeof(double) || type == typeof(decimal);
            bool allowedEnum = type != null && type.IsEnum && (type.Assembly == typeof(LaserDocument).Assembly || type.Assembly == typeof(Color).Assembly);
            if (type == null || (!scalar && !allowedEnum)) throw new InvalidOperationException("Unsupported raster setting type: " + TypeName);
            if (type.IsEnum) return Enum.Parse(type, Value, true);
            return Convert.ChangeType(Value, type, CultureInfo.InvariantCulture);
        }
    }

    [Serializable]
    public class RasterItem
    {
        public string SourceName { get; set; }
        public byte[] ImageData { get; set; }
        public List<string> BaseCommands { get; set; } = new List<string>();
        public int OriginalMaxPower { get; set; } = 1000;
        public int OriginalMinPower { get; set; }
        public double MinPowerPercent { get; set; }
        public List<RasterSetting> Settings { get; set; } = new List<RasterSetting>();

        public RasterItem Clone()
        {
            return new RasterItem
            {
                SourceName = SourceName,
                ImageData = ImageData == null ? null : (byte[])ImageData.Clone(),
                BaseCommands = new List<string>(BaseCommands),
                OriginalMaxPower = OriginalMaxPower,
                OriginalMinPower = OriginalMinPower,
                MinPowerPercent = MinPowerPercent,
                Settings = Settings.Select(setting => new RasterSetting { Key = setting.Key, TypeName = setting.TypeName, Value = setting.Value }).ToList()
            };
        }
    }

    [Serializable]
    public class LayerFillSettings
    {
        public double IntervalMm { get; set; } = 0.10;
        public double AngleDegrees { get; set; }
        public bool Bidirectional { get; set; } = true;
        public bool Optimize { get; set; } = true;
        public bool OutsideToInside { get; set; } = true;

        public LayerFillSettings Clone()
        {
            return (LayerFillSettings)MemberwiseClone();
        }
    }

    [Serializable]
    public class LaserLayer
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public int Order { get; set; }
        public string Name { get; set; } = "Layer";
        public int ColorArgb { get; set; } = Color.Red.ToArgb();
        public bool Visible { get; set; } = true;
        public bool OutputEnabled { get; set; } = true;
        public LayerMode Mode { get; set; } = LayerMode.Line;
        public LayerContentKind ContentKind { get; set; } = LayerContentKind.Vector;
        public int SpeedMmPerMinute { get; set; } = 1000;
        public double PowerPercent { get; set; } = 100;
        public int Passes { get; set; } = 1;
        public double ScaleX { get; set; } = 1;
        public double ScaleY { get; set; } = 1;
        public double TranslateX { get; set; }
        public double TranslateY { get; set; }
        public LayerFillSettings FillSettings { get; set; } = new LayerFillSettings();
        public List<VectorItem> VectorItems { get; set; } = new List<VectorItem>();
        public RasterItem RasterItem { get; set; }
        [NonSerialized, XmlIgnore] public string StatusMessage;
        [NonSerialized, XmlIgnore] public bool HasError;

        [XmlIgnore]
        public Color Color { get { return Color.FromArgb(ColorArgb); } set { ColorArgb = value.ToArgb(); } }
        [XmlIgnore]
        public bool HasContent { get { return ContentKind == LayerContentKind.Raster ? RasterItem != null : VectorItems.Any(i => i.Paths.Count > 0); } }

        public LaserLayer Clone(bool includeContent, bool preserveId = false)
        {
            return new LaserLayer
            {
                Id = preserveId ? Id : Guid.NewGuid(), Order = Order, Name = Name, ColorArgb = ColorArgb,
                Visible = Visible, OutputEnabled = OutputEnabled, Mode = Mode, ContentKind = ContentKind,
                SpeedMmPerMinute = SpeedMmPerMinute, PowerPercent = PowerPercent, Passes = Passes,
                ScaleX = ScaleX, ScaleY = ScaleY, TranslateX = TranslateX, TranslateY = TranslateY,
                FillSettings = FillSettings.Clone(),
                VectorItems = includeContent ? VectorItems.Select(i => i.Clone()).ToList() : new List<VectorItem>(),
                RasterItem = includeContent && RasterItem != null ? RasterItem.Clone() : null
            };
        }

        public string Validate()
        {
            if (SpeedMmPerMinute < 1 || SpeedMmPerMinute > 100000) return "Speed must be between 1 and 100000 mm/min.";
            if (PowerPercent < 0 || PowerPercent > 100) return "Power must be between 0 and 100%.";
            if (Passes < 1 || Passes > 99) return "Passes must be between 1 and 99.";
            if (FillSettings == null || FillSettings.IntervalMm < 0.01 || FillSettings.IntervalMm > 10) return "Fill interval must be between 0.01 and 10 mm.";
            if (ContentKind == LayerContentKind.Raster && Mode != LayerMode.Image) return "Raster layers only support Image mode.";
            if (ContentKind == LayerContentKind.Vector && Mode == LayerMode.Image) return "Vector layers do not support Image mode.";
            return null;
        }
    }

    [Serializable]
    [XmlRoot("LaserProject")]
    public class LaserDocument
    {
        [XmlAttribute("version")]
        public int Version { get; set; } = 2;
        public List<LaserLayer> Layers { get; set; } = new List<LaserLayer>();
        [NonSerialized, XmlIgnore] public bool IsDirty;

        public void NormalizeOrder()
        {
            for (int i = 0; i < Layers.Count; i++) Layers[i].Order = i;
        }

        public LaserLayer FindByColor(Color color, LayerContentKind kind)
        {
            return Layers.FirstOrDefault(l => l.ContentKind == kind && Color.FromArgb(l.ColorArgb).ToArgb() == color.ToArgb());
        }

        public LaserDocument Clone()
        {
            return new LaserDocument
            {
                Version = Version,
                Layers = Layers.Select(layer => layer.Clone(true, true)).ToList(),
                IsDirty = IsDirty
            };
        }
    }
}
