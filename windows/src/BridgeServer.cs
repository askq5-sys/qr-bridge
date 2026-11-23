// Маленький HTTP-сервер в локальной сети: отдаёт телефону страницу и
// принимает/отдаёт текст. Без кода доступа (меняется при каждом запуске)
// отвечает 403. TcpListener не требует прав администратора.
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace QrBridge
{
    public sealed class BridgeServer
    {
        const int MaxBody = 200000;
        const string Plain = "text/plain; charset=utf-8";

        static readonly Regex Placeholder = new Regex(@"\{\{([\w.]+)\}\}");

        readonly string template;
        readonly I18n i18n;
        readonly object sync = new object();
        string text = "";
        TcpListener listener;

        public string Token { get; private set; }
        public int Port { get; private set; }

        /// <summary>Пришёл текст с телефона. Вызывается из потока сервера!</summary>
        public event Action<string> TextReceived;

        public BridgeServer(string pageTemplate, I18n i18n)
        {
            Token = MakeToken();
            template = pageTemplate;
            this.i18n = i18n;
        }

        /// <summary>Страница для телефона на языке, выбранном в окне.</summary>
        string RenderPage()
        {
            string html = Placeholder.Replace(template, delegate (Match m)
            {
                string key = m.Groups[1].Value;
                return key == "lang" ? i18n.Lang : i18n.T(key);
            });
            return html.Replace("%TOKEN%", Token);
        }

        /// <summary>Текст, который телефон может забрать.</summary>
        public string Text
        {
            get { lock (sync) return text; }
            set { lock (sync) text = value ?? ""; }
        }

        public string Url(string ip)
        {
            return "http://" + ip + ":" + Port + "/?t=" + Token;
        }

        public void Start()
        {
            for (int p = 8765; p < 8785; p++)
            {
                try
                {
                    TcpListener l = new TcpListener(IPAddress.Any, p);
                    l.Start();
                    listener = l;
                    Port = p;
                    break;
                }
                catch (SocketException) { }
            }
            if (listener == null) throw new InvalidOperationException(i18n.T("error.port", "a", 8765, "b", 8784));
            Thread th = new Thread(new ThreadStart(AcceptLoop));
            th.IsBackground = true;
            th.Start();
        }

        public void Stop()
        {
            try { if (listener != null) listener.Stop(); }
            catch (Exception) { }
        }

        static string MakeToken()
        {
            byte[] b = new byte[9];
            using (RNGCryptoServiceProvider rng = new RNGCryptoServiceProvider()) rng.GetBytes(b);
            return Convert.ToBase64String(b).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        }

        void AcceptLoop()
        {
            while (true)
            {
                TcpClient c;
                try { c = listener.AcceptTcpClient(); }
                catch (Exception) { return; }   // сервер остановлен
                ThreadPool.QueueUserWorkItem(new WaitCallback(Handle), c);
            }
        }

        bool TokenOk(string query)
        {
            foreach (string part in query.Split('&'))
            {
                int eq = part.IndexOf('=');
                if (eq > 0 && part.Substring(0, eq) == "t")
                {
                    string v = Uri.UnescapeDataString(part.Substring(eq + 1));
                    if (v.Length != Token.Length) return false;
                    int diff = 0;
                    for (int i = 0; i < v.Length; i++) diff |= v[i] ^ Token[i];
                    return diff == 0;
                }
            }
            return false;
        }

        static int FindHeaderEnd(byte[] b, int len)
        {
            for (int i = 0; i + 3 < len; i++)
                if (b[i] == 13 && b[i + 1] == 10 && b[i + 2] == 13 && b[i + 3] == 10) return i;
            return -1;
        }

        static void Send(Stream s, int code, string body, string ctype)
        {
            byte[] data = Encoding.UTF8.GetBytes(body ?? "");
            string reason = code == 200 ? "OK" : code == 403 ? "Forbidden" : code == 404 ? "Not Found" : "Payload Too Large";
            string head = "HTTP/1.1 " + code + " " + reason + "\r\n" +
                          "Content-Type: " + ctype + "\r\n" +
                          "Content-Length: " + data.Length + "\r\n" +
                          "Cache-Control: no-store\r\n" +
                          "Connection: close\r\n\r\n";
            byte[] hb = Encoding.ASCII.GetBytes(head);
            s.Write(hb, 0, hb.Length);
            s.Write(data, 0, data.Length);
            s.Flush();
        }

        void Handle(object state)
        {
            TcpClient client = (TcpClient)state;
            try
            {
                using (client)
                {
                    NetworkStream s = client.GetStream();
                    s.ReadTimeout = 10000;
                    s.WriteTimeout = 10000;

                    // --- заголовки ---
                    byte[] buf = new byte[8192];
                    MemoryStream ms = new MemoryStream();
                    int headerEnd = -1;
                    while (headerEnd < 0)
                    {
                        int n = s.Read(buf, 0, buf.Length);
                        if (n <= 0) return;
                        ms.Write(buf, 0, n);
                        headerEnd = FindHeaderEnd(ms.GetBuffer(), (int)ms.Length);
                        if (headerEnd < 0 && ms.Length > 65536) return;
                    }
                    byte[] all = ms.ToArray();
                    string[] lines = Encoding.ASCII.GetString(all, 0, headerEnd)
                                             .Split(new string[] { "\r\n" }, StringSplitOptions.None);
                    string[] requestLine = lines[0].Split(' ');
                    if (requestLine.Length < 2) return;
                    string method = requestLine[0].ToUpperInvariant();
                    string target = requestLine[1];
                    int contentLength = 0;
                    for (int i = 1; i < lines.Length; i++)
                    {
                        int colon = lines[i].IndexOf(':');
                        if (colon > 0 && string.Equals(lines[i].Substring(0, colon).Trim(), "Content-Length", StringComparison.OrdinalIgnoreCase))
                            int.TryParse(lines[i].Substring(colon + 1).Trim(), out contentLength);
                    }
                    string path = target, query = "";
                    int qm = target.IndexOf('?');
                    if (qm >= 0) { path = target.Substring(0, qm); query = target.Substring(qm + 1); }

                    // --- маршруты ---
                    if (!TokenOk(query)) { Send(s, 403, i18n.T("http.forbidden"), Plain); return; }

                    if (method == "GET" && path == "/") Send(s, 200, RenderPage(), "text/html; charset=utf-8");
                    else if (method == "GET" && path == "/api/text") Send(s, 200, Text, Plain);
                    else if (method == "POST" && path == "/api/text")
                    {
                        if (contentLength < 0 || contentLength > MaxBody) { Send(s, 413, i18n.T("http.toolarge"), Plain); return; }
                        MemoryStream body = new MemoryStream();
                        int bodyStart = headerEnd + 4;
                        body.Write(all, bodyStart, all.Length - bodyStart);
                        while (body.Length < contentLength)
                        {
                            int n2 = s.Read(buf, 0, buf.Length);
                            if (n2 <= 0) break;
                            body.Write(buf, 0, n2);
                        }
                        byte[] bytes = body.ToArray();
                        string received = Encoding.UTF8.GetString(bytes, 0, Math.Min(bytes.Length, contentLength));
                        Text = received;
                        Action<string> handler = TextReceived;
                        if (handler != null) handler(received);
                        Send(s, 200, "ok", Plain);
                    }
                    else Send(s, 404, i18n.T("http.notfound"), Plain);
                }
            }
            catch (Exception) { }   // обрыв связи с телефоном — не повод падать
        }
    }
}
