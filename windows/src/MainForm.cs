// Окно программы (WinForms). Раскладка считается вручную в DoLayout(),
// поэтому не зависит от масштаба экрана (125 %, 150 % и т. д.).
//
// QR переключается сам:
//   поле пустое     -> QR подключения телефона (страница для отправки текста);
//   в поле текст    -> QR этого текста (для прокси-клиента или камеры);
//   текст > 2953 Б  -> QR подключения, текст забирается со страницы.
//
// Язык (RU/EN) переключается в правом верхнем углу и запоминается.
using System;
using System.Drawing;
using System.Windows.Forms;

namespace QrBridge
{
    public sealed class MainForm : Form
    {
        readonly BridgeServer server;
        readonly I18n i18n;
        readonly float scale;
        readonly Timer debounce = new Timer();
        readonly ToolTip tips = new ToolTip();
        QrCode qr;

        readonly Label lblText = new Label();
        readonly ComboBox cmbLang = new ComboBox();
        readonly TextBox txt = new TextBox();
        readonly Button btnPaste = new Button();
        readonly Button btnCopy = new Button();
        readonly Button btnClear = new Button();
        readonly Label lblCount = new Label();
        readonly PictureBox pic = new PictureBox();
        readonly Label lblCaption = new Label();
        readonly Label lblIp = new Label();
        readonly ComboBox cmbIp = new ComboBox();
        readonly Button btnRefresh = new Button();
        readonly Label lblStatus = new Label();

        public MainForm(BridgeServer server, I18n i18n)
        {
            this.server = server;
            this.i18n = i18n;
            AutoScaleMode = AutoScaleMode.None;
            Font = SystemFonts.MessageBoxFont;
            StartPosition = FormStartPosition.CenterScreen;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch (Exception) { }

            using (Graphics g = CreateGraphics()) scale = g.DpiX / 96f;

            cmbLang.DropDownStyle = ComboBoxStyle.DropDownList;
            foreach (string code in I18n.Langs) cmbLang.Items.Add(code.ToUpperInvariant());
            cmbLang.SelectedIndex = Math.Max(0, Array.IndexOf(I18n.Langs, i18n.Lang));
            cmbLang.SelectedIndexChanged += OnLangChanged;

            txt.Multiline = true;
            txt.ScrollBars = ScrollBars.Vertical;
            txt.WordWrap = true;
            txt.MaxLength = 1000000;
            txt.Font = new Font("Consolas", 10F);
            txt.TextChanged += OnTextChanged;

            SetupButton(btnPaste, delegate { Paste(); });
            SetupButton(btnCopy, delegate { CopyText(); });
            SetupButton(btnClear, delegate { txt.Text = ""; Say("", false); });
            SetupButton(btnRefresh, delegate { FillIps(); });

            lblCount.TextAlign = ContentAlignment.MiddleRight;

            pic.BackColor = Color.White;
            pic.SizeMode = PictureBoxSizeMode.CenterImage;
            pic.Resize += delegate { RenderQr(); };

            lblCaption.TextAlign = ContentAlignment.MiddleCenter;
            lblCaption.AutoEllipsis = true;

            lblIp.TextAlign = ContentAlignment.MiddleLeft;
            cmbIp.DropDownStyle = ComboBoxStyle.DropDownList;

            lblStatus.AutoEllipsis = true;
            lblStatus.TextAlign = ContentAlignment.MiddleLeft;

            Controls.AddRange(new Control[] {
                lblText, cmbLang, txt, btnPaste, btnCopy, btnClear, lblCount, pic,
                lblCaption, lblIp, cmbIp, btnRefresh, lblStatus });

            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            ClientSize = new Size(Math.Min(S(560), wa.Width - S(40)), Math.Min(S(820), wa.Height - S(60)));
            MinimumSize = new Size(S(440), S(500));

            debounce.Interval = 120;   // пауза после ввода, прежде чем перестраивать QR
            debounce.Tick += delegate { debounce.Stop(); UpdateQr(); };

            server.TextReceived += OnTextReceivedFromServer;

            ApplyLanguage();
            FillIps();
            cmbIp.SelectedIndexChanged += delegate { UpdateQr(); };
        }

        int S(int v) { return (int)Math.Round(v * scale); }

        string T(string key, params object[] pairs) { return i18n.T(key, pairs); }

        static void SetupButton(Button b, EventHandler click)
        {
            b.AutoSize = true;
            b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            b.Padding = new Padding(6, 2, 6, 2);
            b.Click += click;
        }

        // ------------------------------------------------------------------ раскладка
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            DoLayout();
        }

