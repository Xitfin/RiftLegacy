using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

[assembly: AssemblyTitle("Rift Legacy Launcher")]
[assembly: AssemblyProduct("Rift Legacy")]
[assembly: AssemblyVersion("1.0.0.0")]

namespace RiftLegacyLauncher
{
    internal sealed class UpdateManifest
    {
        public string version { get; set; }
        public string packageUrl { get; set; }
        public string sha256 { get; set; }
        public long size { get; set; }
        public string entryPoint { get; set; }
        public string[] releaseNotes { get; set; }
    }

    internal sealed class LauncherForm : Form
    {
        private const string ManifestUrl = "https://raw.githubusercontent.com/Xitfin/RiftLegacy-Updates/main/update/bootstrap.json";
        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HTCAPTION = 0x2;
        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        private readonly string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
        private readonly PrivateFontCollection fonts = new PrivateFontCollection();
        private FontFamily cinzel;
        private FontFamily spectral;
        private Image iconImage;
        private readonly System.Windows.Forms.Timer animation;
        private int progress;
        private int shimmer;
        private float floatPhase;
        private string statusTitle = "CHECKING FOR UPDATES";
        private string subStatus = "Checking installed files...";
        private string sizeLabel = "";
        private string patchVersion = "";
        private string[] notes = new string[0];
        private bool working = true;
        private bool done;
        private Rectangle minimizeRect = new Rectangle(782, 0, 46, 36);
        private Rectangle closeRect = new Rectangle(874, 0, 46, 36);
        private Point mouse;

        public LauncherForm()
        {
            Text = "Rift Legacy Launcher";
            ClientSize = new Size(920, 650);
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(1, 10, 19);
            DoubleBuffered = true;
            LoadResources();
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            animation = new System.Windows.Forms.Timer { Interval = 40 };
            animation.Tick += delegate { shimmer = (shimmer + 9) % 900; floatPhase += 0.05f; Invalidate(); };
            animation.Start();
            Shown += delegate { Region = new Region(Rounded(new Rectangle(0, 0, Width, Height), 8)); var thread = new Thread(Run); thread.IsBackground = true; thread.Start(); };
            MouseDown += OnMouseDown;
            MouseMove += delegate(object sender, MouseEventArgs e) { mouse = e.Location; Invalidate(new Rectangle(780, 0, 140, 36)); };
            MouseLeave += delegate { mouse = Point.Empty; Invalidate(new Rectangle(780, 0, 140, 36)); };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            DrawTitleBar(g);
            DrawBody(g);
        }

        private void DrawTitleBar(Graphics g)
        {
            g.FillRectangle(new SolidBrush(Color.FromArgb(10, 22, 32)), 0, 0, Width, 36);
            if (iconImage != null) g.DrawImage(iconImage, new Rectangle(12, 10, 16, 16));
            DrawText(g, "Rift Legacy Launcher", UiFont(9f), Color.FromArgb(201, 194, 176), new RectangleF(37, 0, 300, 36), StringAlignment.Near, StringAlignment.Center);
            if (minimizeRect.Contains(mouse)) g.FillRectangle(new SolidBrush(Color.FromArgb(24, 255, 255, 255)), minimizeRect);
            if (closeRect.Contains(mouse)) g.FillRectangle(new SolidBrush(Color.FromArgb(232, 17, 35)), closeRect);
            DrawText(g, "—", UiFont(10f), Color.FromArgb(201, 194, 176), minimizeRect, StringAlignment.Center, StringAlignment.Center);
            DrawText(g, "□", UiFont(9f), Color.FromArgb(201, 194, 176), new RectangleF(828, 0, 46, 36), StringAlignment.Center, StringAlignment.Center);
            DrawText(g, "×", UiFont(12f), closeRect.Contains(mouse) ? Color.White : Color.FromArgb(201, 194, 176), closeRect, StringAlignment.Center, StringAlignment.Center);
        }

