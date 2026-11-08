using System;
using System.IO;
using System.Net;
using System.Text;

namespace LaserGRBL.Community
{
    // All calls run on the community window's worker task, never the machine/UI thread.
    public sealed class CommunityClient
    {
        public sealed class WireRequest
        {
            public Uri Uri { get; set; }
            public string Method { get; set; }
            public string Authorization { get; set; }
            public string IdempotencyKey { get; set; }
            public byte[] Body { get; set; }
        }
        public sealed class WireResponse
        {
            public int Status { get; set; }
            public byte[] Body { get; set; }
        }
        private readonly Uri root;
        private readonly Func<WireRequest, WireResponse> transport;
        private readonly Action<CommunitySession> persist;
        private readonly object gate = new object();
        public CommunitySession Session { get; private set; }
        public CommunityClient(string address, CommunitySession session, Action<CommunitySession> persistSession, Func<WireRequest, WireResponse> httpTransport = null)
        {
            Uri uri;
            if (!Uri.TryCreate(address, UriKind.Absolute, out uri) || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo) || uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)) throw new ArgumentException(CommunityText.Get("HttpsOnly"));
            root = new Uri(uri, "api/v1/"); Session = session; persist = persistSession; transport = httpTransport ?? Execute;
        }
        public CommunitySession Login(string email, string password)
        {
            var result = Request<CommunitySession>("POST", "auth/login", new { email, password }, false);
            ValidateSession(result); result.server = root.GetLeftPart(UriPartial.Authority); Session = result; persist(result); return result;
        }
        private static void ValidateSession(CommunitySession session)
        {
            if (session == null || session.user == null || string.IsNullOrEmpty(session.user.id) || string.IsNullOrEmpty(session.accessToken) || string.IsNullOrEmpty(session.refreshToken) || Convert.FromBase64String(session.libraryKey).Length != 32) throw new InvalidDataException("Invalid session response.");
        }
        public T Request<T>(string method, string path, object data = null, bool authenticated = true, string idempotencyKey = null)
        {
            lock (gate)
            {
                try { return Send<T>(method, path, data, authenticated, idempotencyKey); }
                catch (CommunityError error)
                {
                    if (error.Status != 401 || !authenticated || Session == null) throw;
                    var refreshed = Send<CommunitySession>("POST", "auth/refresh", new { refreshToken = Session.refreshToken }, false, null);
                    ValidateSession(refreshed);
                    if (refreshed.user.id != Session.user.id || refreshed.libraryKey != Session.libraryKey) throw new InvalidDataException("Session account changed.");
                    refreshed.server = Session.server; Session = refreshed; persist(refreshed);
                    return Send<T>(method, path, data, authenticated, idempotencyKey);
                }
            }
        }
        private T Send<T>(string method, string path, object data, bool authenticated, string idempotencyKey)
        {
            var target = new Uri(root, path);
            if (!target.AbsoluteUri.StartsWith(root.AbsoluteUri, StringComparison.Ordinal)) throw new ArgumentException("Invalid API path.");
            if (authenticated && Session == null) throw new CommunityError(401, "AUTH_REQUIRED", CommunityText.Get("LoginRequired"));
            var response = transport(new WireRequest { Uri = target, Method = method, Authorization = authenticated ? "Bearer " + Session.accessToken : null, IdempotencyKey = idempotencyKey, Body = data == null ? null : CommunityJson.Bytes(data) });
            string json = Encoding.UTF8.GetString(response.Body ?? new byte[0]);
            if (response.Status < 200 || response.Status >= 300)
            {
                ErrorEnvelope envelope = null;
                try { envelope = CommunityJson.Read<ErrorEnvelope>(json); } catch (Exception e) when (e is ArgumentException || e is System.Runtime.Serialization.SerializationException || e is System.Xml.XmlException) { }
                throw new CommunityError(response.Status, envelope?.error?.code ?? "HTTP_ERROR", envelope?.error?.message ?? "HTTP " + response.Status);
            }
            return string.IsNullOrWhiteSpace(json) ? default(T) : CommunityJson.Read<T>(json);
        }
        private static WireResponse Execute(WireRequest wire)
        {
            var request = (HttpWebRequest)WebRequest.Create(wire.Uri);
            request.Method = wire.Method; request.AllowAutoRedirect = false; request.Timeout = 30000; request.ReadWriteTimeout = 30000; request.Accept = "application/json";
            if (wire.Authorization != null) request.Headers[HttpRequestHeader.Authorization] = wire.Authorization;
            if (wire.IdempotencyKey != null) request.Headers["Idempotency-Key"] = wire.IdempotencyKey;
            try
            {
                if (wire.Body != null)
                {
                    request.ContentType = "application/json; charset=utf-8"; request.ContentLength = wire.Body.Length;
                    using (var stream = request.GetRequestStream()) stream.Write(wire.Body, 0, wire.Body.Length);
                }
                using (var response = (HttpWebResponse)request.GetResponse())
                using (var stream = response.GetResponseStream()) return new WireResponse { Status = (int)response.StatusCode, Body = ReadLimited(stream, 100000000) };
            }
            catch (WebException error)
            {
                var response = error.Response as HttpWebResponse;
                if (response == null) throw new CommunityError(0, "NETWORK_UNAVAILABLE", CommunityText.Get("NetworkUnavailable"));
                using (response)
                using (var stream = response.GetResponseStream())
                {
                    return new WireResponse { Status = (int)response.StatusCode, Body = ReadLimited(stream, 65536) };
                }
            }
        }
        public static byte[] ReadLimited(Stream stream, int maximum)
        {
            using (var output = new MemoryStream())
            {
                var buffer = new byte[16384]; int count;
                while ((count = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (output.Length + count > maximum) throw new InvalidDataException("Response exceeds limit.");
                    output.Write(buffer, 0, count);
                }
                return output.ToArray();
            }
        }
        // Presigned transfers deliberately omit API tokens, disable redirects and use exact headers.
        public static byte[] Transfer(CommunityUpload upload, byte[] body = null)
        {
            var uri = new Uri(upload.url);
            if (uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo)) throw new InvalidDataException("Invalid transfer URL.");
            var request = (HttpWebRequest)WebRequest.Create(uri);
            request.Method = body == null ? "GET" : "PUT"; request.AllowAutoRedirect = false;
            if (upload.method != request.Method) throw new InvalidDataException("Invalid transfer method.");
            request.Timeout = 60000; request.ReadWriteTimeout = 60000;
            if (upload.headers != null) foreach (var header in upload.headers)
            {
                if (header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)) request.ContentType = header.Value;
                else if (header.Key.StartsWith("x-amz-", StringComparison.OrdinalIgnoreCase)) request.Headers[header.Key] = header.Value;
                else throw new InvalidDataException("Unsupported transfer header.");
            }
            if (body != null)
            {
                request.ContentLength = body.Length;
                using (var stream = request.GetRequestStream()) stream.Write(body, 0, body.Length);
            }
            using (var response = (HttpWebResponse)request.GetResponse())
            {
                if ((int)response.StatusCode >= 300) throw new InvalidDataException("Unexpected transfer redirect.");
                using (var stream = response.GetResponseStream()) return ReadLimited(stream, body == null ? 100000000 : 65536);
            }
        }
    }
}
