﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Cowboy.Sockets.WebSockets
{
    internal class WebSocketClientHandshaker
    {
        public const string MagicHandeshakeAcceptedKey = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

        private static readonly List<string> HeaderItems = new List<string>()
        {
            "Upgrade",
            "Connection",
            "Sec-WebSocket-Accept",
            "Sec-WebSocket-Version",
            "Sec-WebSocket-Protocol",
            "Sec-WebSocket-Extensions",
            "Origin",
            "Date",
            "Server",
            "Cookie",
            "WWW-Authenticate",
        };

        public static byte[] CreateOpenningHandshakeRequest(
            string host,
            string path,
            out string key,
            string protocol = null,
            string version = null,
            string extensions = null,
            string origin = null,
            IEnumerable<KeyValuePair<string, string>> cookies = null)
        {
            if (string.IsNullOrEmpty(host))
                throw new ArgumentNullException("host");
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException("path");

            var sb = new StringBuilder();

            sb.AppendFormatWithCrCf("GET {0} HTTP/1.1", path);
            sb.AppendFormatWithCrCf("Host: {0}", host);

            sb.AppendWithCrCf("Upgrade: websocket");
            sb.AppendWithCrCf("Connection: Upgrade");

            // In addition to Upgrade headers, the client sends a Sec-WebSocket-Key header 
            // containing base64-encoded random bytes, and the server replies with a hash of the key 
            // in the Sec-WebSocket-Accept header. This is intended to prevent a caching proxy 
            // from re-sending a previous WebSocket conversation, and does not provide any authentication, 
            // privacy or integrity. The hashing function appends the 
            // fixed string 258EAFA5-E914-47DA-95CA-C5AB0DC85B11 (a GUID) to the value 
            // from Sec-WebSocket-Key header (which is not decoded from base64), 
            // applies the SHA-1 hashing function, and encodes the result using base64.
            key = Convert.ToBase64String(Encoding.ASCII.GetBytes(Guid.NewGuid().ToString().Substring(0, 16)));
            sb.AppendFormatWithCrCf("Sec-WebSocket-Key: {0}", key);

            // The |Sec-WebSocket-Version| header field in the client's
            // handshake includes the version of the WebSocket Protocol with
            // which the client is attempting to communicate.  If this
            // version does not match a version understood by the server, the
            // server MUST abort the WebSocket handshake described in this
            // section and instead send an appropriate HTTP error code(such
            // as 426 Upgrade Required) and a |Sec-WebSocket-Version| header
            // field indicating the version(s)the server is capable of understanding.
            if (!string.IsNullOrEmpty(version))
                sb.AppendFormatWithCrCf("Sec-WebSocket-Version: {0}", version);
            else
                sb.AppendFormatWithCrCf("Sec-WebSocket-Version: {0}", 13);

            // Optionally
            // The |Sec-WebSocket-Protocol| request-header field can be
            // used to indicate what subprotocols(application - level protocols
            // layered over the WebSocket Protocol) are acceptable to the client.
            if (!string.IsNullOrEmpty(protocol))
                sb.AppendFormatWithCrCf("Sec-WebSocket-Protocol: {0}", protocol);

            // Optionally
            // A (possibly empty) list representing the protocol-level
            // extensions the server is ready to use.
            if (!string.IsNullOrEmpty(extensions))
                sb.AppendFormatWithCrCf("Sec-WebSocket-Extensions: {0}", extensions);

            // Optionally
            // The |Origin| header field is used to protect against
            // unauthorized cross-origin use of a WebSocket server by scripts using         
            // the WebSocket API in a web browser.
            // This header field is sent by browser clients; for non-browser clients, 
            // this header field may be sent if it makes sense in the context of those clients.
            if (!string.IsNullOrEmpty(origin))
                sb.AppendFormatWithCrCf("Origin: {0}", origin);

            if (cookies != null && cookies.Any())
            {
                string[] pairs = new string[cookies.Count()];

                for (int i = 0; i < cookies.Count(); i++)
                {
                    var item = cookies.ElementAt(i);
                    pairs[i] = item.Key + "=" + Uri.EscapeUriString(item.Value);
                }

                sb.AppendFormatWithCrCf("Cookie: {0}", string.Join(";", pairs));
            }

            sb.AppendWithCrCf();

            // GET /chat HTTP/1.1
            // Host: server.example.com
            // Upgrade: websocket
            // Connection: Upgrade
            // Sec-WebSocket-Key: x3JJHMbDL1EzLkh9GBhXDw==
            // Sec-WebSocket-Protocol: chat, superchat
            // Sec-WebSocket-Version: 13
            // Origin: http://example.com
            var message = sb.ToString();
            return Encoding.UTF8.GetBytes(message);
        }

        public static bool VerifyOpenningHandshakeResponse(byte[] buffer, int offset, int count, string secWebSocketKey)
        {
            if (buffer == null)
                throw new ArgumentNullException("buffer");
            if (string.IsNullOrEmpty(secWebSocketKey))
                throw new ArgumentNullException("context.SecWebSocketKey");

            var response = Encoding.UTF8.GetString(buffer, offset, count);

            // HTTP/1.1 101 Switching Protocols
            // Upgrade: websocket
            // Connection: Upgrade
            // Sec-WebSocket-Accept: 1tGBmA9p0DQDgmFll6P0/UcVS/E=
            // Sec-WebSocket-Protocol: chat
            var headerItems = ParseWebSocketResponseHeaderItems(response);

            if (!headerItems.ContainsKey("HttpStatusCode"))
                return false;
            if (!headerItems.ContainsKey("Connection"))
                return false;
            if (!headerItems.ContainsKey("Upgrade"))
                return false;
            if (!headerItems.ContainsKey("Sec-WebSocket-Accept"))
                return false;

            // If the status code received from the server is not 101, the
            // client handles the response per HTTP [RFC2616] procedures.  In
            // particular, the client might perform authentication if it
            // receives a 401 status code; the server might redirect the client
            // using a 3xx status code (but clients are not required to follow
            // them), etc.
            if (headerItems["HttpStatusCode"] != "101")
                return false;

            // If the response lacks an |Upgrade| header field or the |Upgrade|
            // header field contains a value that is not an ASCII case-
            // insensitive match for the value "websocket", the client MUST
            // _Fail the WebSocket Connection_.
            if (headerItems["Connection"].ToLowerInvariant() != "upgrade")
                return false;

            // If the response lacks a |Connection| header field or the
            // |Connection| header field doesn't contain a token that is an
            // ASCII case-insensitive match for the value "Upgrade", the client
            // MUST _Fail the WebSocket Connection_.
            if (headerItems["Upgrade"].ToLowerInvariant() != "websocket")
                return false;

            // If the response lacks a |Sec-WebSocket-Accept| header field or
            // the |Sec-WebSocket-Accept| contains a value other than the
            // base64-encoded SHA-1 of the concatenation of the |Sec-WebSocket-
            // Key| (as a string, not base64-decoded) with the string "258EAFA5-
            // E914-47DA-95CA-C5AB0DC85B11" but ignoring any leading and
            // trailing whitespace, the client MUST _Fail the WebSocket Connection_.
            string challenge =
                Convert.ToBase64String(
                    SHA1.Create().ComputeHash(
                        Encoding.ASCII.GetBytes(
                            secWebSocketKey + MagicHandeshakeAcceptedKey)));
            if (!headerItems["Sec-WebSocket-Accept"].Equals(challenge, StringComparison.OrdinalIgnoreCase))
                return false;

            // If the response includes a |Sec-WebSocket-Extensions| header
            // field and this header field indicates the use of an extension
            // that was not present in the client's handshake (the server has
            // indicated an extension not requested by the client), the client
            // MUST _Fail the WebSocket Connection_.

            // If the response includes a |Sec-WebSocket-Protocol| header field
            // and this header field indicates the use of a subprotocol that was
            // not present in the client's handshake (the server has indicated a
            // subprotocol not requested by the client), the client MUST _Fail
            // the WebSocket Connection_.

            return true;
        }

        private static Dictionary<string, string> ParseWebSocketResponseHeaderItems(string response)
        {
            var headerItems = new Dictionary<string, string>();

            var lines = response.Split(new char[] { '\r', '\n' }).Where(l => l.Length > 0);
            foreach (var line in lines)
            {
                if (line.StartsWith(@"HTTP/"))
                {
                    var segements = line.Split(' ');
                    if (segements.Length > 1)
                    {
                        headerItems.Add("HttpStatusCode", segements[1]);
                    }
                }
                else
                {
                    foreach (var item in HeaderItems)
                    {
                        if (line.StartsWith(item + ":"))
                        {
                            var index = line.IndexOf(':');
                            if (index != -1)
                            {
                                var value = line.Substring(index + 1);
                                headerItems.Add(item, value.Trim());
                            }
                        }
                    }
                }
            }

            return headerItems;
        }
    }
}