        private void DrawBody(Graphics g)
        {
            Rectangle body = new Rectangle(0, 36, Width, Height - 36);
            using (var brush = new LinearGradientBrush(body, Color.FromArgb(10, 37, 48), Color.FromArgb(1, 10, 19), LinearGradientMode.Vertical)) g.FillRectangle(brush, body);
            for (int i = 8; i >= 1; i--)
            {
                int alpha = 3 + (8 - i) * 2;
                using (var glow = new SolidBrush(Color.FromArgb(alpha, 3, 151, 171))) g.FillEllipse(glow, 460 - i * 48, -30 - i * 20, i * 96, i * 55);
            }
            float logoY = 92f + (float)Math.Sin(floatPhase) * 6f;
            DrawGradientLogo(g, "RIFT LEGACY", new RectangleF(50, logoY, 820, 88));
            DrawText(g, "RELIVE THE RIFT", SpectralFont(10.5f, FontStyle.Regular), Color.FromArgb(91, 143, 154), new RectangleF(50, logoY + 82, 820, 28), StringAlignment.Center, StringAlignment.Center);

            Rectangle panel = new Rectangle(120, 248, 680, 310);
            DrawText(g, statusTitle, CinzelFont(12f, FontStyle.Bold), Color.FromArgb(200, 170, 110), new RectangleF(panel.X, panel.Y, 520, 30), StringAlignment.Near, StringAlignment.Center);
            DrawText(g, progress + "%", CinzelFont(15f, FontStyle.Bold), Color.FromArgb(84, 208, 224), new RectangleF(panel.Right - 110, panel.Y - 2, 110, 34), StringAlignment.Far, StringAlignment.Center);
            DrawProgress(g, new Rectangle(panel.X, panel.Y + 43, panel.Width, 26));
            DrawText(g, subStatus, SpectralFont(10f, FontStyle.Regular), Color.FromArgb(123, 138, 144), new RectangleF(panel.X, panel.Y + 78, 460, 24), StringAlignment.Near, StringAlignment.Center);
            DrawText(g, sizeLabel, SpectralFont(10f, FontStyle.Regular), Color.FromArgb(91, 143, 154), new RectangleF(panel.Right - 250, panel.Y + 78, 250, 24), StringAlignment.Far, StringAlignment.Center);
            DrawNotes(g, new Rectangle(panel.X, panel.Y + 122, panel.Width, 126));
            if (done)
            {
                float angle = shimmer % 360;
                using (var pen = new Pen(Color.FromArgb(84, 208, 224), 3f)) g.DrawArc(pen, 300, panel.Y + 273, 24, 24, angle, 270);
                DrawText(g, "STARTING RIFT LEGACY...", CinzelFont(13.5f, FontStyle.Bold), Color.FromArgb(84, 208, 224), new RectangleF(338, panel.Y + 265, 360, 38), StringAlignment.Near, StringAlignment.Center);
            }
            DrawText(g, "RIFT LEGACY LAUNCHER  ·  v1.0", CinzelFont(7.5f, FontStyle.Regular), Color.FromArgb(61, 74, 79), new RectangleF(0, Height - 34, Width, 20), StringAlignment.Center, StringAlignment.Center);
        }

        private void DrawProgress(Graphics g, Rectangle rect)
        {
            using (var back = new SolidBrush(Color.FromArgb(10, 16, 20))) g.FillRectangle(back, rect);
            using (var border = new Pen(Color.FromArgb(120, 90, 40))) g.DrawRectangle(border, rect);
            int fillWidth = Math.Max(0, Math.Min(rect.Width - 2, (rect.Width - 2) * progress / 100));
            if (fillWidth <= 0) return;
            Rectangle fill = new Rectangle(rect.X + 1, rect.Y + 1, fillWidth, rect.Height - 1);
            using (var b = new LinearGradientBrush(fill, Color.FromArgb(2, 102, 120), Color.FromArgb(84, 208, 224), LinearGradientMode.Horizontal)) g.FillRectangle(b, fill);
            int stripe = rect.X - 130 + shimmer;
            GraphicsState state = g.Save(); g.SetClip(fill);
            using (var shine = new LinearGradientBrush(new Rectangle(stripe, rect.Y, 120, rect.Height), Color.FromArgb(0, 255, 255, 255), Color.FromArgb(135, 255, 255, 255), LinearGradientMode.Horizontal)) g.FillRectangle(shine, stripe, rect.Y, 120, rect.Height);
            g.Restore(state);
        }

