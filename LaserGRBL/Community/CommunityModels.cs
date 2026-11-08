using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Runtime.Serialization.Json;
using System.Globalization;
using System.Linq;

namespace LaserGRBL.Community
{
    public static class CommunityJson
    {
        public static string Write(object value)
        {
            if (value == null) return "null";
            if (value is string)
            {
                var text = new StringBuilder("\"");
                foreach (char c in (string)value)
                {
                    if (c == '"' || c == '\\') text.Append('\\').Append(c);
                    else if (c < 32) text.Append("\\u").Append(((int)c).ToString("x4"));
                    else text.Append(c);
                }
                return text.Append('"').ToString();
            }
            if (value is bool) return (bool)value ? "true" : "false";
            if (value.GetType().IsPrimitive || value is decimal) return Convert.ToString(value, CultureInfo.InvariantCulture);
            var dictionary = value as System.Collections.IDictionary;
            if (dictionary != null) return "{" + string.Join(",", dictionary.Keys.Cast<object>().Select(k => Write(Convert.ToString(k, CultureInfo.InvariantCulture)) + ":" + Write(dictionary[k]))) + "}";
            var sequence = value as System.Collections.IEnumerable;
            if (sequence != null) return "[" + string.Join(",", sequence.Cast<object>().Select(Write)) + "]";
            return "{" + string.Join(",", value.GetType().GetProperties().Where(p => p.CanRead && p.GetIndexParameters().Length == 0).Select(p => Write(p.Name) + ":" + Write(p.GetValue(value, null)))) + "}";
        }
        public static T Read<T>(string value)
        {
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(value)))
                return (T)new DataContractJsonSerializer(typeof(T), new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true, MaxItemsInObjectGraph = 1000000 }).ReadObject(stream);
        }
        public static byte[] Bytes(object value) { return Encoding.UTF8.GetBytes(Write(value)); }
    }

    public sealed class CommunityUser
    {
        public string id { get; set; }
        public string email { get; set; }
        public bool emailVerified { get; set; }
        public string displayName { get; set; }
        public string bio { get; set; }
        public string avatarUrl { get; set; }
        public override string ToString() { return displayName ?? id; }
    }
    public sealed class CommunitySession
    {
        public CommunityUser user { get; set; }
        public string accessToken { get; set; }
        public string refreshToken { get; set; }
        public string libraryKey { get; set; }
        public string expiresAt { get; set; }
        public string server { get; set; }
    }
    public sealed class CommunityPage<T>
    {
        public List<T> items { get; set; } = new List<T>();
        public string nextCursor { get; set; }
    }
    public sealed class CommunityQuota
    {
        public long limitBytes { get; set; } = 200000000;
        public long usedBytes { get; set; }
        public long reservedBytes { get; set; }
    }
    public sealed class CommunityDesign
    {
        public string id { get; set; }
        public string title { get; set; }
        public string description { get; set; }
        public string category { get; set; }
        public string[] tags { get; set; }
        public CommunityUser designer { get; set; }
        public string licenseVersion { get; set; }
        public string status { get; set; }
        public string thumbnail { get; set; }
        public string sha256 { get; set; }
        public int version { get; set; }
        public override string ToString() { return title + " · " + (designer == null ? "" : designer.displayName); }
    }
    public sealed class CommunityReceipt
    {
        public string payload { get; set; }
        public string signature { get; set; }
    }
    public sealed class CommunityEntitlement
    {
        public string accountId { get; set; }
        public string designId { get; set; }
        public string acquisitionId { get; set; }
        public string sha256 { get; set; }
        public string licenseVersion { get; set; }
    }
    public sealed class CommunityContent
    {
        public string id { get; set; }
        public string designId { get; set; }
        public string title { get; set; }
        public string svg { get; set; }
        public CommunityReceipt receipt { get; set; }
    }
    public sealed class LibraryEntry
    {
        public bool containsAcquired { get; set; }
        public string id { get; set; } = Guid.NewGuid().ToString("N");
        public string title { get; set; }
        public string[] tags { get; set; } = new string[0];
        public string description { get; set; }
        public string category { get; set; }
        public string updatedAt { get; set; }
        public string remoteId { get; set; }
        public int version { get; set; }
        public string acquisitionId { get; set; }
        public string designId { get; set; }
        public override string ToString() { return title + (acquisitionId == null ? "" : " · " + CommunityText.Get("Acquired")); }
    }
    public sealed class LibraryBundle
    {
        public LibraryEntry entry { get; set; }
        public string project { get; set; }
        public string thumbnail { get; set; }
    }
    public sealed class CommunityUpload
    {
        public string id { get; set; }
        public string url { get; set; }
        public string method { get; set; }
        public Dictionary<string, string> headers { get; set; }
    }
    public sealed class CommunityError : Exception
    {
        public string Code { get; private set; }
        public int Status { get; private set; }
        public CommunityError(int status, string code, string message) : base(message) { Status = status; Code = code; }
    }
    public sealed class ErrorEnvelope
    {
        public ErrorBody error { get; set; }
    }
    public sealed class ErrorBody { public string code { get; set; } public string message { get; set; } }
    internal static class CommunityText
    {
        private static readonly System.Resources.ResourceManager Resources = new System.Resources.ResourceManager("LaserGRBL.Community.CommunityText", typeof(CommunityText).Assembly);
        public static string Get(string key) { return Resources.GetString(key, System.Globalization.CultureInfo.CurrentUICulture) ?? key; }
    }
}
