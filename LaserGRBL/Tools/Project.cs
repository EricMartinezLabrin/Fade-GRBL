using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System.Runtime.Serialization;
using System.Linq;
using LaserGRBL;
using System.Reflection;

namespace Tools
{
    public static class Project
    {
        #region Private fields

        /// <summary>
        /// Formatter
        /// </summary>
        private static readonly BinaryFormatter Formatter = new BinaryFormatter
        {
            AssemblyFormat = System.Runtime.Serialization.Formatters.FormatterAssemblyStyle.Simple,
            Binder = new LegacyProjectBinder()
        };

        private sealed class LegacyProjectBinder : SerializationBinder
        {
            public override Type BindToType(string assemblyName, string typeName)
            {
                string simpleAssembly = new AssemblyName(assemblyName).Name;
                string applicationAssembly = typeof(Project).Assembly.GetName().Name;
                if (simpleAssembly != "mscorlib" && simpleAssembly != "System" && simpleAssembly != "System.Drawing" && simpleAssembly != applicationAssembly)
                    throw new SerializationException("Legacy project contains an unsupported assembly: " + simpleAssembly);
                Type type = Type.GetType(typeName + ", " + assemblyName, false);
                if (type == null || !Allowed(type))
                    throw new SerializationException("Legacy project contains an unsupported type: " + typeName);
                return type;
            }

            private static bool Allowed(Type type)
            {
                if (type.IsPrimitive || type == typeof(string) || type == typeof(decimal) || type == typeof(object) || type == typeof(Guid)) return true;
                if (type.Assembly == typeof(string).Assembly && typeof(IEqualityComparer<string>).IsAssignableFrom(type)) return true;
                if (type.IsEnum) return type.Assembly == typeof(Project).Assembly || type.Assembly == typeof(System.Drawing.Color).Assembly;
                if (type.IsArray) return Allowed(type.GetElementType());
                if (type == typeof(System.Drawing.Size) || type == typeof(System.Drawing.SizeF) || type == typeof(System.Drawing.Point) || type == typeof(System.Drawing.PointF)) return true;
                if (type.IsGenericType)
                {
                    Type definition = type.GetGenericTypeDefinition();
                    if (definition != typeof(List<>) && definition != typeof(Dictionary<,>) && definition != typeof(KeyValuePair<,>)) return false;
                    return type.GetGenericArguments().All(Allowed);
                }
                return false;
            }
        }

        #endregion

        #region Public properties

        /// <summary>
        /// Holds the actual project settings
        /// </summary>
        public static List<Dictionary<string, object>> ProjectSettings = new List<Dictionary<string, object>>();

        #endregion

        #region Methods

        /// <summary>
        /// Add new settings
        /// </summary>
        /// <param name="settings">Dictionary holding the new settings</param>
        public static void AddSettings(Dictionary<string, object> settings)
        {
            if (settings == null) return;

            // Add image to dictionary
            settings.Add("ImageName", Path.GetFileName(Settings.GetObject<string>("Core.LastOpenFile", null)));
            settings.Add("ImageBase64", ConvertImageToBase64(Settings.GetObject<string>("Core.LastOpenFile", null)));

            ProjectSettings.Add(settings);
        }

        /// <summary>
        /// Clear all project settings (has to be done if file new file is opened or last file is reloaded)
        /// </summary>
        public static void ClearSettings()
        {
            ProjectSettings.Clear();
        }

        /// <summary>
        /// Store project to file
        /// </summary>
        /// <param name="filename">Filepath where project should be stored</param>
        public static void StoreSettings(string filename)
        {
            throw new NotSupportedException("Legacy BinaryFormatter projects are read-only. Save projects with LayerProjectSerializer v2.");
        }

        /// <summary>
        /// Loads a project from a file
        /// </summary>
        /// <param name="filename">Filepath to the project</param>
        /// <returns>Settings of the project</returns>
        public static List<Dictionary<string, object>> LoadProject(string filename)
        {
            if (string.IsNullOrEmpty(filename)) return new List<Dictionary<string, object>>();
            if (!File.Exists(filename)) return new List<Dictionary<string, object>>();

            List<Dictionary<string, object>> project;

            using (var fs = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                project = (List<Dictionary<string, object>>)Formatter.Deserialize(fs);
                fs.Close();
            }

            return project;
        }

        /// <summary>
        /// Converts an image to base64 string
        /// </summary>
        /// <param name="imagePath">Path to the image file</param>
        /// <returns></returns>
        private static string ConvertImageToBase64(string imagePath)
        {
            using (var image = Image.FromFile(imagePath))
            {
                using (var m = new MemoryStream())
                {
                    image.Save(m, image.RawFormat);
                    var imageBytes = m.ToArray();
                    return Convert.ToBase64String(imageBytes);
                }
            }
        }

        /// <summary>
        /// Save base64 image to file
        /// </summary>
        /// <param name="base64Image">Base64 image string</param>
        /// <param name="imagePath">Path where to store the image</param>
        public static void SaveImage(string base64Image, string imagePath)
        {
            var bytes = Convert.FromBase64String(base64Image);
            using (var ms = new MemoryStream(bytes))
            {
                var image = Image.FromStream(ms);
                image.Save(imagePath);
            }
        }

        #endregion
    }
}