        private void DrawNotes(Graphics g, Rectangle rect)
        {
            using (var fill = new SolidBrush(Color.FromArgb(85, 4, 20, 28))) g.FillRectangle(fill, rect);
            using (var border = new Pen(Color.FromArgb(42, 74, 82))) g.DrawRectangle(border, rect);
            string title = "UPDATE" + (string.IsNullOrWhiteSpace(patchVersion) ? "" : "  ·  " + patchVersion);
            DrawText(g, title, CinzelFont(9.5f, FontStyle.Bold), Color.FromArgb(200, 170, 110), new RectangleF(rect.X + 24, rect.Y + 13, rect.Width - 48, 24), StringAlignment.Near, StringAlignment.Center);
            string[] shown = notes == null || notes.Length == 0 ? new[] { "Checking the latest Rift Legacy package", "Verifying downloaded files", "Preparing the classic Rift experience" } : notes;
            for (int i = 0; i < Math.Min(3, shown.Length); i++)
            {
                DrawText(g, "◆", SpectralFont(7f, FontStyle.Regular), Color.FromArgb(3, 151, 171), new RectangleF(rect.X + 24, rect.Y + 43 + i * 23, 20, 20), StringAlignment.Near, StringAlignment.Center);
                DrawText(g, shown[i], SpectralFont(9.5f, FontStyle.Regular), Color.FromArgb(167, 176, 179), new RectangleF(rect.X + 48, rect.Y + 42 + i * 23, rect.Width - 72, 22), StringAlignment.Near, StringAlignment.Center);
            }
        }

        private void Run()
        {
            string installDirectory = Path.Combine(baseDirectory, "Rift Legacy");
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                UpdateManifest manifest = DownloadManifest();
                patchVersion = manifest.version; notes = manifest.releaseNotes ?? new string[0]; RefreshUi();
                string localVersion = ReadLocalVersion(installDirectory);
                string entryPoint = string.IsNullOrWhiteSpace(manifest.entryPoint) ? "Rift Legacy.exe" : manifest.entryPoint;
                string executable = Path.Combine(installDirectory, entryPoint);
                if (!File.Exists(executable) || IsNewer(manifest.version, localVersion)) Install(manifest, installDirectory);
                SetState("AUTOMATIC LAUNCH", "Update complete · starting Rift Legacy", 100, FormatSize(manifest.size, manifest.size), true);
                Thread.Sleep(900);
                Launch(Path.Combine(installDirectory, entryPoint));
                BeginInvoke(new Action(Close));
            }
            catch (Exception ex)
            {
                string fallback = Path.Combine(installDirectory, "Rift Legacy.exe");
                if (File.Exists(fallback))
                {
                    SetState("OFFLINE MODE", "GitHub unavailable · starting installed version", 100, "", true);
                    Thread.Sleep(650); Launch(fallback); BeginInvoke(new Action(Close)); return;
                }
                working = false;
                SetState("INSTALLATION FAILED", ex.Message, 0, "", false);
                BeginInvoke(new Action(delegate { MessageBox.Show(this, ex.Message, "Rift Legacy", MessageBoxButtons.OK, MessageBoxIcon.Error); }));
            }
        }

