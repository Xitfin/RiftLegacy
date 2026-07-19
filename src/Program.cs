using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.IO.Compression;
using System.Globalization;
using System.Linq;
using System.Management;
using System.Net;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

[assembly: AssemblyTitle("Rift Legacy")]
[assembly: AssemblyProduct("Rift Legacy")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace ClassicSkinMorph
{
    internal enum LauncherState { Loading, Active, Error }

    internal sealed class LoadProgress
    {
        public int Percent;
        public string Status;
    }

    internal sealed class SweepCanvas : Control
    {
        public double Phase { get; set; }
        public SweepCanvas() { DoubleBuffered = true; }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Color.FromArgb(10, 20, 40));
            int virtualWidth = Width * 3;
            float offsetX = (float)(-Width * 2 + Phase * Width * 4);
            var virtualGradient = new Rectangle(0, 0, virtualWidth, Math.Max(1, Height));
            using (var brush = new LinearGradientBrush(virtualGradient, Color.FromArgb(10, 20, 40), Color.FromArgb(10, 20, 40), 0f))
            {
                brush.InterpolationColors = new ColorBlend {
                    Colors = new[] { Color.FromArgb(10, 20, 40), Color.FromArgb(201, 169, 97), Color.FromArgb(244, 227, 168), Color.FromArgb(201, 169, 97), Color.FromArgb(10, 20, 40) },
                    Positions = new[] { 0f, .45f, .50f, .55f, 1f }
                };
                brush.TranslateTransform(offsetX, 0f, MatrixOrder.Append);
                e.Graphics.FillRectangle(brush, ClientRectangle);
            }
        }
    }

    internal sealed class GlowCanvas : Control
    {
        public double Phase { get; set; }
        public GlowCanvas() { DoubleBuffered = true; BackColor = Color.FromArgb(10, 20, 40); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            double pulse = (Math.Sin(Phase * Math.PI * 2 - Math.PI / 2) + 1) / 2;
            int goldAlpha = 38 + (int)(102 * pulse); // 15% -> 55%
            var shadowBounds = new Rectangle(16, 22, Width - 32, Height - 36);
            using (var shadowPath = new GraphicsPath())
            {
                shadowPath.AddEllipse(shadowBounds);
                using (var shadow = new PathGradientBrush(shadowPath))
                {
                    shadow.CenterColor = Color.FromArgb(90, 0, 0, 0);
                    shadow.SurroundColors = new[] { Color.Transparent };
                    shadow.FocusScales = new PointF(.65f, .55f);
                    e.Graphics.FillPath(shadow, shadowPath);
                }
            }
            var glowBounds = new Rectangle(5, 8, Width - 10, Height - 16);
            using (var glowPath = new GraphicsPath())
            {
                glowPath.AddEllipse(glowBounds);
                using (var glow = new PathGradientBrush(glowPath))
                {
                    glow.CenterColor = Color.FromArgb(goldAlpha, 201, 169, 97);
                    glow.SurroundColors = new[] { Color.Transparent };
                    glow.FocusScales = new PointF(.62f, .50f);
                    e.Graphics.FillPath(glow, glowPath);
                }
            }
        }
    }

    internal sealed class StatusCanvas : Control
    {
        public string StatusText { get; set; }
        public LauncherState State { get; set; }
        public double Phase { get; set; }
        public StatusCanvas() { DoubleBuffered = true; BackColor = Color.FromArgb(10, 20, 40); Font = new Font("Segoe UI", 9f, FontStyle.Bold); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color color = State == LauncherState.Active ? Color.FromArgb(61, 220, 110) : State == LauncherState.Error ? Color.FromArgb(224, 90, 78) : Color.FromArgb(76, 141, 255);
            string text = StatusText ?? "";
            Size measured = TextRenderer.MeasureText(text, Font, new Size(int.MaxValue, Height), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            int dotSpace = 18, start = (Width - measured.Width - dotSpace) / 2;
            double wave = State == LauncherState.Active ? 0 : (Math.Sin(Phase * Math.PI * 2 - Math.PI / 2) + 1) / 2;
            float scale = 1f + (float)(.3 * wave);
            int alpha = State == LauncherState.Active ? 255 : 255 - (int)(166 * wave);
            float diameter = 6f * scale;
            using (var brush = new SolidBrush(Color.FromArgb(alpha, color))) e.Graphics.FillEllipse(brush, start, (Height - diameter) / 2f, diameter, diameter);
            TextRenderer.DrawText(e.Graphics, text, Font, new Point(start + dotSpace, (Height - measured.Height) / 2), color, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        }
    }

    internal sealed class ActiveCanvas : Control
    {
        public double Phase { get; set; }
        public ActiveCanvas() { DoubleBuffered = true; BackColor = Color.FromArgb(10, 20, 40); Font = new Font("Segoe UI", 9f, FontStyle.Bold); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            const string text = "YOU CAN NOW PLAY — CLASSIC & DEFAULT SKINS ACTIVE";
            Size measured = TextRenderer.MeasureText(text, Font, new Size(int.MaxValue, Height), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            int start = (Width - measured.Width - 18) / 2;
            float radius = 3.5f + (float)(10 * Phase);
            int alpha = (int)(128 * (1 - Phase));
            using (var ring = new Pen(Color.FromArgb(alpha, 61, 220, 110), 1.5f)) e.Graphics.DrawEllipse(ring, start + 3.5f - radius, Height / 2f - radius, radius * 2, radius * 2);
            using (var dot = new SolidBrush(Color.FromArgb(61, 220, 110))) e.Graphics.FillEllipse(dot, start, Height / 2f - 3.5f, 7, 7);
            TextRenderer.DrawText(e.Graphics, text, Font, new Point(start + 18, (Height - measured.Height) / 2), Color.FromArgb(61, 220, 110), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        }
    }

    internal sealed class HoverIconButton : Control
    {
        public double HoverAmount { get; private set; }
        private bool hovered;
        public HoverIconButton()
        {
            DoubleBuffered = true; Cursor = Cursors.Hand; TabStop = false;
            MouseEnter += delegate { hovered = true; };
            MouseLeave += delegate { hovered = false; };
        }
        public void AnimateStep()
        {
            double target = hovered ? 1.0 : 0.0;
            HoverAmount += (target - HoverAmount) * .22;
            if (Math.Abs(target - HoverAmount) < .01) HoverAmount = target;
            Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int gold = (int)(90 + 130 * HoverAmount);
            int fill = (int)(12 + 25 * HoverAmount);
            using (var background = new SolidBrush(Color.FromArgb(fill, 201, 169, 97)))
                e.Graphics.FillRectangle(background, new Rectangle(1, 1, Width - 3, Height - 3));
            using (var border = new Pen(Color.FromArgb(gold, 201, 169, 97), 1f + (float)HoverAmount))
                e.Graphics.DrawRectangle(border, new Rectangle(1, 1, Width - 3, Height - 3));
            Color textColor = Blend(Color.FromArgb(170, 180, 198), Color.FromArgb(244, 227, 168), HoverAmount);
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        }
        private static Color Blend(Color from, Color to, double amount)
        {
            return Color.FromArgb(
                (int)(from.R + (to.R - from.R) * amount),
                (int)(from.G + (to.G - from.G) * amount),
                (int)(from.B + (to.B - from.B) * amount));
        }
    }

    internal sealed class ProgressCanvas : Control
    {
        private int percent;
        private float displayedPercent;
        private LauncherState state;
        public double Phase { get; set; }
        public int Percent { get { return percent; } set { percent = Math.Max(0, Math.Min(100, value)); Invalidate(); } }
        public LauncherState State { get { return state; } set { state = value; Invalidate(); } }

        public ProgressCanvas()
        {
            DoubleBuffered = true;
            Font = new Font("Segoe UI", 11.5f, FontStyle.Bold);
            BackColor = Color.FromArgb(13, 26, 48);
        }

        public void AnimateStep()
        {
            displayedPercent += (percent - displayedPercent) * .16f;
            if (Math.Abs(percent - displayedPercent) < .1f) displayedPercent = percent;
            Invalidate();
        }

        public void Reset()
        {
            percent = 0;
            displayedPercent = 0;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var border = new Pen(Color.FromArgb(42, 58, 87)))
                e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
            var fill = state == LauncherState.Active ? Color.FromArgb(47, 190, 92) :
                       state == LauncherState.Error ? Color.FromArgb(212, 69, 58) : Color.FromArgb(46, 99, 224);
            int width = (int)((Width - 2) * displayedPercent / 100f);
            if (width > 0)
            {
                using (var brush = new SolidBrush(fill)) e.Graphics.FillRectangle(brush, 1, 1, width, Height - 2);
                if (state != LauncherState.Active)
                {
                    var oldClip = e.Graphics.Clip;
                    e.Graphics.SetClip(new Rectangle(1, 1, width, Height - 2));
                    int shimmerX = (int)(-Width * .4 + Phase * Width * 1.8);
                    Point[] band = { new Point(shimmerX, 1), new Point(shimmerX + 42, 1), new Point(shimmerX + 18, Height - 1), new Point(shimmerX - 24, Height - 1) };
                    using (var shimmer = new SolidBrush(Color.FromArgb(72, 255, 255, 255))) e.Graphics.FillPolygon(shimmer, band);
                    e.Graphics.Clip = oldClip;
                }
                if (state != LauncherState.Active)
                    using (var edge = new Pen(Color.FromArgb(212, 175, 55), 2)) e.Graphics.DrawLine(edge, width, 1, width, Height - 2);
            }
            string text = state == LauncherState.Error ? "ERROR" : percent + "%";
            TextRenderer.DrawText(e.Graphics, text, Font, ClientRectangle, Color.FromArgb(244, 246, 250),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    internal sealed class SessionRecord
    {
        public string DataRoot { get; set; }
        public List<BackupRecord> Backups { get; set; }
        public List<string> ModIds { get; set; }
    }

    internal sealed class BackupRecord
    {
        public string Path { get; set; }
        public string Backup { get; set; }
        public bool Existed { get; set; }
    }

    internal sealed class UserPreferences
    {
        public bool LoadingScreen { get; set; }
        public List<string> DisabledChampions { get; set; }
        public UserPreferences() { LoadingScreen = true; DisabledChampions = new List<string>(); }
    }

    internal sealed class SettingsForm : Form
    {
        private readonly CheckedListBox championsList;
        private readonly CheckBox loadingScreen;
        private readonly Dictionary<string, string> packageByChampion = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public UserPreferences Preferences { get; private set; }

        public SettingsForm(string root, UserPreferences current)
        {
            Text = "Rift Legacy Settings"; ClientSize = new Size(720, 690);
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent; BackColor = Color.FromArgb(4, 20, 28);
            ForeColor = Color.FromArgb(240, 230, 200); ShowInTaskbar = false;
            var title = new Label { Text = "SETTINGS", Location = new Point(26, 18), Size = new Size(650, 34),
                Font = new Font("Georgia", 17f, FontStyle.Bold), ForeColor = Color.FromArgb(240, 230, 200) };
            Controls.Add(title);
            Controls.Add(new Panel { Location = new Point(0, 64), Size = new Size(720, 2), BackColor = Color.FromArgb(120, 90, 40) });
            var skinsHeader = new Label { Text = "CLASSIC SKINS", Location = new Point(28, 82), Size = new Size(400, 28),
                Font = new Font("Georgia", 12f, FontStyle.Bold), ForeColor = Color.FromArgb(200, 170, 110) };
            Controls.Add(skinsHeader);
            var all = DarkButton("CHECK ALL", new Point(466, 78), new Size(102, 30));
            var none = DarkButton("UNCHECK ALL", new Point(578, 78), new Size(112, 30));
            Controls.Add(all); Controls.Add(none);
            championsList = new CheckedListBox { Location = new Point(28, 120), Size = new Size(662, 424),
                MultiColumn = true, ColumnWidth = 216, CheckOnClick = true, BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.FromArgb(7, 27, 36), ForeColor = Color.FromArgb(220, 225, 225), Font = new Font("Segoe UI", 9f) };
            string[] ordered = { "Ahri","Alistar","Amumu","Anivia","Annie","Ashe","Blitzcrank","Brand","Cho'Gath","Corki","Dr. Mundo","Evelynn","Ezreal","Fiddlesticks","Gangplank","Garen","Gragas","Heimerdinger","Janna","Jarvan IV","Jax","Karthus","Kassadin","Katarina","Kayle","Kog'Maw","Lee Sin","Leona","Lulu","Lux","Malphite","Malzahar","Master Yi","Miss Fortune","Morgana","Nasus","Nidalee","Nunu & Willump","Olaf","Pantheon","Rammus","Ryze","Shaco","Sion","Singed","Sivir","Skarner","Sona","Soraka","Taric","Teemo","Tristana","Tryndamere","Twisted Fate","Twitch","Vayne","Veigar","Warwick","Wukong","Zilean" };
            string mods = Path.Combine(root, "mods");
            foreach (string package in Directory.GetFiles(mods, "*.fantome").OrderBy(path => path))
            {
                string key = Path.GetFileNameWithoutExtension(package).ToLowerInvariant();
                if (IsLoadingPackage(key)) continue;
                string display = Regex.Replace(key, "-(classic|base)$", "", RegexOptions.IgnoreCase).Replace('-', ' ');
                display = System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(display);
                packageByChampion[NormalizeChampion(display)] = key;
            }
            foreach (string champion in ordered)
            {
                string key; packageByChampion.TryGetValue(NormalizeChampion(champion), out key);
                int index = championsList.Items.Add(champion);
                championsList.SetItemChecked(index, key == null || !current.DisabledChampions.Contains(key));
            }
            Controls.Add(championsList);
            all.Click += delegate { for (int i = 0; i < championsList.Items.Count; i++) championsList.SetItemChecked(i, true); };
            none.Click += delegate { for (int i = 0; i < championsList.Items.Count; i++) championsList.SetItemChecked(i, false); };
            var loadingHeader = new Label { Text = "LOADING SCREEN", Location = new Point(28, 558), Size = new Size(250, 26),
                Font = new Font("Georgia", 11f, FontStyle.Bold), ForeColor = Color.FromArgb(200, 170, 110) };
            loadingScreen = new CheckBox { Text = "Black background and Season 1 frame", Location = new Point(30, 590), Size = new Size(360, 28),
                Checked = current.LoadingScreen, ForeColor = Color.FromArgb(210, 215, 220), FlatStyle = FlatStyle.Flat };
            Controls.Add(loadingHeader); Controls.Add(loadingScreen);
            var save = DarkButton("SAVE", new Point(494, 636), new Size(92, 34)); save.DialogResult = DialogResult.OK;
            var cancel = DarkButton("CANCEL", new Point(598, 636), new Size(92, 34)); cancel.DialogResult = DialogResult.Cancel;
            Controls.Add(save); Controls.Add(cancel); AcceptButton = save; CancelButton = cancel;
            FormClosing += delegate {
                if (DialogResult != DialogResult.OK) return;
                var disabled = new List<string>();
                for (int i = 0; i < championsList.Items.Count; i++)
                {
                    string key; if (!championsList.GetItemChecked(i) && packageByChampion.TryGetValue(NormalizeChampion(Convert.ToString(championsList.Items[i])), out key)) disabled.Add(key);
                }
                Preferences = new UserPreferences { LoadingScreen = loadingScreen.Checked, DisabledChampions = disabled };
            };
        }

        private static Button DarkButton(string text, Point location, Size size)
        {
            var b = new Button { Text = text, Location = location, Size = size, FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(20, 28, 34), ForeColor = Color.FromArgb(200, 170, 110), Font = new Font("Segoe UI", 8f, FontStyle.Bold) };
            b.FlatAppearance.BorderColor = Color.FromArgb(120, 90, 40); return b;
        }
        private static string NormalizeChampion(string value) { return Regex.Replace(value.ToLowerInvariant(), "[^a-z0-9]", "").Replace("nunuandwillump", "nunu"); }
        internal static bool IsLoadingPackage(string key)
        {
            return key.StartsWith("loading-screen-");
        }
    }

    internal static class Json
    {
        private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 100 };
        public static Dictionary<string, object> ReadObject(string path)
        {
            return Serializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(path, Encoding.UTF8));
        }
        public static T Read<T>(string path) { return Serializer.Deserialize<T>(File.ReadAllText(path, Encoding.UTF8)); }
        public static void Write(string path, object value)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            File.WriteAllText(path, Serializer.Serialize(value), new UTF8Encoding(false));
        }
    }

    internal sealed class LtkService
    {
        private readonly string root;
        private readonly string sessionPath;
        private readonly string dataRoot;
        private readonly string backupRoot;
        private SessionRecord session;

        public LtkService(string applicationRoot)
        {
            root = applicationRoot;
            sessionPath = Path.Combine(root, "state", "ltk-session.json");
            dataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "dev.leaguetoolkit.manager");
            backupRoot = Path.Combine(root, "state", "ltk-backup");
        }

        public int Start(string championsDirectory, UserPreferences preferences, IProgress<LoadProgress> progress)
        {
            Restore();
            EnsureNoConflictingProcesses();
            string ltkExe = Path.Combine(root, "LTK Manager", "ltk-manager.exe");
            string modsRoot = Path.Combine(root, "mods");
            if (!File.Exists(ltkExe)) throw new InvalidOperationException("LTK engine not found.");
            const string season1Key = "loading-screen-season1-jade";
            string[] packages = Directory.Exists(modsRoot) ? Directory.GetFiles(modsRoot, "*.fantome").Where(path => {
                string key = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                if (key == season1Key) return preferences.LoadingScreen;
                if (key == "loading-screen-black") return preferences.LoadingScreen;
                if (key.StartsWith("loading-screen-")) return false;
                return !preferences.DisabledChampions.Contains(key);
            }).OrderBy(x => x).ToArray() : new string[0];
            if (packages.Length == 0) throw new InvalidOperationException("No .fantome packages were found.");

            string profileRoot = Path.Combine(dataRoot, "profiles", "default");
            Directory.CreateDirectory(Path.Combine(dataRoot, "archives"));
            Directory.CreateDirectory(Path.Combine(dataRoot, "mods"));
            Directory.CreateDirectory(profileRoot);
            Directory.CreateDirectory(backupRoot);
            string settingsPath = Path.Combine(dataRoot, "settings.json");
            string libraryPath = Path.Combine(dataRoot, "library.json");
            string overlayPath = Path.Combine(profileRoot, "overlay.json");

            session = new SessionRecord { DataRoot = dataRoot, Backups = new List<BackupRecord>(), ModIds = new List<string>() };
            // The selected PBE installation is the engine's persistent target. Restoring an
            // older settings.json on shutdown can silently switch LTK back to live League.
            foreach (string path in new[] { libraryPath, overlayPath })
            {
                string backup = Path.Combine(backupRoot, Path.GetFileName(path));
                bool existed = File.Exists(path);
                if (existed) File.Copy(path, backup, true);
                session.Backups.Add(new BackupRecord { Path = path, Backup = backup, Existed = existed });
            }
            SaveSession();

            try
            {
                var library = File.Exists(libraryPath) ? Json.ReadObject(libraryPath) : new Dictionary<string, object> { { "version", 1 }, { "mods", new object[0] } };
                var entries = ToObjectList(library.ContainsKey("mods") ? library["mods"] : null);
                for (int i = 0; i < packages.Length; i++)
                {
                    var metadata = ReadFantomeMetadata(packages[i]);
                    string id = Guid.NewGuid().ToString();
                    session.ModIds.Add(id);
                    SaveSession();
                    File.Copy(packages[i], Path.Combine(dataRoot, "archives", id + ".fantome"), true);
                    string configDirectory = Path.Combine(dataRoot, "mods", id);
                    Directory.CreateDirectory(configDirectory);
                    string displayName = GetString(metadata, "Name", Path.GetFileNameWithoutExtension(packages[i]));
                    var config = new Dictionary<string, object> {
                        { "name", Slug(displayName) }, { "display_name", displayName },
                        { "version", GetString(metadata, "Version", "1.0") },
                        { "description", GetString(metadata, "Description", "") },
                        { "authors", new[] { GetString(metadata, "Author", "Unknown") } },
                        { "layers", new object[] { new Dictionary<string, object> { { "name", "base" }, { "priority", 0 }, { "description", "Base layer of the mod" } } } }
                    };
                    Json.Write(Path.Combine(configDirectory, "mod.config.json"), config);
                    entries.Add(new Dictionary<string, object> { { "id", id }, { "installedAt", DateTime.UtcNow.ToString("o") }, { "format", "fantome" } });
                    progress.Report(new LoadProgress { Percent = Math.Max(1, 30 * (i + 1) / packages.Length), Status = "LOADING CLASSIC SKINS..." });
                }
                library["mods"] = entries.ToArray();
                // LTK 1.11 reads enabled mods from the active library profile. Merely adding
                // archives (or writing overlay.json) imports them but leaves them disabled.
                var profiles = ToObjectList(library.ContainsKey("profiles") ? library["profiles"] : null);
                string activeProfileId = library.ContainsKey("activeProfileId") ? Convert.ToString(library["activeProfileId"]) : "";
                foreach (object profileValue in profiles)
                {
                    var profile = profileValue as Dictionary<string, object>;
                    if (profile == null) continue;
                    string profileId = profile.ContainsKey("id") ? Convert.ToString(profile["id"]) : "";
                    if (activeProfileId.Length == 0 || string.Equals(profileId, activeProfileId, StringComparison.OrdinalIgnoreCase))
                    {
                        profile["enabledMods"] = session.ModIds.ToArray();
                        profile["modOrder"] = session.ModIds.ToArray();
                        break;
                    }
                }
                library["profiles"] = profiles.ToArray();
                Json.Write(libraryPath, library);
                Json.Write(overlayPath, new Dictionary<string, object> {
                    { "version", 5 }, { "enabledMods", session.ModIds.ToArray() },
                    { "modFingerprints", new Dictionary<string, object>() }, { "gameFingerprint", 0 },
                    { "blockedWads", new[] { "map22.wad.client", "scripts.wad.client" } },
                    { "stringOverrideLocales", new object[0] }, { "wadFingerprints", new Dictionary<string, object>() },
                    { "linkedBinOffenders", new object[0] }
                });
                WriteSettings(settingsPath, championsDirectory);
                SaveSession();
                Process.Start(new ProcessStartInfo {
                    FileName = ltkExe, WorkingDirectory = Path.GetDirectoryName(ltkExe),
                    UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden
                });
                return packages.Length;
            }
            catch { Restore(); throw; }
        }

        public void Restore()
        {
            Kill("ltk-manager"); Kill("ltk_patcher_host"); Kill("cslol-host");
            if (!File.Exists(sessionPath)) return;
            try { session = Json.Read<SessionRecord>(sessionPath); } catch { return; }
            Thread.Sleep(300);
            foreach (var item in session.Backups ?? new List<BackupRecord>())
            {
                try { if (item.Existed && File.Exists(item.Backup)) File.Copy(item.Backup, item.Path, true); else if (!item.Existed && File.Exists(item.Path)) File.Delete(item.Path); } catch { }
            }
            foreach (string id in session.ModIds ?? new List<string>())
            {
                TryDeleteFile(Path.Combine(session.DataRoot, "archives", id + ".fantome"));
                TryDeleteDirectory(Path.Combine(session.DataRoot, "mods", id));
            }
            TryDeleteFile(sessionPath);
            TryDeleteDirectory(backupRoot);
            session = null;
        }

        public void ResetForReload()
        {
            Restore();
            // Reset the active UI output and overlay metadata, but preserve
            // unchanged multi-gigabyte WADs (especially Map11). Deleting the
            // whole overlay makes LTK rebuild every map and can exhaust its
            // process before the patcher host starts.
            string profileRoot = Path.Combine(dataRoot, "profiles", "default");
            TryDeleteFile(Path.Combine(profileRoot, "overlay", "DATA", "FINAL", "UI.wad.client"));
            TryDeleteFile(Path.Combine(profileRoot, "override_meta.bin"));
            TryDeleteFile(Path.Combine(profileRoot, "overlay.json"));
            TryDeleteFile(Path.Combine(dataRoot, ".overlay-build-version"));
        }

        public bool IsReady
        {
            get
            {
                return Process.GetProcessesByName("cslol-host").Any(p => !p.HasExited)
                    || Process.GetProcessesByName("ltk_patcher_host").Any(p => !p.HasExited);
            }
        }
        public void HideManagerWindow()
        {
            foreach (var process in Process.GetProcessesByName("ltk-manager"))
            {
                try { if (process.MainWindowHandle != IntPtr.Zero) ShowWindowAsync(process.MainWindowHandle, 0); } catch { }
            }
        }

        private void EnsureNoConflictingProcesses()
        {
            if (Process.GetProcessesByName("ltk-manager").Length + Process.GetProcessesByName("cslol-host").Length + Process.GetProcessesByName("ltk_patcher_host").Length > 0)
                throw new InvalidOperationException("Close LTK Manager before starting Rift Legacy.");
            using (var searcher = new ManagementObjectSearcher("SELECT ProcessId FROM Win32_Process WHERE Name='League of Legends.exe'"))
                if (searcher.Get().Count > 0)
                    throw new InvalidOperationException("League processes are still running or stuck after a crash. Restart Windows, then start Rift Legacy before League.");
        }

        private void WriteSettings(string path, string championsDirectory)
        {
            string leaguePath = Path.GetFullPath(Path.Combine(championsDirectory, "..", "..", ".."));
            Dictionary<string, object> settings = File.Exists(path) ? Json.ReadObject(path) : DefaultSettings(leaguePath);
            settings["leaguePath"] = leaguePath.Replace('\\', '/'); settings["firstRunComplete"] = true;
            settings["minimizeToTray"] = true; settings["startInTray"] = true;
            settings["startInTrayUnlessUpdate"] = false; settings["alwaysStartPatcher"] = true;
            Json.Write(path, settings);
        }

        private static Dictionary<string, object> DefaultSettings(string path)
        {
            return new Dictionary<string, object> {
                {"leaguePath",path.Replace('\\','/')},{"modStoragePath",null},{"workshopPath",null},{"firstRunComplete",true},{"theme","system"},
                {"accentColor",new Dictionary<string,object>{{"preset",null},{"customHue",null}}},{"backdropImage",null},{"backdropBlur",null},
                {"libraryViewMode",null},{"patchTft",false},{"minimizeToTray",true},{"startInTray",true},{"autoRun",false},
                {"startInTrayUnlessUpdate",false},{"alwaysStartPatcher",true},{"migrationDismissed",false},{"reloadModsHotkey",null},
                {"killLeagueHotkey",null},{"killLeagueStopsPatcher",true},{"trustedDomains",new[]{"runeforge.dev","divineskins.gg"}},
                {"watcherEnabled",false},{"blockScriptsWad",true},{"linkedBinCheckEnabled",true},{"wadBlocklist",new object[0]},
                {"authorProfiles",new object[0]},{"defaultAuthorProfileId",null},{"hasSeenHddWarning",true},{"elevateInjector",false},
                {"autoCategorizationEnabled",true},{"enforceSkinhackScan",true},{"applyStringOverridesToAllLocales",false}
            };
        }

        private void SaveSession() { Json.Write(sessionPath, session); }
        private static List<object> ToObjectList(object value)
        {
            var list = new List<object>(); var enumerable = value as IEnumerable;
            if (enumerable != null && !(value is string)) foreach (object item in enumerable) list.Add(item);
            return list;
        }
        private static Dictionary<string, object> ReadFantomeMetadata(string path)
        {
            using (var archive = ZipFile.OpenRead(path))
            {
                var entry = archive.GetEntry("META/info.json");
                if (entry == null) throw new InvalidOperationException("META/info.json is missing from " + Path.GetFileName(path));
                using (var reader = new StreamReader(entry.Open(), Encoding.UTF8))
                    return new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(reader.ReadToEnd());
            }
        }
        private static string GetString(Dictionary<string, object> map, string key, string fallback) { object value; return map.TryGetValue(key, out value) && value != null ? Convert.ToString(value) : fallback; }
        private static string Slug(string value) { return Regex.Replace(value.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-'); }
        private static void Kill(string name) { foreach (var p in Process.GetProcessesByName(name)) try { p.Kill(); p.WaitForExit(1000); } catch { } }
        private static void TryDeleteFile(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }
        private static void TryDeleteDirectory(string path) { try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { } }
        [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);
    }

    internal sealed class GamePlayerInfo
    {
        public string RiotId { get; set; }
        public string Champion { get; set; }
        public string Rank { get; set; }
        public string Team { get; set; }
    }

    internal sealed class GamePlayersForm : Form
    {
        public GamePlayersForm(IEnumerable<GamePlayerInfo> players)
        {
            Text = "Players in Game";
            ClientSize = new Size(900, 430);
            MinimumSize = new Size(760, 420);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.FromArgb(10, 20, 40);
            ForeColor = Color.FromArgb(244, 246, 250);
            Font = new Font("Segoe UI", 9f);

            var titleBlue = Header("BLUE SIDE", Color.FromArgb(45, 165, 235));
            var titleRed = Header("RED SIDE", Color.FromArgb(220, 75, 75));
            var blue = PlayerList();
            var red = PlayerList();
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 2, RowCount = 2 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.Controls.Add(titleBlue, 0, 0); layout.Controls.Add(titleRed, 1, 0);
            layout.Controls.Add(blue, 0, 1); layout.Controls.Add(red, 1, 1);
            Controls.Add(layout);

            foreach (GamePlayerInfo player in players)
                (string.Equals(player.Team, "ORDER", StringComparison.OrdinalIgnoreCase) ? blue : red).Controls.Add(PlayerCard(player));
        }

        private static Label Header(string text, Color color)
        {
            return new Label { Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = color, Font = new Font("Segoe UI", 12f, FontStyle.Bold) };
        }

        private static FlowLayoutPanel PlayerList()
        {
            return new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false,
                AutoScroll = true, Padding = new Padding(6), BackColor = Color.FromArgb(8, 16, 32) };
        }

        private static Panel PlayerCard(GamePlayerInfo player)
        {
            string[] rankLines = (player.Rank ?? "Unknown").Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            string rankName = rankLines.Length > 0 ? rankLines[0] : "Unknown";
            string lp = rankLines.Length > 1 ? rankLines[1].Trim('(', ')') : "";
            string stats = rankLines.Length > 2 ? rankLines[2].Replace("W / ", " WINS  |  ").Replace("L | ", " LOSSES  |  ") : "";
            Color accent = string.Equals(player.Team, "ORDER", StringComparison.OrdinalIgnoreCase)
                ? Color.FromArgb(45, 165, 235) : Color.FromArgb(220, 75, 75);
            var card = new Panel { Width = 400, Height = 92, Margin = new Padding(4, 5, 4, 5), BackColor = Color.FromArgb(14, 29, 53) };
            card.Paint += delegate(object sender, PaintEventArgs e) {
                using (var border = new Pen(Color.FromArgb(45, 64, 94))) e.Graphics.DrawRectangle(border, 0, 0, card.Width - 1, card.Height - 1);
                using (var side = new SolidBrush(accent)) e.Graphics.FillRectangle(side, 0, 0, 4, card.Height);
                using (var separator = new Pen(Color.FromArgb(34, 50, 78))) e.Graphics.DrawLine(separator, 14, 61, card.Width - 14, 61);
            };
            var name = new Label { Location = new Point(16, 9), Size = new Size(225, 22), Text = player.RiotId,
                BackColor = Color.Transparent, ForeColor = Color.FromArgb(244, 227, 168), Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) };
            var champion = new Label { Location = new Point(16, 34), Size = new Size(225, 20), Text = player.Champion.ToUpperInvariant(),
                BackColor = Color.Transparent, ForeColor = Color.FromArgb(145, 164, 192), Font = new Font("Segoe UI", 8f, FontStyle.Bold) };
            var rank = new Label { Location = new Point(240, 7), Size = new Size(146, 24), Text = rankName.ToUpperInvariant(),
                BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleRight, ForeColor = Color.FromArgb(201, 169, 97),
                Font = new Font("Segoe UI", 10f, FontStyle.Bold) };
            var points = new Label { Location = new Point(240, 34), Size = new Size(146, 18), Text = lp,
                BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleRight, ForeColor = Color.FromArgb(185, 196, 214),
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold) };
            var record = new Label { Location = new Point(16, 67), Size = new Size(370, 18), Text = stats,
                BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleRight, ForeColor = Color.FromArgb(170, 180, 198),
                Font = new Font("Segoe UI", 8f, FontStyle.Bold) };
            card.Controls.Add(name); card.Controls.Add(champion); card.Controls.Add(rank); card.Controls.Add(points); card.Controls.Add(record);
            return card;
        }
    }

    internal sealed class MainForm : Form
    {
        private readonly string root;
        private readonly LtkService ltk;
        private readonly StatusCanvas status;
        private readonly ActiveCanvas active;
        private readonly ProgressCanvas progressBar;
        private readonly SweepCanvas accent;
        private readonly GlowCanvas logoGlow;
        private readonly PictureBox logo;
        private readonly HoverIconButton launchButton;
        private readonly HoverIconButton githubButton;
        private readonly HoverIconButton playersButton;
        private readonly HoverIconButton settingsButton;
        private readonly System.Windows.Forms.Timer monitorTimer;
        private readonly System.Windows.Forms.Timer gameStateTimer;
        private readonly System.Windows.Forms.Timer animationTimer;
        private readonly Stopwatch animationClock;
        private readonly Stopwatch loadingClock = new Stopwatch();
        private LauncherState state = LauncherState.Loading;
        private int packageCount;
        private int logStartLine;
        private string logPath;
        private bool sessionStarted;
        private bool isLoading;
        private readonly string preferencesPath;
        private UserPreferences preferences;

        public MainForm()
        {
            root = AppDomain.CurrentDomain.BaseDirectory;
            ltk = new LtkService(root);
            preferencesPath = Path.Combine(root, "state", "user-preferences.json");
            preferences = LoadPreferences();
            Text = "Classic Skin Morph";
            if (!string.Equals(Path.GetFileName(Application.ExecutablePath), "ClassicSkinMorph.exe", StringComparison.OrdinalIgnoreCase))
                Text += " - RANK TEST";
            ClientSize = new Size(560, 307);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(10, 20, 40);
            ForeColor = Color.FromArgb(244, 246, 250);
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

            accent = new SweepCanvas { Location = new Point(0, 0), Size = new Size(560, 3) };
            Controls.Add(accent);

            launchButton = new HoverIconButton { Location = new Point(12, 12), Size = new Size(72, 28), Text = "LOAD", Font = new Font("Segoe UI", 8.5f, FontStyle.Bold) };
            githubButton = new HoverIconButton { Location = new Point(12, 45), Size = new Size(72, 28), Text = "GITHUB", Font = new Font("Segoe UI", 8.5f, FontStyle.Bold) };
            playersButton = new HoverIconButton { Location = new Point(12, 78), Size = new Size(72, 28), Text = "PLAYERS", Font = new Font("Segoe UI", 8.5f, FontStyle.Bold), Enabled = false };
            settingsButton = new HoverIconButton { Location = new Point(516, 12), Size = new Size(32, 28), Text = "⚙", Font = new Font("Segoe UI Symbol", 13f, FontStyle.Regular) };
            Controls.Add(launchButton);
            Controls.Add(githubButton);
            Controls.Add(playersButton);
            Controls.Add(settingsButton);
            launchButton.Click += async delegate { await BeginLoading(); };
            githubButton.Click += delegate {
                try { Process.Start(new ProcessStartInfo("https://github.com/Xitfin/ClassicSkinMorph") { UseShellExecute = true }); }
                catch { }
            };
            playersButton.Click += PlayersClicked;
            settingsButton.Click += SettingsClicked;

            string logoPath = Path.Combine(root, "assets", "classic-skin-morph-logo.png");
            logoGlow = new GlowCanvas { Location = new Point(130, 0), Size = new Size(300, 142) };
            Controls.Add(logoGlow);
            logo = new PictureBox { Location = new Point(20, 16), Size = new Size(260, 110), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent };
            if (File.Exists(logoPath))
            {
                using (var source = Image.FromFile(logoPath)) logo.Image = new Bitmap(source);
            }
            logoGlow.Controls.Add(logo);

            status = new StatusCanvas { Location = new Point(50, 188), Size = new Size(460, 20), StatusText = "READY TO LOAD", State = LauncherState.Loading };
            Controls.Add(status);

            progressBar = new ProgressCanvas { Location = new Point(70, 211), Size = new Size(420, 32), Percent = 0, State = LauncherState.Loading };
            Controls.Add(progressBar);

            active = new ActiveCanvas { Location = new Point(50, 243), Size = new Size(460, 20) };
            active.Visible = false;
            Controls.Add(active);

            var about = new RichTextBox {
                Location = new Point(60, 134), Size = new Size(440, 44), BorderStyle = BorderStyle.None,
                BackColor = Color.FromArgb(10, 20, 40), ForeColor = Color.FromArgb(170, 180, 198),
                Font = new Font("Segoe UI", 8.5f), ReadOnly = true, TabStop = false,
                ScrollBars = RichTextBoxScrollBars.None, DetectUrls = false
            };
            about.Text = "Classic Skin Morph is a free and open-source tool based on LTK Manager. Designed for nostalgic players, it restores legacy assets to transform your game and let you relive the classic Rift experience !";
            about.SelectAll(); about.SelectionAlignment = HorizontalAlignment.Center;
            foreach (string emphasized in new[] { "Classic Skin Morph", "LTK Manager" })
            {
                int index = about.Text.IndexOf(emphasized, StringComparison.Ordinal);
                if (index >= 0) { about.Select(index, emphasized.Length); about.SelectionFont = new Font("Segoe UI", 8.5f, FontStyle.Bold); }
            }
            about.Select(0, 0);
            Controls.Add(about);

            Controls.Add(new Panel { Location = new Point(70, 268), Size = new Size(420, 1), BackColor = Color.FromArgb(35, 50, 80) });
            var patchTitle = MakeLabel(new Point(50, 277), new Size(460, 16), 8.5f, FontStyle.Bold, ContentAlignment.MiddleCenter, Color.FromArgb(212, 175, 55));
            patchTitle.Text = "▼ PATCH NOTE V1.0";
            patchTitle.Cursor = Cursors.Hand;
            Controls.Add(patchTitle);
            var notes = MakeLabel(new Point(90, 303), new Size(400, 82), 8.5f, FontStyle.Regular, ContentAlignment.TopLeft, Color.FromArgb(170, 180, 198));
            notes.Text = "- Authentic Season 1 loading screen and classic UI\r\n- In-game Blue Side and Red Side player roster\r\n- Classic splash arts, HUD icons, and legacy models\r\n- Improved PBE compatibility and LTK integration";
            notes.Visible = false;
            Controls.Add(notes);
            bool patchExpanded = false;
            EventHandler togglePatch = delegate {
                patchExpanded = !patchExpanded;
                notes.Visible = patchExpanded;
                patchTitle.Text = (patchExpanded ? "▲" : "▼") + " PATCH NOTE V1.0";
                ClientSize = new Size(560, patchExpanded ? 397 : 307);
            };
            patchTitle.Click += togglePatch;

            // WinForms inserts newly added controls at the front of the Z-order.
            // Keep the animated glow behind the static, sharp logo and all text.
            logoGlow.SendToBack();
            logo.BringToFront();
            status.BringToFront();
            accent.BringToFront();
            launchButton.BringToFront();
            githubButton.BringToFront();
            playersButton.BringToFront();
            settingsButton.BringToFront();

            monitorTimer = new System.Windows.Forms.Timer { Interval = 500 };
            monitorTimer.Tick += TimerTick;
            gameStateTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            gameStateTimer.Tick += delegate { playersButton.Enabled = IsLiveGameStarted(); };
            gameStateTimer.Start();
            animationClock = Stopwatch.StartNew();
            animationTimer = new System.Windows.Forms.Timer { Interval = 40 };
            animationTimer.Tick += AnimationTick;
            animationTimer.Start();
            FormClosing += OnClosing;
        }

        private async Task BeginLoading()
        {
            if (isLoading) return;
            isLoading = true; launchButton.Enabled = false;
            try
            {
                sessionStarted = false;
                packageCount = 0;
                monitorTimer.Interval = 250;
                progressBar.Reset();
                if (launchButton.Text == "RELOAD")
                    await Task.Run(() => ltk.ResetForReload());
                string champions = EnsurePbeConfiguration();
                if (champions == null) { Close(); return; }
                HideEnemySummonerEmotes(champions);
                logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "dev.leaguetoolkit.manager", "logs", "ltk-manager." + DateTime.Now.ToString("yyyy-MM-dd") + ".log");
                logStartLine = File.Exists(logPath) ? File.ReadLines(logPath).Count() : 0;
                SetState(LauncherState.Loading, "LOADING CLASSIC SKINS...", 0);
                loadingClock.Restart();
                monitorTimer.Start();
                // LTK reports package-import progress up to 30%, then starts the much longer
                // overlay build. Late 30% callbacks must never move our visual fallback back.
                var reporter = new Progress<LoadProgress>(p => SetState(
                    LauncherState.Loading,
                    "LOADING CLASSIC SKINS...",
                    Math.Max(progressBar.Percent, p.Percent)));
                packageCount = await Task.Run(() => ltk.Start(champions, preferences, reporter));
                sessionStarted = true;
                status.StatusText = "LOADING CLASSIC SKINS...";
                launchButton.Text = "RELOAD";
            }
            catch (Exception ex) { SetState(LauncherState.Error, ex.Message.ToUpperInvariant(), progressBar.Percent); }
            finally { isLoading = false; launchButton.Enabled = true; }
        }

        private UserPreferences LoadPreferences()
        {
            try {
                if (File.Exists(preferencesPath)) {
                    var loaded = Json.Read<UserPreferences>(preferencesPath);
                    if (loaded.DisabledChampions == null) loaded.DisabledChampions = new List<string>();
                    return loaded;
                }
            } catch { }
            return new UserPreferences();
        }

        private void SettingsClicked(object sender, EventArgs e)
        {
            using (var dialog = new SettingsForm(root, preferences))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Preferences == null) return;
                preferences = dialog.Preferences; Json.Write(preferencesPath, preferences);
                launchButton.Text = sessionStarted ? "RELOAD" : "LOAD";
                status.StatusText = sessionStarted ? "SETTINGS CHANGED — CLICK RELOAD" : "READY TO LOAD";
                status.State = LauncherState.Loading; active.Visible = false;
            }
        }

        private async void PlayersClicked(object sender, EventArgs e)
        {
            if (!IsLiveGameStarted()) return;
            playersButton.Enabled = false;
            string previousText = playersButton.Text;
            playersButton.Text = "...";
            try
            {
                List<GamePlayerInfo> players = await Task.Run(() => ReadGamePlayers());
                if (players.Count == 0)
                {
                    MessageBox.Show(this, "The player list is not available yet.", "Classic Skin Morph", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                using (var dialog = new GamePlayersForm(players)) dialog.ShowDialog(this);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Unable to read the current game.\r\n\r\n" + ex.Message, "Classic Skin Morph", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                playersButton.Text = previousText;
                playersButton.Enabled = IsLiveGameStarted();
            }
        }

        private static bool IsLiveGameStarted()
        {
            // The live-client HTTPS endpoint can reject HttpWebRequest during its
            // TLS warm-up even though the game is already running. The game
            // process is the reliable availability signal; the click handler
            // still reports a clear message if player data is not ready yet.
            return Process.GetProcessesByName("League of Legends").Length > 0;
        }

        private List<GamePlayerInfo> ReadGamePlayers()
        {
            object raw = JsonValue("https://127.0.0.1:2999/liveclientdata/playerlist", null);
            var rows = raw as object[];
            if (rows == null) rows = (raw as ArrayList ?? new ArrayList()).Cast<object>().ToArray();
            string lcuBase = null, authorization = null;
            TryReadLcuConnection(out lcuBase, out authorization);
            string localRiotId = null, localRank = null;
            if (!string.IsNullOrEmpty(lcuBase))
            {
                try
                {
                    var current = JsonText(lcuBase + "/lol-summoner/v1/current-summoner", authorization);
                    localRiotId = Field(current, "gameName", "") + "#" + Field(current, "tagLine", "");
                    localRank = FormatJadeRank(JsonText(lcuBase + "/lol-ranked/v1/current-ranked-stats", authorization));
                }
                catch { }
            }
            var result = new List<GamePlayerInfo>();
            foreach (object item in rows)
            {
                var row = item as Dictionary<string, object>;
                if (row == null) continue;
                string riotId = Field(row, "riotId", Field(row, "summonerName", "Unknown player"));
                result.Add(new GamePlayerInfo {
                    RiotId = riotId,
                    Champion = Field(row, "championName", "Unknown champion"),
                    Team = Field(row, "team", "CHAOS"),
                    Rank = "Unknown"
                });
            }
            if (!string.IsNullOrEmpty(lcuBase))
            {
                string apiBase = lcuBase, apiAuthorization = authorization;
                string ownRiotId = localRiotId, ownRank = localRank;
                Parallel.ForEach(result, new ParallelOptions { MaxDegreeOfParallelism = 5 }, player => {
                    try
                    {
                        player.Rank = string.Equals(player.RiotId, ownRiotId, StringComparison.OrdinalIgnoreCase)
                            ? (ownRank ?? "Unknown")
                            : ReadJadeRank(apiBase, apiAuthorization, player.RiotId);
                    }
                    catch { player.Rank = "Unknown"; }
                });
            }
            return result;
        }

        private void TryReadLcuConnection(out string baseUrl, out string authorization)
        {
            baseUrl = null; authorization = null;
            try
            {
                string configPath = Path.Combine(root, "config.json");
                var config = Json.ReadObject(configPath);
                object value;
                if (!config.TryGetValue("pbeChampionsDirectory", out value)) return;
                string installRoot = Path.GetFullPath(Path.Combine(Convert.ToString(value), "..", "..", "..", ".."));
                string lockfile = Path.Combine(installRoot, "lockfile");
                if (!File.Exists(lockfile)) return;
                string lockText;
                using (var stream = new FileStream(lockfile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new StreamReader(stream, Encoding.UTF8)) lockText = reader.ReadToEnd();
                string[] parts = lockText.Split(':');
                if (parts.Length < 5) return;
                baseUrl = "https://127.0.0.1:" + parts[2];
                authorization = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("riot:" + parts[3]));
            }
            catch { baseUrl = null; authorization = null; }
        }

        private static string ReadJadeRank(string baseUrl, string authorization, string riotId)
        {
            var summoner = JsonText(baseUrl + "/lol-summoner/v1/summoners?name=" + Uri.EscapeDataString(riotId), authorization);
            string puuid = Field(summoner, "puuid", "");
            if (string.IsNullOrEmpty(puuid)) return "Unknown";
            var ranked = JsonText(baseUrl + "/lol-ranked/v1/ranked-stats/" + Uri.EscapeDataString(puuid), authorization);
            return FormatJadeRank(ranked);
        }

        private static string FormatJadeRank(Dictionary<string, object> ranked)
        {
            object queueMapRaw;
            if (!ranked.TryGetValue("queueMap", out queueMapRaw)) return "Salt";
            var queueMap = queueMapRaw as Dictionary<string, object>;
            if (queueMap == null) return "Salt";
            object jadeRaw;
            if (!queueMap.TryGetValue("JADE_RANKED_SOLO_5x5", out jadeRaw))
                jadeRaw = queueMap.FirstOrDefault(pair => pair.Key.IndexOf("JADE", StringComparison.OrdinalIgnoreCase) >= 0).Value;
            var jade = jadeRaw as Dictionary<string, object>;
            if (jade == null) return "Salt";
            string tier = Field(jade, "tier", Field(jade, "rankedTier", "Salt"));
            string division = Field(jade, "division", Field(jade, "rankedDivision", ""));
            tier = string.IsNullOrWhiteSpace(tier) ? "Salt" : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(tier.ToLowerInvariant());
            string rank = string.IsNullOrWhiteSpace(division) || string.Equals(division, "NA", StringComparison.OrdinalIgnoreCase)
                ? tier
                : tier + " " + division.ToUpperInvariant();
            object lpValue;
            int lp;
            if (jade.TryGetValue("leaguePoints", out lpValue) && int.TryParse(Convert.ToString(lpValue), out lp))
                rank += "\r\n(" + lp + " LP)";
            object winsValue, lossesValue;
            int wins, losses;
            if (jade.TryGetValue("wins", out winsValue) && jade.TryGetValue("losses", out lossesValue)
                && int.TryParse(Convert.ToString(winsValue), out wins) && int.TryParse(Convert.ToString(lossesValue), out losses))
            {
                int games = wins + losses;
                double winRate = games > 0 ? wins * 100.0 / games : 0;
                rank += "\r\n" + wins + "W / " + losses + "L | " + winRate.ToString("0.0", CultureInfo.InvariantCulture) + "% WR";
            }
            return rank;
        }

        private static Dictionary<string, object> JsonText(string url, string authorization)
        {
            return JsonValue(url, authorization) as Dictionary<string, object> ?? new Dictionary<string, object>();
        }

        private static object JsonValue(string url, string authorization)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            ServicePointManager.ServerCertificateValidationCallback = delegate { return true; };
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Timeout = 2500;
            request.ReadWriteTimeout = 2500;
            if (!string.IsNullOrEmpty(authorization)) request.Headers[HttpRequestHeader.Authorization] = authorization;
            using (var response = request.GetResponse())
            using (var reader = new StreamReader(response.GetResponseStream()))
                return new JavaScriptSerializer().DeserializeObject(reader.ReadToEnd());
        }

        private static string Field(Dictionary<string, object> row, string key, string fallback)
        {
            object value;
            return row != null && row.TryGetValue(key, out value) && value != null ? Convert.ToString(value) : fallback;
        }

        private void TimerTick(object sender, EventArgs e)
        {
            ltk.HideManagerWindow();
            if (ltk.IsReady)
            {
                SetState(LauncherState.Active, "CLASSIC SKINS LOADED", 100);
                monitorTimer.Interval = 1000;
                return;
            }
            if (loadingClock.IsRunning && progressBar.Percent >= 30)
            {
                int visualProgress = 30 + (int)Math.Min(62, loadingClock.Elapsed.TotalSeconds * 62.0 / 60.0);
                SetState(LauncherState.Loading, "LOADING CLASSIC SKINS...", Math.Max(progressBar.Percent, visualProgress));
            }
            if (!sessionStarted || packageCount <= 0)
            {
                return;
            }
            try
            {
                int built = File.Exists(logPath)
                    ? File.ReadLines(logPath).Skip(logStartLine).Count(line => line.Contains("Patched WAD complete"))
                    : 0;
                built = Math.Min(packageCount, built);
                int reportedValue = 30 + 65 * built / packageCount;
                // Some LTK builds no longer append overlay progress to the daily log.
                // Keep the UI moving while the engine rebuilds, but never claim completion
                // before cslol-host/ltk_patcher_host is actually ready.
                int fallbackValue = 30 + (int)Math.Min(62, loadingClock.Elapsed.TotalSeconds * 62.0 / 90.0);
                int value = Math.Min(95, Math.Max(progressBar.Percent, Math.Max(reportedValue, fallbackValue)));
                SetState(LauncherState.Loading, "LOADING CLASSIC SKINS...", value);
            }
            catch { }
        }

        private void AnimationTick(object sender, EventArgs e)
        {
            double seconds = animationClock.Elapsed.TotalSeconds;
            accent.Phase = (seconds % 3.2) / 3.2;
            logoGlow.Phase = (seconds % 2.6) / 2.6;
            status.Phase = (seconds % 1.4) / 1.4;
            progressBar.Phase = (seconds % 1.8) / 1.8;
            active.Phase = (seconds % 1.6) / 1.6;
            accent.Invalidate(); logoGlow.Invalidate(); status.Invalidate(); active.Invalidate();
            progressBar.AnimateStep();
            launchButton.AnimateStep();
            githubButton.AnimateStep();
            playersButton.AnimateStep();
            settingsButton.AnimateStep();
        }

        private void SetState(LauncherState newState, string message, int percent)
        {
            state = newState;
            progressBar.State = newState;
            progressBar.Percent = newState == LauncherState.Active ? 100 : percent;
            if (newState == LauncherState.Active)
            {
                status.StatusText = "CLASSIC SKINS LOADED";
                status.State = LauncherState.Active;
                active.Visible = true;
            }
            else if (newState == LauncherState.Error)
            {
                status.StatusText = message;
                status.State = LauncherState.Error;
                active.Visible = false;
            }
            else
            {
                status.StatusText = message;
                status.State = LauncherState.Loading;
                active.Visible = false;
            }
        }

        private string EnsurePbeConfiguration()
        {
            string configPath = Path.Combine(root, "config.json");
            string examplePath = Path.Combine(root, "config.example.json");
            Dictionary<string, object> config;
            if (File.Exists(configPath)) config = Json.ReadObject(configPath);
            else if (File.Exists(examplePath)) config = Json.ReadObject(examplePath);
            else config = new Dictionary<string, object>();
            object value; string champions = config.TryGetValue("pbeChampionsDirectory", out value) ? Convert.ToString(value) : "";
            if (!ValidChampionsDirectory(champions))
            {
                using (var dialog = new FolderBrowserDialog { Description = "First-time setup: select your League of Legends PBE folder", ShowNewFolderButton = false })
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return null;
                    string selected = dialog.SelectedPath;
                    string direct = Path.Combine(selected, "Game", "DATA", "FINAL", "Champions");
                    string nested = Path.Combine(selected, "Riot Games", "League of Legends (PBE)", "Game", "DATA", "FINAL", "Champions");
                    champions = ValidChampionsDirectory(selected) ? selected : ValidChampionsDirectory(direct) ? direct : ValidChampionsDirectory(nested) ? nested : null;
                    if (champions == null) { MessageBox.Show(this, "Invalid PBE folder.", "Classic Skin Morph", MessageBoxButtons.OK, MessageBoxIcon.Error); return null; }
                }
            }
            config["pbeChampionsDirectory"] = Path.GetFullPath(champions);
            if (!config.ContainsKey("modLibrary")) config["modLibrary"] = "mods";
            Json.Write(configPath, config);
            return Path.GetFullPath(champions);
        }

        private static bool ValidChampionsDirectory(string path)
        {
            try { return !string.IsNullOrWhiteSpace(path) && Directory.Exists(path) && Directory.GetFiles(path, "*.wad.client").Length > 0; } catch { return false; }
        }

        private static void HideEnemySummonerEmotes(string championsDirectory)
        {
            string installRoot = Path.GetFullPath(Path.Combine(championsDirectory, "..", "..", "..", ".."));
            string configRoot = Path.Combine(installRoot, "Config");
            string gameConfig = Path.Combine(configRoot, "game.cfg");
            if (File.Exists(gameConfig))
            {
                string text = File.ReadAllText(gameConfig);
                string updated = Regex.Replace(text, @"(?im)^HideEnemySummonerEmotes\s*=\s*\d+\s*$", "HideEnemySummonerEmotes=1");
                if (updated == text && !Regex.IsMatch(text, @"(?im)^HideEnemySummonerEmotes\s*="))
                    updated += (updated.EndsWith("\n") ? "" : Environment.NewLine) + "HideEnemySummonerEmotes=1" + Environment.NewLine;
                if (updated != text) File.WriteAllText(gameConfig, updated, new UTF8Encoding(false));
            }

            string persisted = Path.Combine(configRoot, "PersistedSettings.json");
            if (File.Exists(persisted))
            {
                string text = File.ReadAllText(persisted);
                string pattern = @"(\""name\""\s*:\s*\""HideEnemySummonerEmotes\""\s*,\s*\""value\""\s*:\s*\""?)\d+(\""?)";
                string updated = Regex.Replace(text, pattern, "${1}1$2", RegexOptions.IgnoreCase);
                if (updated != text) File.WriteAllText(persisted, updated, new UTF8Encoding(false));
            }
        }

        private void OnClosing(object sender, FormClosingEventArgs e)
        {
            monitorTimer.Stop();
            gameStateTimer.Stop();
            animationTimer.Stop();
            if (!sessionStarted && !File.Exists(Path.Combine(root, "state", "ltk-session.json"))) return;
            status.StatusText = "STOPPING AND CLEANING UP SKINS...";
            status.State = LauncherState.Loading;
            active.Visible = false;
            Refresh();
            ltk.Restore();
        }

        private static Label MakeLabel(Point location, Size size, float fontSize, FontStyle style, ContentAlignment alignment, Color color)
        {
            return new Label { Location = location, Size = size, Font = new Font("Segoe UI", fontSize, style), TextAlign = alignment, ForeColor = color, BackColor = Color.Transparent };
        }
    }

    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            int applyIndex = Array.FindIndex(args, value => string.Equals(value, "--apply-update", StringComparison.OrdinalIgnoreCase));
            if (applyIndex >= 0 && args.Length >= applyIndex + 4)
            {
                int processId;
                if (int.TryParse(args[applyIndex + 1], out processId)) ApplyUpdate(processId, args[applyIndex + 2], args[applyIndex + 3]);
                return;
            }
            if (args.Any(value => string.Equals(value, "--backend-load", StringComparison.OrdinalIgnoreCase)))
            {
                RunBackendLoad(); return;
            }
            if (args.Any(value => string.Equals(value, "--backend-restore", StringComparison.OrdinalIgnoreCase)))
            {
                new LtkService(AppDomain.CurrentDomain.BaseDirectory).Restore(); return;
            }
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object sender, System.Threading.ThreadExceptionEventArgs e) { LogCrash(e.Exception); };
            AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e) { LogCrash(e.ExceptionObject as Exception); };
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try { Application.Run(new RiftLegacyForm()); }
            catch (Exception ex) { LogCrash(ex); }
        }

        private static void ApplyUpdate(int processId, string source, string target)
        {
            try
            {
                try { Process.GetProcessById(processId).WaitForExit(60000); } catch { }
                bool copied = false;
                for (int attempt = 0; attempt < 60 && !copied; attempt++)
                {
                    try { File.Copy(source, target, true); copied = true; }
                    catch { Thread.Sleep(500); }
                }
                if (!copied) return;
                Process.Start(new ProcessStartInfo { FileName = target, UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(target) });
                try { File.Delete(source); } catch { }
            }
            catch (Exception ex) { LogCrash(ex); }
        }

        private static void RunBackendLoad()
        {
            string root = AppDomain.CurrentDomain.BaseDirectory;
            string resultPath = Path.Combine(root, "state", "backend-result.json");
            try
            {
                var config = Json.ReadObject(Path.Combine(root, "config.json")); object value;
                if (!config.TryGetValue("pbeChampionsDirectory", out value)) throw new InvalidOperationException("PBE directory is not configured.");
                UserPreferences preferences;
                string preferencesPath = Path.Combine(root, "state", "user-preferences.json");
                try { preferences = File.Exists(preferencesPath) ? Json.Read<UserPreferences>(preferencesPath) : new UserPreferences(); }
                catch { preferences = new UserPreferences(); }
                if (preferences.DisabledChampions == null) preferences.DisabledChampions = new List<string>();
                var service = new LtkService(root); service.ResetForReload();
                int count = service.Start(Convert.ToString(value), preferences, new Progress<LoadProgress>());
                Json.Write(resultPath, new Dictionary<string, object> { { "ok", true }, { "packages", count } });
            }
            catch (Exception ex)
            {
                Json.Write(resultPath, new Dictionary<string, object> { { "ok", false }, { "message", ex.Message } });
            }
        }

        private static void LogCrash(Exception exception)
        {
            try
            {
                string directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "state");
                Directory.CreateDirectory(directory);
                File.AppendAllText(Path.Combine(directory, "crash.log"), DateTime.Now.ToString("o") + Environment.NewLine + (exception == null ? "Unknown error" : exception.ToString()) + Environment.NewLine);
            }
            catch { }
        }
    }
}