        void DoLayout()
        {
            if (lblStatus.Parent == null) return;   // ещё не собрано
            int pad = S(10), gap = S(6);
            int w = Math.Max(S(100), ClientSize.Width - 2 * pad);
            int textH = lblText.PreferredHeight;

            // сверху вниз: подпись поля и переключатель языка справа
            int y = pad;
            int langW = S(60);
            int langH = cmbLang.Height;
            cmbLang.SetBounds(pad + w - langW, y, langW, langH);
            lblText.SetBounds(pad, y + (langH - textH) / 2, w - langW - gap, textH);
            y += Math.Max(textH, langH) + S(3);
            txt.SetBounds(pad, y, w, S(90));
            y += S(90) + gap;

            int bh = btnPaste.PreferredSize.Height;
            int x = pad;
            foreach (Button b in new Button[] { btnPaste, btnCopy, btnClear })
            {
                Size ps = b.PreferredSize;
                b.SetBounds(x, y, ps.Width, bh);
                x += ps.Width + gap;
            }
            int countW = Math.Max(S(60), pad + w - x);
            lblCount.SetBounds(pad + w - countW, y, countW, bh);
            y += bh + gap;

            // снизу вверх
            int bottom = ClientSize.Height - pad;
            lblStatus.SetBounds(pad, bottom - textH, w, textH);
            bottom -= textH + gap;

            int rowH = Math.Max(cmbIp.PreferredHeight, btnRefresh.PreferredSize.Height);
            int ipLabelW = lblIp.PreferredWidth;
            lblIp.SetBounds(pad, bottom - rowH, ipLabelW, rowH);
            int cx = pad + ipLabelW + gap;
            cmbIp.SetBounds(cx, bottom - rowH + (rowH - cmbIp.Height) / 2, S(150), cmbIp.Height);
            cx += S(150) + gap;
            btnRefresh.SetBounds(cx, bottom - rowH, btnRefresh.PreferredSize.Width, rowH);
            bottom -= rowH + gap;

            lblCaption.SetBounds(pad, bottom - textH, w, textH);
            bottom -= textH + gap;

            // QR занимает всё, что осталось посередине
            pic.SetBounds(pad, y, w, Math.Max(S(60), bottom - y));
        }

        // ------------------------------------------------------------------ QR
        string CurrentIp()
        {
            string ip = cmbIp.SelectedItem as string;
            return ip ?? "127.0.0.1";
        }

        void UpdateQr()
        {
            string text = GetText();
            string url = server.Url(CurrentIp());
            string tooltip = url;
            if (text.Length > 0)
            {
                try
                {
                    qr = QrCode.Encode(text, Ecc.L);
                    lblCaption.Text = T("caption.text", "n", text.Length);
                    tooltip = null;
                }
                catch (DataTooLongException)
                {
                    qr = QrCode.Encode(url, Ecc.M);
                    lblCaption.Text = T("caption.toolong");
                }
            }
            else
            {
                qr = QrCode.Encode(url, Ecc.M);
                lblCaption.Text = T("caption.connect");
            }
            tips.SetToolTip(pic, tooltip);
            RenderQr();
        }

        void RenderQr()
        {
            if (qr == null) return;
            int target = Math.Min(pic.ClientSize.Width, pic.ClientSize.Height) - 4;
            if (target < 40) return;
            Image old = pic.Image;
            pic.Image = qr.ToBitmap(target, 4);
            if (old != null) old.Dispose();
        }

        // ------------------------------------------------------------------ текст
        string GetText() { return txt.Text.Trim(); }

        void OnTextChanged(object sender, EventArgs e)
        {
            string text = GetText();
            server.Text = text;
            lblCount.Text = T("count", "n", text.Length);
            debounce.Stop();
            debounce.Start();
        }

        void Paste()
        {
            try
            {
                if (Clipboard.ContainsText()) { txt.Text = Clipboard.GetText(); Say(T("status.pasted"), false); }
                else Say(T("status.clip_empty"), true);
            }
            catch (Exception) { Say(T("status.clip_read_fail"), true); }
        }

        void CopyText()
        {
            string text = GetText();
            if (text.Length == 0) { Say(T("status.nothing"), true); return; }
            try { Clipboard.SetText(text); Say(T("status.copied", "n", text.Length), false); }
            catch (Exception) { Say(T("status.clip_write_fail"), true); }
        }

        void Say(string msg, bool error)
        {
            lblStatus.ForeColor = error ? Color.FromArgb(198, 40, 40) : Color.FromArgb(46, 125, 50);
            lblStatus.Text = msg;
        }

        // ------------------------------------------------------------------ язык
        void ApplyLanguage()
        {
            Text = T("app.title") + " " + Application.ProductVersion;
            lblText.Text = T("label.text");
            btnPaste.Text = T("btn.paste");
            btnCopy.Text = T("btn.copy");
            btnClear.Text = T("btn.clear");
            btnRefresh.Text = T("btn.refresh");
            lblIp.Text = T("label.ip");
            lblCount.Text = T("count", "n", GetText().Length);
            Say("", false);
            DoLayout();          // ширина кнопок зависит от языка
            UpdateQr();
        }

        void OnLangChanged(object sender, EventArgs e)
        {
            int i = cmbLang.SelectedIndex;
            if (i < 0 || I18n.Langs[i] == i18n.Lang) return;
            i18n.SetLang(I18n.Langs[i]);
            Settings.SaveLang(i18n.Lang);
            ApplyLanguage();
        }

        // ------------------------------------------------------------------ входящие
        void OnTextReceivedFromServer(string text)   // поток сервера
        {
            if (IsHandleCreated) BeginInvoke(new Action<string>(OnIncoming), text);
        }

        void OnIncoming(string text)
        {
            txt.Text = text;
            debounce.Stop();
            UpdateQr();
            CopyText();
            Say(T("status.received", "n", text.Length), false);
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
        }

        void FillIps()
        {
            cmbIp.Items.Clear();
            foreach (string ip in NetInfo.LocalIps()) cmbIp.Items.Add(ip);
            cmbIp.SelectedIndex = 0;
            UpdateQr();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            server.TextReceived -= OnTextReceivedFromServer;
            debounce.Dispose();
            base.OnFormClosed(e);
        }
    }
}