        private UpdateManifest DownloadManifest()
        {
            SetState("CHECKING FOR UPDATES", "Checking installed files...", 0, "", false);
            using (var client = Client())
            {
                var manifest = new JavaScriptSerializer().Deserialize<UpdateManifest>(client.DownloadString(ManifestUrl));
                if (manifest == null || string.IsNullOrWhiteSpace(manifest.version) || string.IsNullOrWhiteSpace(manifest.packageUrl) || string.IsNullOrWhiteSpace(manifest.sha256)) throw new InvalidDataException("The update manifest is incomplete.");
                if (!manifest.packageUrl.StartsWith("https://github.com/Xitfin/RiftLegacy-Updates/releases/download/", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The update URL is not trusted.");
                return manifest;
            }
        }

        private void Install(UpdateManifest manifest, string installDirectory)
        {
            string work = Path.Combine(baseDirectory, ".rift-legacy-update");
            string archive = Path.Combine(work, "package.zip");
            string stage = Path.Combine(work, "stage");
            if (Directory.Exists(work)) Directory.Delete(work, true);
            Directory.CreateDirectory(work);
            using (var client = Client())
            {
                client.DownloadProgressChanged += delegate(object sender, DownloadProgressChangedEventArgs e) { SetState("UPDATE IN PROGRESS", "Downloading update...", e.ProgressPercentage, FormatSize(e.BytesReceived, e.TotalBytesToReceive), false); };
                client.DownloadFileTaskAsync(new Uri(manifest.packageUrl), archive).GetAwaiter().GetResult();
            }
            if (manifest.size > 0 && new FileInfo(archive).Length != manifest.size) throw new InvalidDataException("The downloaded package size is invalid.");
            SetState("UPDATE IN PROGRESS", "Verifying downloaded files...", 100, FormatSize(manifest.size, manifest.size), false);
            string actualHash; using (var stream = File.OpenRead(archive)) using (var sha = SHA256.Create()) actualHash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
            if (!string.Equals(actualHash, manifest.sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The downloaded package failed SHA-256 verification.");
            SetState("UPDATE IN PROGRESS", "Extracting game data...", 100, FormatSize(manifest.size, manifest.size), false);
            Directory.CreateDirectory(stage); ZipFile.ExtractToDirectory(archive, stage);
            string entryPoint = string.IsNullOrWhiteSpace(manifest.entryPoint) ? "Rift Legacy.exe" : manifest.entryPoint;
            if (!File.Exists(Path.Combine(stage, entryPoint))) throw new InvalidDataException("Rift Legacy.exe is missing from the package.");
            SetState("UPDATE IN PROGRESS", "Installing update...", 100, FormatSize(manifest.size, manifest.size), false);
            string backup = installDirectory + ".old";
            if (Directory.Exists(backup)) Directory.Delete(backup, true);
            if (Directory.Exists(installDirectory)) Directory.Move(installDirectory, backup);
            try
            {
                Directory.Move(stage, installDirectory);
                File.WriteAllText(Path.Combine(installDirectory, "version.json"), new JavaScriptSerializer().Serialize(new Dictionary<string, object> { { "version", manifest.version }, { "installedAt", DateTime.UtcNow.ToString("o") } }));
                if (Directory.Exists(backup)) Directory.Delete(backup, true);
            }
            catch
            {
                if (Directory.Exists(installDirectory)) Directory.Delete(installDirectory, true);
                if (Directory.Exists(backup)) Directory.Move(backup, installDirectory);
                throw;
            }
            try { Directory.Delete(work, true); } catch { }
        }

        private void SetState(string title, string sub, int value, string bytes, bool completed)
        {
            statusTitle = title; subStatus = sub; progress = Math.Max(0, Math.Min(100, value)); sizeLabel = bytes; done = completed; RefreshUi();
        }

        private void RefreshUi() { try { if (IsHandleCreated) BeginInvoke(new Action(Invalidate)); } catch { } }
        private static string FormatSize(long current, long total) { return total <= 0 ? "" : string.Format("{0:0.0} / {1:0.0} MB", current / 1048576d, total / 1048576d); }
        private static string ReadLocalVersion(string installDirectory) { try { var data = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(Path.Combine(installDirectory, "version.json"))); return data.ContainsKey("version") ? Convert.ToString(data["version"]) : ""; } catch { return ""; } }
        private static bool IsNewer(string remote, string local) { if (string.IsNullOrWhiteSpace(local)) return true; Version a, b; return Version.TryParse(remote, out a) && Version.TryParse(local, out b) ? a > b : !string.Equals(remote, local, StringComparison.OrdinalIgnoreCase); }
        private static WebClient Client() { var c = new WebClient(); c.Headers[HttpRequestHeader.UserAgent] = "RiftLegacyLauncher/1.0"; return c; }
        private static void Launch(string executable) { Process.Start(new ProcessStartInfo { FileName = executable, WorkingDirectory = Path.GetDirectoryName(executable), UseShellExecute = true }); }

        private void OnMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            if (closeRect.Contains(e.Location)) { if (!working || MessageBox.Show(this, "Close the launcher?", "Rift Legacy", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) Close(); return; }
            if (minimizeRect.Contains(e.Location)) { WindowState = FormWindowState.Minimized; return; }
            if (e.Y <= 36) { ReleaseCapture(); SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero); }
        }

        private void LoadResources()
        {
            LoadFont("Cinzel500"); LoadFont("Cinzel700"); LoadFont("Cinzel900"); LoadFont("Spectral400"); LoadFont("Spectral600");
            cinzel = FindFamily("Cinzel"); spectral = FindFamily("Spectral");
            try { using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("RiftIcon")) if (stream != null) iconImage = new Bitmap(stream); } catch { }
        }

        private void LoadFont(string resource)
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
            {
                if (stream == null) return; byte[] bytes = new byte[stream.Length]; stream.Read(bytes, 0, bytes.Length);
                IntPtr memory = Marshal.AllocCoTaskMem(bytes.Length); Marshal.Copy(bytes, 0, memory, bytes.Length); fonts.AddMemoryFont(memory, bytes.Length); Marshal.FreeCoTaskMem(memory);
            }
        }

