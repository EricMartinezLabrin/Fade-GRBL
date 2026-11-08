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
            {
                if (stream.Length < 4) return false;
                int first = stream.ReadByte(), second = stream.ReadByte();
                return (first == 0x1f && second == 0x8b) || (first == 'F' && second == 'M' && stream.ReadByte() == 'P' && stream.ReadByte() == '1');
            }
        }

        public static void Save(string filename, LaserDocument document)
        {
            Community.CommunitySecurity.AtomicWrite(filename, ToBytes(document));
            document.IsDirty = false;
        }

        public static byte[] ToBytes(LaserDocument document)
        {
            Community.CommunityRuntime.ValidateAccess(document);
            byte[] bytes;
            using (var memory = new MemoryStream())
            {
                using (var gzip = new GZipStream(memory, CompressionMode.Compress, true))
                    Serializer.Serialize(gzip, LaserProjectDto.FromModel(document));
                bytes = memory.ToArray();
            }
            return Community.CommunityRuntime.IsProtected(document) ? Community.CommunitySecurity.Seal(bytes, Community.CommunityRuntime.RequireSession().libraryKey) : bytes;
        }

        public static LaserDocument Load(string filename)
        {
            if (new FileInfo(filename).Length > 100000000) throw new InvalidDataException("Project exceeds 100 MB limit.");
            return FromBytes(File.ReadAllBytes(filename));
        }

        public static LaserDocument FromBytes(byte[] bytes)
        {
            bool protectedFile = bytes.Length >= 4 && System.Text.Encoding.ASCII.GetString(bytes, 0, 4) == "FMP1";
            if (protectedFile) bytes = Community.CommunitySecurity.Open(bytes, Community.CommunityRuntime.RequireSession().libraryKey);
            using (var file = new MemoryStream(bytes, false))
            using (GZipStream gzip = new GZipStream(file, CompressionMode.Decompress))
            using (var xml = System.Xml.XmlReader.Create(gzip, new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 100000000 }))
            {
                LaserProjectDto dto = (LaserProjectDto)Serializer.Deserialize(xml);
                if (dto == null || (dto.Version != 2 && dto.Version != 3 && dto.Version != 4)) throw new InvalidDataException("Unsupported LaserGRBL project version.");
                LaserDocument document = dto.ToModel();
                if (!protectedFile && Community.CommunityRuntime.IsProtected(document)) throw new InvalidDataException("Acquired designs require a protected project.");
                Community.CommunityRuntime.ValidateAccess(document);
                if (dto.Version < 4) foreach(var layer in document.Layers) { if(layer.ScaleX==0)layer.ScaleX=1; if(layer.ScaleY==0)layer.ScaleY=1; }
                if(document.Layers.Any(l=>!AffineTransform.Layer(l).Valid || l.VectorItems.SelectMany(i=>i.Paths).Any(p=>p.Pose==null||!p.Pose.Valid))) throw new InvalidDataException("Invalid project transform.");
                foreach(var item in document.Layers.SelectMany(l=>l.VectorItems).Where(i=>i.Text!=null&&!i.Text.HasAffinePose))
                {
                    var b=CanvasObjects.Bounds(item.Paths);
                    var pose=new AffineTransform { A=item.Text.ReferenceWidth>0?b.Width/item.Text.ReferenceWidth:1,
                        D=item.Text.ReferenceHeight>0?b.Height/item.Text.ReferenceHeight:1,X=b.Left,Y=b.Top };
                    foreach(var path in item.Paths)path.Pose=pose.Clone();item.Text.HasAffinePose=true;
                }
                document.Version = 4;
                CanvasGroups.MergeColors(document);
                document.NormalizeOrder();
                document.IsDirty = false;
                return document;
            }
        }

        [XmlRoot("LaserProject")]
        public class LaserProjectDto
        {
            [XmlAttribute("version")]
            public int Version { get; set; } = 4;
            public List<CanvasGroup> Groups { get; set; } = new List<CanvasGroup>();
            public JobPassOrder PassOrder { get; set; } = JobPassOrder.ByLayer;
            public List<LayerDto> Layers { get; set; } = new List<LayerDto>();

            public static LaserProjectDto FromModel(LaserDocument document)
            {
                return new LaserProjectDto { Version = 4, Groups = document.Groups.Select(g => g.Clone()).ToList(), PassOrder = document.PassOrder, Layers = document.Layers.Select(LayerDto.FromModel).ToList() };
            }

            public LaserDocument ToModel()
            {
                return new LaserDocument { Version = Version, Groups = Groups ?? new List<CanvasGroup>(), PassOrder = PassOrder, Layers = (Layers ?? new List<LayerDto>()).Select(layer => layer.ToModel()).ToList() };
            }
        }

        public class LayerDto
        {
            public Guid Id { get; set; }
            public Guid GroupId { get; set; }
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
            public double CrossX { get; set; }
            public double CrossY { get; set; }
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
                    Id = layer.Id, GroupId = layer.GroupId, Order = layer.Order, Name = layer.Name, ColorArgb = layer.ColorArgb,
                    Visible = layer.Visible, OutputEnabled = layer.OutputEnabled, Mode = layer.Mode, ContentKind = layer.ContentKind,
                    SpeedMmPerMinute = layer.SpeedMmPerMinute, PowerPercent = layer.PowerPercent, Passes = layer.Passes,
                    CrossX = layer.CrossX, CrossY = layer.CrossY, ScaleX = layer.ScaleX, ScaleY = layer.ScaleY, TranslateX = layer.TranslateX, TranslateY = layer.TranslateY,
                    Fill = FillDto.FromModel(layer.FillSettings),
                    VectorItems = layer.VectorItems.Select(VectorItemDto.FromModel).ToList(),
                    Raster = RasterDto.FromModel(layer.RasterItem)
                };
            }

            public LaserLayer ToModel()
            {
                return new LaserLayer
                {
                    Id = Id == Guid.Empty ? Guid.NewGuid() : Id, GroupId = GroupId, Order = Order, Name = Name, ColorArgb = ColorArgb,
                    Visible = Visible, OutputEnabled = OutputEnabled, Mode = Mode, ContentKind = ContentKind,
                    SpeedMmPerMinute = SpeedMmPerMinute, PowerPercent = PowerPercent, Passes = Passes,
                    CrossX = CrossX, CrossY = CrossY, ScaleX = ScaleX, ScaleY = ScaleY, TranslateX = TranslateX, TranslateY = TranslateY,
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
            public Guid Id { get; set; }
            public TextObject Text { get; set; }
            public string SourceName { get; set; }
            public List<VectorPathDto> Paths { get; set; } = new List<VectorPathDto>();
            public static VectorItemDto FromModel(VectorItem item) { return new VectorItemDto { Id = item.Id, Text = item.Text, SourceName = item.SourceName, Paths = item.Paths.Select(VectorPathDto.FromModel).ToList() }; }
            public VectorItem ToModel() { return new VectorItem { Id = Id == Guid.Empty ? Guid.NewGuid() : Id, Text = Text, SourceName = SourceName, Paths = (Paths ?? new List<VectorPathDto>()).Select(path => path.ToModel()).ToList() }; }
        }

        public class VectorPathDto
        {
            public Community.CommunityReceipt Acquisition { get; set; }
            public Guid Id { get; set; }
            public Guid GroupId { get; set; }
            public bool Closed { get; set; }
            public AffineTransform Pose { get; set; } = new AffineTransform();
            public List<PointDto> Points { get; set; } = new List<PointDto>();
            public static VectorPathDto FromModel(VectorPath path) { return new VectorPathDto { Acquisition = path.Acquisition, Id = path.Id, GroupId = path.GroupId, Closed = path.Closed, Pose = path.Pose.Clone(), Points = path.Points.Select(point => new PointDto { X = point.X, Y = point.Y }).ToList() }; }
            public VectorPath ToModel() { return new VectorPath { Acquisition = Acquisition, Id = Id == Guid.Empty ? Guid.NewGuid() : Id, GroupId = GroupId, Closed = Closed, Pose = Pose ?? new AffineTransform(), Points = (Points ?? new List<PointDto>()).Select(point => new LaserPoint(point.X, point.Y)).ToList() }; }
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
