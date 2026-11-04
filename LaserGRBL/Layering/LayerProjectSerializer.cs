using System;
using System.IO;
using System.IO.Compression;
using System.Xml.Serialization;
using System.Collections.Generic;
using System.Linq;

namespace LaserGRBL.Layering
{
    public static class LayerProjectSerializer
    {
        private static readonly XmlSerializer Serializer = new XmlSerializer(typeof(LaserProjectDto));

        public static bool IsVersion2(string filename)
        {
            if (!File.Exists(filename)) return false;
            using (FileStream stream = File.OpenRead(filename))
                return stream.Length >= 2 && stream.ReadByte() == 0x1f && stream.ReadByte() == 0x8b;
        }

        public static void Save(string filename, LaserDocument document)
        {
            string temporary = filename + ".tmp";
            try
            {
                using (FileStream file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                using (GZipStream gzip = new GZipStream(file, CompressionMode.Compress))
                    Serializer.Serialize(gzip, LaserProjectDto.FromModel(document));
                if (File.Exists(filename)) File.Delete(filename);
                File.Move(temporary, filename);
                document.IsDirty = false;
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        public static LaserDocument Load(string filename)
        {
            using (FileStream file = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (GZipStream gzip = new GZipStream(file, CompressionMode.Decompress))
            {
                LaserProjectDto dto = (LaserProjectDto)Serializer.Deserialize(gzip);
                if (dto == null || dto.Version != 2) throw new InvalidDataException("Unsupported LaserGRBL project version.");
                LaserDocument document = dto.ToModel();
                document.NormalizeOrder();
                document.IsDirty = false;
                return document;
            }
        }

        [XmlRoot("LaserProject")]
        public class LaserProjectDto
        {
            [XmlAttribute("version")]
            public int Version { get; set; } = 2;
            public List<LayerDto> Layers { get; set; } = new List<LayerDto>();

            public static LaserProjectDto FromModel(LaserDocument document)
            {
                return new LaserProjectDto { Version = 2, Layers = document.Layers.Select(LayerDto.FromModel).ToList() };
            }

            public LaserDocument ToModel()
            {
                return new LaserDocument { Version = Version, Layers = (Layers ?? new List<LayerDto>()).Select(layer => layer.ToModel()).ToList() };
            }
        }

        public class LayerDto
        {
            public Guid Id { get; set; }
            public int Order { get; set; }
            public string Name { get; set; }
            public int ColorArgb { get; set; }
            public bool Visible { get; set; }
            public bool OutputEnabled { get; set; }
            public LayerMode Mode { get; set; }
            public LayerContentKind ContentKind { get; set; }
            public int SpeedMmPerMinute { get; set; }
            public double PowerPercent { get; set; }
            public int Passes { get; set; }
            public double ScaleX { get; set; } = 1;
            public double ScaleY { get; set; } = 1;
            public double TranslateX { get; set; }
            public double TranslateY { get; set; }
            public FillDto Fill { get; set; }
            public List<VectorItemDto> VectorItems { get; set; } = new List<VectorItemDto>();
            public RasterDto Raster { get; set; }

            public static LayerDto FromModel(LaserLayer layer)
            {
                return new LayerDto
                {
                    Id = layer.Id, Order = layer.Order, Name = layer.Name, ColorArgb = layer.ColorArgb,
                    Visible = layer.Visible, OutputEnabled = layer.OutputEnabled, Mode = layer.Mode, ContentKind = layer.ContentKind,
                    SpeedMmPerMinute = layer.SpeedMmPerMinute, PowerPercent = layer.PowerPercent, Passes = layer.Passes,
                    ScaleX = layer.ScaleX, ScaleY = layer.ScaleY, TranslateX = layer.TranslateX, TranslateY = layer.TranslateY,
                    Fill = FillDto.FromModel(layer.FillSettings),
                    VectorItems = layer.VectorItems.Select(VectorItemDto.FromModel).ToList(),
                    Raster = RasterDto.FromModel(layer.RasterItem)
                };
            }

            public LaserLayer ToModel()
            {
                return new LaserLayer
                {
                    Id = Id == Guid.Empty ? Guid.NewGuid() : Id, Order = Order, Name = Name, ColorArgb = ColorArgb,
                    Visible = Visible, OutputEnabled = OutputEnabled, Mode = Mode, ContentKind = ContentKind,
                    SpeedMmPerMinute = SpeedMmPerMinute, PowerPercent = PowerPercent, Passes = Passes,
                    ScaleX = ScaleX == 0 ? 1 : ScaleX, ScaleY = ScaleY == 0 ? 1 : ScaleY, TranslateX = TranslateX, TranslateY = TranslateY,
                    FillSettings = Fill == null ? new LayerFillSettings() : Fill.ToModel(),
                    VectorItems = (VectorItems ?? new List<VectorItemDto>()).Select(item => item.ToModel()).ToList(),
                    RasterItem = Raster == null ? null : Raster.ToModel()
                };
            }
        }

        public class FillDto
        {
            public double IntervalMm { get; set; }
            public double AngleDegrees { get; set; }
            public bool Bidirectional { get; set; }
            public bool Optimize { get; set; }
            public bool OutsideToInside { get; set; }
            public static FillDto FromModel(LayerFillSettings value) { value = value ?? new LayerFillSettings(); return new FillDto { IntervalMm = value.IntervalMm, AngleDegrees = value.AngleDegrees, Bidirectional = value.Bidirectional, Optimize = value.Optimize, OutsideToInside = value.OutsideToInside }; }
            public LayerFillSettings ToModel() { return new LayerFillSettings { IntervalMm = IntervalMm, AngleDegrees = AngleDegrees, Bidirectional = Bidirectional, Optimize = Optimize, OutsideToInside = OutsideToInside }; }
        }

        public class VectorItemDto
        {
            public string SourceName { get; set; }
            public List<VectorPathDto> Paths { get; set; } = new List<VectorPathDto>();
            public static VectorItemDto FromModel(VectorItem item) { return new VectorItemDto { SourceName = item.SourceName, Paths = item.Paths.Select(VectorPathDto.FromModel).ToList() }; }
            public VectorItem ToModel() { return new VectorItem { SourceName = SourceName, Paths = (Paths ?? new List<VectorPathDto>()).Select(path => path.ToModel()).ToList() }; }
        }

        public class VectorPathDto
        {
            public bool Closed { get; set; }
            public List<PointDto> Points { get; set; } = new List<PointDto>();
            public static VectorPathDto FromModel(VectorPath path) { return new VectorPathDto { Closed = path.Closed, Points = path.Points.Select(point => new PointDto { X = point.X, Y = point.Y }).ToList() }; }
            public VectorPath ToModel() { return new VectorPath { Closed = Closed, Points = (Points ?? new List<PointDto>()).Select(point => new LaserPoint(point.X, point.Y)).ToList() }; }
        }

        public class PointDto { public double X { get; set; } public double Y { get; set; } }

        public class RasterDto
        {
            public string SourceName { get; set; }
            public byte[] ImageData { get; set; }
            public List<string> BaseCommands { get; set; } = new List<string>();
            public int OriginalMaxPower { get; set; }
            public int OriginalMinPower { get; set; }
            public double MinPowerPercent { get; set; }
            public List<RasterSettingDto> Settings { get; set; } = new List<RasterSettingDto>();
            public static RasterDto FromModel(RasterItem item) { return item == null ? null : new RasterDto { SourceName = item.SourceName, ImageData = item.ImageData, BaseCommands = new List<string>(item.BaseCommands), OriginalMaxPower = item.OriginalMaxPower, OriginalMinPower = item.OriginalMinPower, MinPowerPercent = item.MinPowerPercent, Settings = item.Settings.Select(RasterSettingDto.FromModel).ToList() }; }
            public RasterItem ToModel() { return new RasterItem { SourceName = SourceName, ImageData = ImageData, BaseCommands = BaseCommands ?? new List<string>(), OriginalMaxPower = OriginalMaxPower, OriginalMinPower = OriginalMinPower, MinPowerPercent = MinPowerPercent, Settings = (Settings ?? new List<RasterSettingDto>()).Select(setting => setting.ToModel()).ToList() }; }
        }

        public class RasterSettingDto
        {
            public string Key { get; set; }
            public string TypeName { get; set; }
            public string Value { get; set; }
            public static RasterSettingDto FromModel(RasterSetting setting) { return new RasterSettingDto { Key = setting.Key, TypeName = setting.TypeName, Value = setting.Value }; }
            public RasterSetting ToModel() { return new RasterSetting { Key = Key, TypeName = TypeName, Value = Value }; }
        }
    }
}