        private FontFamily FindFamily(string name) { foreach (FontFamily f in fonts.Families) if (f.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0) return f; return FontFamily.GenericSerif; }
        private Font CinzelFont(float size, FontStyle style) { try { return new Font(cinzel, size, style, GraphicsUnit.Point); } catch { return new Font("Georgia", size, style); } }
        private Font SpectralFont(float size, FontStyle style) { try { return new Font(spectral, size, style, GraphicsUnit.Point); } catch { return new Font("Georgia", size, style); } }
        private static Font UiFont(float size) { return new Font("Segoe UI", size, FontStyle.Regular); }
        private static void DrawText(Graphics g, string text, Font font, Color color, RectangleF rect, StringAlignment horizontal, StringAlignment vertical) { using (font) using (var brush = new SolidBrush(color)) using (var format = new StringFormat { Alignment = horizontal, LineAlignment = vertical, Trimming = StringTrimming.EllipsisCharacter }) g.DrawString(text ?? "", font, brush, rect, format); }
        private void DrawGradientLogo(Graphics g, string text, RectangleF rect)
        {
            using (var font = CinzelFont(47f, FontStyle.Bold)) using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            using (var path = new GraphicsPath())
            {
                path.AddString(text, font.FontFamily, (int)font.Style, g.DpiY * font.Size / 72f, rect, format);
                using (var shadow = new Pen(Color.FromArgb(45, 200, 170, 110), 5f)) g.DrawPath(shadow, path);
                using (var brush = new LinearGradientBrush(rect, Color.FromArgb(245, 230, 184), Color.FromArgb(138, 108, 51), LinearGradientMode.Vertical)) g.FillPath(brush, path);
            }
        }
        private static GraphicsPath Rounded(Rectangle rect, int radius) { var p = new GraphicsPath(); int d = radius * 2; p.AddArc(rect.X, rect.Y, d, d, 180, 90); p.AddArc(rect.Right - d, rect.Y, d, d, 270, 90); p.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90); p.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90); p.CloseFigure(); return p; }
    }

    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            bool created; using (var mutex = new Mutex(true, "RiftLegacyLauncher.Singleton", out created))
            { if (!created) return; Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false); Application.Run(new LauncherForm()); }
        }
    }
}
