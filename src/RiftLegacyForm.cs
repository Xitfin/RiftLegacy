using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using System.Runtime.InteropServices;

namespace ClassicSkinMorph
{
    internal sealed class RiftProfile
    {
        public string RiotId = "";
        public string Rank = "Unranked";
        public int Lp;
        public int Wins;
        public int Losses;
        public double WinRate { get { int total = Wins + Losses; return total == 0 ? 0 : Wins * 100.0 / total; } }
    }

    internal sealed class RiftLegacyForm : Form
    {
        private static readonly Color Dark = Color.FromArgb(1, 10, 19);
        private static readonly Color Dark2 = Color.FromArgb(10, 28, 36);
        private static readonly Color Gold = Color.FromArgb(200, 170, 110);
        private static readonly Color GoldDark = Color.FromArgb(120, 90, 40);
        private static readonly Color Parchment = Color.FromArgb(232, 214, 164);
        private static readonly Color Ink = Color.FromArgb(61, 47, 22);
        private static readonly Color Teal = Color.FromArgb(84, 208, 224);
        private static readonly PrivateFontCollection Fonts = new PrivateFontCollection();
        private static string cinzelName = "Georgia";
        private static string spectralName = "Georgia";
        private readonly string root;
        private readonly LtkService ltk;
        private readonly string preferencesPath;
        private UserPreferences preferences;
        private readonly Panel body;
        private readonly Panel topBar;
        private readonly Panel titleBar;
        private readonly Button loadButton;
        private readonly Button settingsButton;
        private readonly Label connectionLabel;
        private readonly ProgressBar loadProgress;
        private readonly Label gameTitle;
        private readonly Label gameSub;
        private readonly Panel gameArea;
        private readonly Panel profileArea;
        private readonly System.Windows.Forms.Timer pollTimer;
        private readonly System.Windows.Forms.Timer loadTimer;
        private bool busy;
        private bool gameWasActive;
        private bool rosterLoading;
        private bool polling;
        private DateTime lastProfileRefresh = DateTime.MinValue;
        private RiftProfile currentProfile;
        private int visualProgress;

        public RiftLegacyForm()
        {
            root = AppDomain.CurrentDomain.BaseDirectory;
            ltk = new LtkService(root);
            preferencesPath = Path.Combine(root, "state", "user-preferences.json");
            preferences = LoadPreferences();
            LoadBundledFonts(root);
            Text = "Rift Legacy";
            ClientSize = new Size(1280, 960);
            MinimumSize = new Size(1100, 780);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.None;
            BackColor = Dark;
            ForeColor = Color.White;
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

            titleBar = new Panel { Location = new Point(0, 0), Size = new Size(ClientSize.Width, 36),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, BackColor = Color.FromArgb(10, 22, 32) };
            titleBar.MouseDown += DragWindow;
            titleBar.Controls.Add(new Panel { Location = new Point(12, 10), Size = new Size(16, 16), BackColor = Gold });
            var windowTitle = new Label { Text = "Rift Legacy", Location = new Point(37, 0), Size = new Size(240, 36),
                TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 9f), ForeColor = Color.FromArgb(201, 194, 176), BackColor = Color.Transparent };
            windowTitle.MouseDown += DragWindow; titleBar.Controls.Add(windowTitle);
            titleBar.Controls.Add(WindowButton("—", 138, delegate { WindowState = FormWindowState.Minimized; }, false));
            titleBar.Controls.Add(WindowButton("□", 92, delegate { WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized; }, false));
            titleBar.Controls.Add(WindowButton("×", 46, delegate { Close(); }, true));
            Controls.Add(titleBar);

            topBar = new Panel { Location = new Point(0, 36), Size = new Size(ClientSize.Width, 76),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, BackColor = Dark2, Padding = new Padding(28, 17, 28, 17) };
            topBar.Paint += delegate(object s, PaintEventArgs e) { using (var p = new Pen(GoldDark, 2)) e.Graphics.DrawLine(p, 0, topBar.Height - 2, topBar.Width, topBar.Height - 2); };
            Controls.Add(topBar);
            loadButton = ClassicButton("LOAD", new Point(28, 18), new Size(110, 40), true);
            settingsButton = ClassicButton("⚙", new Point(154, 18), new Size(40, 40), false);
            settingsButton.Font = new Font("Segoe UI Symbol", 13f, FontStyle.Regular);
            topBar.Controls.Add(loadButton); topBar.Controls.Add(settingsButton);
            loadButton.Click += async delegate { await BeginLoad(); };
            settingsButton.Click += SettingsClicked;

            var logo = LabelAt("RIFT LEGACY", new Point(460, 4), new Size(360, 38), 19.5f, FontStyle.Bold, Gold, ContentAlignment.MiddleCenter);
            var motto = LabelAt("RELIVE THE RIFT", new Point(460, 41), new Size(360, 18), 8f, FontStyle.Regular, Color.FromArgb(91, 90, 86), ContentAlignment.MiddleCenter);
            topBar.Controls.Add(logo); topBar.Controls.Add(motto);

            connectionLabel = LabelAt("CHECKING CLIENT...", new Point(930, 10), new Size(300, 20), 8f, FontStyle.Regular, Gold, ContentAlignment.MiddleRight);
            loadProgress = new ProgressBar { Location = new Point(1010, 36), Size = new Size(220, 12), Style = ProgressBarStyle.Continuous, Maximum = 100 };
            topBar.Controls.Add(connectionLabel); topBar.Controls.Add(loadProgress);

            body = new ParchmentPanel { Location = new Point(0, 112), Size = new Size(ClientSize.Width, ClientSize.Height - 112),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                Padding = new Padding(34, 26, 34, 30) };
            Controls.Add(body); body.BringToFront(); topBar.BringToFront(); titleBar.BringToFront();
            gameTitle = LabelAt("CURRENT GAME", new Point(40, 20), new Size(1200, 34), 17, FontStyle.Bold, Color.FromArgb(90, 65, 22), ContentAlignment.MiddleCenter);
            gameSub = SpectralLabelAt("Automatic game detection", new Point(40, 54), new Size(1200, 22), 9, FontStyle.Italic, Color.FromArgb(124, 101, 51), ContentAlignment.MiddleCenter);
            gameArea = new Panel { Location = new Point(40, 84), Size = new Size(1200, 420), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, BackColor = Color.FromArgb(90, 255, 248, 224) };
            profileArea = new Panel { Location = new Point(40, 542), Size = new Size(1200, 382), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, BackColor = Color.FromArgb(105, 255, 248, 224) };
            profileArea.Paint += delegate(object sender, PaintEventArgs e) {
                using (var border = new Pen(Color.FromArgb(196, 168, 106))) e.Graphics.DrawRectangle(border, 0, 0, profileArea.Width - 1, profileArea.Height - 1);
                using (var divider = new Pen(Color.FromArgb(90, 138, 108, 51), 2)) e.Graphics.DrawLine(divider, 600, 34, 600, profileArea.Height - 34);
            };
            body.AutoScroll = true;
            body.AutoScrollMinSize = new Size(0, 955);
            body.Controls.Add(gameTitle); body.Controls.Add(gameSub); body.Controls.Add(gameArea); body.Controls.Add(profileArea);
            ShowNoClient();

            pollTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            pollTimer.Tick += async delegate { await PollState(); };
            loadTimer = new System.Windows.Forms.Timer { Interval = 350 };
            loadTimer.Tick += delegate { if (visualProgress < 95) { visualProgress++; loadProgress.Value = visualProgress; } ltk.HideManagerWindow(); if (ltk.IsReady) FinishLoad(); };
            Shown += async delegate { await StartupSequence(); };
            FormClosing += delegate { pollTimer.Stop(); loadTimer.Stop(); ltk.Restore(); };
            Resize += delegate { LayoutTopBar(); };
        }

        private void LayoutTopBar()
        {
            int center = topBar.ClientSize.Width / 2;
            foreach (Control c in topBar.Controls)
            {
                if (c.Text == "RIFT LEGACY" || c.Text == "RELIVE THE RIFT") c.Left = center - c.Width / 2;
            }
            connectionLabel.Left = topBar.ClientSize.Width - 350;
            loadProgress.Left = topBar.ClientSize.Width - 290;
        }

        private Button WindowButton(string text, int right, Action action, bool close)
        {
            var button = new Button { Text = text, Location = new Point(ClientSize.Width - right, 0), Size = new Size(46, 36),
                Anchor = AnchorStyles.Top | AnchorStyles.Right, FlatStyle = FlatStyle.Flat, FlatAppearance = { BorderSize = 0 },
                BackColor = Color.FromArgb(10, 22, 32), ForeColor = Color.FromArgb(201, 194, 176), Font = new Font("Segoe UI", 10f), TabStop = false };
            button.Click += delegate { action(); };
            button.MouseEnter += delegate { button.BackColor = close ? Color.FromArgb(232, 17, 35) : Color.FromArgb(35, 46, 55); };
            button.MouseLeave += delegate { button.BackColor = Color.FromArgb(10, 22, 32); };
            return button;
        }

        private void DragWindow(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            ReleaseCapture(); SendMessage(Handle, 0xA1, new IntPtr(2), IntPtr.Zero);
        }

        private async Task StartupSequence()
        {
            loadButton.Enabled = false;
            for (int p = 0; p <= 100; p += 5)
            {
                connectionLabel.Text = p < 100 ? "CHECKING FOR UPDATES... " + p + "%" : "UP TO DATE";
                loadProgress.Value = p;
                await Task.Delay(24);
            }
            loadProgress.Value = 0;
            loadButton.Enabled = true;
            pollTimer.Start();
            await PollState();
        }

        private async Task PollState()
        {
            if (polling) return;
            polling = true;
            try
            {
                Dictionary<string, object> stats = null;
                bool gameActive = await Task.Run(() => TryLiveJson("gamestats", out stats));
                string ignoredApi, ignoredAuth;
                bool clientReady = TryLcuConnection(out ignoredApi, out ignoredAuth);
                if (!clientReady)
                {
                    connectionLabel.Text = "OFFLINE";
                    loadButton.Enabled = false;
                    ShowNoClient();
                    gameWasActive = false;
                    return;
                }
                loadButton.Enabled = !busy;
                connectionLabel.Text = ltk.IsReady ? "READY TO PLAY" : "CLIENT CONNECTED";
                if ((DateTime.UtcNow - lastProfileRefresh).TotalSeconds >= 10)
                {
                    lastProfileRefresh = DateTime.UtcNow;
                    RiftProfile profile = await Task.Run(() => ReadCurrentProfile());
                    if (profile != null) { currentProfile = profile; RenderProfile(profile); }
                }
                if (gameActive)
                {
                    object value; double seconds = stats != null && stats.TryGetValue("gameTime", out value) ? Convert.ToDouble(value) : 0;
                    gameSub.Text = "JADE  ·  GAME TIME " + TimeSpan.FromSeconds(seconds).ToString(@"m\:ss");
                    if (!gameWasActive && !rosterLoading)
                    {
                        rosterLoading = true;
                        gameArea.Controls.Clear();
                        gameArea.Controls.Add(LabelAt("READING PLAYERS...", new Point(0, 140), new Size(gameArea.Width, 30), 13, FontStyle.Bold, GoldDark, ContentAlignment.MiddleCenter));
                        List<GamePlayerInfo> players = await Task.Run(() => ReadPlayers());
                        RenderGame(players);
                        rosterLoading = false;
                    }
                    gameWasActive = true;
                }
                else
                {
                    if (gameWasActive || gameArea.Controls.Count == 0 || gameTitle.Text != "CURRENT GAME") ShowNoGame();
                    gameWasActive = false;
                }
            }
            finally { polling = false; }
        }

        private void ShowNoClient()
        {
            gameArea.Controls.Clear(); profileArea.Controls.Clear();
            gameTitle.Text = "LEAGUE OF LEGENDS IS NOT RUNNING";
            gameSub.Text = "Waiting for a response from the PBE client...";
            gameArea.Controls.Add(LabelAt("!", new Point((gameArea.Width - 84) / 2, 78), new Size(84, 84), 34, FontStyle.Bold, Color.FromArgb(140, 35, 24), ContentAlignment.MiddleCenter));
            gameArea.Controls.Add(LabelAt("LAUNCH LEAGUE OF LEGENDS TO CONTINUE", new Point(0, 178), new Size(gameArea.Width, 32), 13, FontStyle.Bold, Color.FromArgb(140, 35, 24), ContentAlignment.MiddleCenter));
        }

        private void ShowNoGame()
        {
            gameTitle.Text = "CURRENT GAME";
            gameSub.Text = "Automatic game detection is active";
            gameArea.Controls.Clear();
            gameArea.Controls.Add(LabelAt("NO GAME IN PROGRESS", new Point(0, 126), new Size(gameArea.Width, 34), 15, FontStyle.Bold, Color.FromArgb(107, 82, 32), ContentAlignment.MiddleCenter));
            gameArea.Controls.Add(SpectralLabelAt("Rift Legacy will display both teams automatically.", new Point(0, 165), new Size(gameArea.Width, 22), 9, FontStyle.Italic, Color.FromArgb(124, 101, 51), ContentAlignment.MiddleCenter));
        }

        private void RenderProfile(RiftProfile profile)
        {
            profileArea.Controls.Clear();
            var name = LabelAt(profile.RiotId, new Point(30, 28), new Size(540, 54), 30, FontStyle.Bold, Ink, ContentAlignment.MiddleCenter);
            var rank = LabelAt(profile.Rank, new Point(30, 328), new Size(540, 38), 20, FontStyle.Bold, Color.FromArgb(90, 65, 22), ContentAlignment.MiddleCenter);
            profileArea.Controls.Add(name); profileArea.Controls.Add(rank);
            string emblem = Path.Combine(root, "assets", "rift-rank-emblem-large.png");
            if (File.Exists(emblem)) profileArea.Controls.Add(new PictureBox { Location = new Point(170, 78), Size = new Size(260, 240), Image = Image.FromFile(emblem), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent });
            AddMetric("LP", profile.Lp.ToString(), 650, 48, Ink);
            AddMetric("WINS", profile.Wins.ToString(), 650, 120, Color.FromArgb(27, 110, 47));
            AddMetric("LOSSES", profile.Losses.ToString(), 650, 192, Color.FromArgb(140, 35, 24));
            AddMetric("WIN RATE", profile.WinRate.ToString("0.0") + "%", 650, 264, profile.WinRate >= 50 ? Color.FromArgb(27, 110, 47) : Color.FromArgb(140, 35, 24));
        }

        private void AddMetric(string label, string value, int x, int y, Color color)
        {
            profileArea.Controls.Add(LabelAt(label, new Point(x, y), new Size(220, 44), 14, FontStyle.Bold, Color.FromArgb(138, 108, 51), ContentAlignment.MiddleLeft));
            profileArea.Controls.Add(LabelAt(value, new Point(x + 230, y), new Size(250, 44), 23, FontStyle.Bold, color, ContentAlignment.MiddleRight));
        }

        private void RenderGame(List<GamePlayerInfo> players)
        {
            gameTitle.Text = "CURRENT GAME";
            gameArea.Controls.Clear();
            var blue = TeamPanel("BLUE SIDE", Color.FromArgb(42, 111, 143), 10);
            var red = TeamPanel("RED SIDE", Color.FromArgb(162, 53, 42), 610);
            gameArea.Controls.Add(blue); gameArea.Controls.Add(red);
            foreach (GamePlayerInfo p in players)
            {
                Panel team = string.Equals(p.Team, "ORDER", StringComparison.OrdinalIgnoreCase) ? blue : red;
                int row = team.Controls.OfType<Panel>().Count();
                team.Controls.Add(PlayerRow(p, 10, 50 + row * 68, team.Width - 20));
            }
        }

        private Panel TeamPanel(string title, Color accent, int x)
        {
            var panel = new Panel { Location = new Point(x, 10), Size = new Size(580, 400), BackColor = Color.FromArgb(42, accent) };
            panel.Paint += delegate(object s, PaintEventArgs e) { using (var p = new Pen(accent)) e.Graphics.DrawRectangle(p, 0, 0, panel.Width - 1, panel.Height - 1); };
            panel.Controls.Add(LabelAt(title, new Point(18, 9), new Size(544, 30), 12, FontStyle.Bold, accent, ContentAlignment.MiddleLeft));
            return panel;
        }

        private Panel PlayerRow(GamePlayerInfo player, int x, int y, int width)
        {
            var row = new Panel { Location = new Point(x, y), Size = new Size(width, 60), BackColor = Color.FromArgb(165, 255, 248, 224) };
            string[] info = (player.Rank ?? "Unknown").Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            string smallEmblem = Path.Combine(root, "assets", "rift-rank-emblem.png");
            if (File.Exists(smallEmblem)) row.Controls.Add(new PictureBox { Location = new Point(8, 8), Size = new Size(56, 42), Image = Image.FromFile(smallEmblem), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent });
            row.Controls.Add(SpectralLabelAt(player.RiotId, new Point(76, 8), new Size(260, 22), 10, FontStyle.Bold, Ink, ContentAlignment.MiddleLeft));
            row.Controls.Add(SpectralLabelAt(player.Champion + "  ·  " + info[0], new Point(76, 31), new Size(280, 18), 8.5f, FontStyle.Italic, Color.FromArgb(138, 108, 51), ContentAlignment.MiddleLeft));
            row.Controls.Add(SpectralLabelAt(info.Length > 1 ? info[1].Trim('(', ')') : "", new Point(350, 8), new Size(190, 20), 9, FontStyle.Bold, Ink, ContentAlignment.MiddleRight));
            row.Controls.Add(SpectralLabelAt(info.Length > 2 ? info[2] : "", new Point(320, 31), new Size(220, 18), 8, FontStyle.Bold, Ink, ContentAlignment.MiddleRight));
            return row;
        }

        private async Task BeginLoad()
        {
            if (busy) return;
            busy = true; loadButton.Enabled = false; loadButton.Text = "LOADING"; visualProgress = 0; loadProgress.Value = 0;
            try
            {
                string champions = EnsurePbeConfiguration();
                if (champions == null) return;
                ltk.ResetForReload();
                loadTimer.Start();
                var reporter = new Progress<LoadProgress>(p => { visualProgress = Math.Max(visualProgress, p.Percent); loadProgress.Value = Math.Min(95, visualProgress); });
                await Task.Run(() => ltk.Start(champions, preferences, reporter));
            }
            catch (Exception ex) { loadTimer.Stop(); connectionLabel.Text = "ERROR: " + ex.Message.ToUpperInvariant(); busy = false; loadButton.Enabled = true; loadButton.Text = "LOAD"; }
        }

        private void FinishLoad()
        {
            loadTimer.Stop(); visualProgress = 100; loadProgress.Value = 100; connectionLabel.Text = "READY TO PLAY"; busy = false; loadButton.Enabled = true; loadButton.Text = "RELOAD";
        }

        private void SettingsClicked(object sender, EventArgs e)
        {
            using (var dialog = new SettingsForm(root, preferences))
                if (dialog.ShowDialog(this) == DialogResult.OK && dialog.Preferences != null)
                { preferences = dialog.Preferences; Json.Write(preferencesPath, preferences); loadButton.Text = "RELOAD"; }
        }

        private UserPreferences LoadPreferences()
        {
            try { if (File.Exists(preferencesPath)) { var p = Json.Read<UserPreferences>(preferencesPath); if (p.DisabledChampions == null) p.DisabledChampions = new List<string>(); return p; } } catch { }
            return new UserPreferences();
        }

        private string EnsurePbeConfiguration()
        {
            string path = Path.Combine(root, "config.json");
            var config = File.Exists(path) ? Json.ReadObject(path) : new Dictionary<string, object>();
            object value; string champions = config.TryGetValue("pbeChampionsDirectory", out value) ? Convert.ToString(value) : "";
            if (!Directory.Exists(champions))
            {
                using (var d = new FolderBrowserDialog { Description = "Select League of Legends PBE" })
                {
                    if (d.ShowDialog(this) != DialogResult.OK) return null;
                    string direct = Path.Combine(d.SelectedPath, "Game", "DATA", "FINAL", "Champions");
                    champions = Directory.Exists(direct) ? direct : d.SelectedPath;
                }
            }
            config["pbeChampionsDirectory"] = champions; Json.Write(path, config); return champions;
        }

        private RiftProfile ReadCurrentProfile()
        {
            string api, auth; if (!TryLcuConnection(out api, out auth)) return null;
            var summoner = RequestObject(api + "/lol-summoner/v1/current-summoner", auth);
            var ranked = RequestObject(api + "/lol-ranked/v1/current-ranked-stats", auth);
            var jade = JadeQueue(ranked);
            return new RiftProfile {
                RiotId = Field(summoner, "gameName", "Unknown") + "#" + Field(summoner, "tagLine", ""),
                Rank = RankName(jade), Lp = Number(jade, "leaguePoints"), Wins = Number(jade, "wins"), Losses = Number(jade, "losses")
            };
        }

        private List<GamePlayerInfo> ReadPlayers()
        {
            object raw = RequestValue("https://127.0.0.1:2999/liveclientdata/playerlist", null);
            var rows = raw as object[] ?? new object[0];
            string api, auth; TryLcuConnection(out api, out auth);
            var result = rows.Select(v => v as Dictionary<string, object>).Where(v => v != null).Select(v => new GamePlayerInfo {
                RiotId = Field(v, "riotId", Field(v, "summonerName", "Unknown")), Champion = Field(v, "championName", "Unknown"), Team = Field(v, "team", "CHAOS"), Rank = "Unknown"
            }).ToList();
            if (!string.IsNullOrEmpty(api)) Parallel.ForEach(result, new ParallelOptions { MaxDegreeOfParallelism = 5 }, p => {
                try
                {
                    var summoner = RequestObject(api + "/lol-summoner/v1/summoners?name=" + Uri.EscapeDataString(p.RiotId), auth);
                    string puuid = Field(summoner, "puuid", "");
                    var jade = JadeQueue(RequestObject(api + "/lol-ranked/v1/ranked-stats/" + puuid, auth));
                    int wins = Number(jade, "wins"), losses = Number(jade, "losses"), total = wins + losses;
                    p.Rank = RankName(jade) + "\r\n(" + Number(jade, "leaguePoints") + " LP)\r\n" + wins + "W / " + losses + "L | " + (total == 0 ? 0 : wins * 100.0 / total).ToString("0.0") + "% WR";
                }
                catch { }
            });
            return result;
        }

        private bool TryLcuConnection(out string api, out string auth)
        {
            api = null; auth = null;
            try
            {
                var config = Json.ReadObject(Path.Combine(root, "config.json")); object value;
                if (!config.TryGetValue("pbeChampionsDirectory", out value)) return false;
                string install = Path.GetFullPath(Path.Combine(Convert.ToString(value), "..", "..", "..", ".."));
                string lockfile = Path.Combine(install, "lockfile"); if (!File.Exists(lockfile)) return false;
                string text; using (var fs = new FileStream(lockfile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) using (var sr = new StreamReader(fs)) text = sr.ReadToEnd();
                string[] p = text.Split(':'); if (p.Length < 5) return false;
                api = "https://127.0.0.1:" + p[2]; auth = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("riot:" + p[3])); return true;
            }
            catch { return false; }
        }

        private static bool TryLiveJson(string endpoint, out Dictionary<string, object> value)
        {
            try { value = RequestObject("https://127.0.0.1:2999/liveclientdata/" + endpoint, null); return value.Count > 0; } catch { value = null; return false; }
        }

        private static object RequestValue(string url, string auth)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12; ServicePointManager.ServerCertificateValidationCallback = delegate { return true; };
            var req = (HttpWebRequest)WebRequest.Create(url); req.Timeout = 1200; req.ReadWriteTimeout = 1200; req.Proxy = null;
            if (!string.IsNullOrEmpty(auth)) req.Headers[HttpRequestHeader.Authorization] = auth;
            using (var response = req.GetResponse()) using (var reader = new StreamReader(response.GetResponseStream())) return new JavaScriptSerializer().DeserializeObject(reader.ReadToEnd());
        }

        private static Dictionary<string, object> RequestObject(string url, string auth) { return RequestValue(url, auth) as Dictionary<string, object> ?? new Dictionary<string, object>(); }
        private static Dictionary<string, object> JadeQueue(Dictionary<string, object> ranked)
        {
            object q; var map = ranked.TryGetValue("queueMap", out q) ? q as Dictionary<string, object> : null; object jade;
            return map != null && map.TryGetValue("JADE_RANKED_SOLO_5x5", out jade) ? jade as Dictionary<string, object> ?? new Dictionary<string, object>() : new Dictionary<string, object>();
        }
        private static string RankName(Dictionary<string, object> jade) { string tier = Field(jade, "tier", "Unranked"); string division = Field(jade, "division", ""); return string.IsNullOrEmpty(tier) ? "Unranked" : System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(tier.ToLowerInvariant()) + (division == "NA" || division == "" ? "" : " " + division); }
        private static int Number(Dictionary<string, object> map, string key) { object v; int n; return map != null && map.TryGetValue(key, out v) && int.TryParse(Convert.ToString(v), out n) ? n : 0; }
        private static string Field(Dictionary<string, object> map, string key, string fallback) { object v; return map != null && map.TryGetValue(key, out v) && v != null ? Convert.ToString(v) : fallback; }
        private static void BorderPaint(object sender, PaintEventArgs e) { var p = sender as Panel; using (var pen = new Pen(Color.FromArgb(196, 168, 106))) e.Graphics.DrawRectangle(pen, 0, 0, p.Width - 1, p.Height - 1); }

        private static Button ClassicButton(string text, Point location, Size size, bool red)
        {
            var b = new Button { Text = text, Location = location, Size = size, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, Font = new Font(cinzelName, 9, FontStyle.Bold), ForeColor = Color.FromArgb(240, 230, 200), BackColor = red ? Color.FromArgb(126, 31, 20) : Color.FromArgb(20, 28, 34) };
            b.FlatAppearance.BorderColor = red ? Color.FromArgb(216, 162, 74) : GoldDark; return b;
        }
        private static Label LabelAt(string text, Point location, Size size, float font, FontStyle style, Color color, ContentAlignment align)
        { return new Label { Text = text, Location = location, Size = size, Font = new Font(cinzelName, font, style), ForeColor = color, BackColor = Color.Transparent, TextAlign = align, AutoEllipsis = true }; }
        private static Label SpectralLabelAt(string text, Point location, Size size, float font, FontStyle style, Color color, ContentAlignment align)
        { return new Label { Text = text, Location = location, Size = size, Font = new Font(spectralName, font, style), ForeColor = color, BackColor = Color.Transparent, TextAlign = align, AutoEllipsis = true }; }

        private static void LoadBundledFonts(string applicationRoot)
        {
            try
            {
                string directory = Path.Combine(applicationRoot, "assets", "fonts");
                foreach (string file in Directory.GetFiles(directory, "*.ttf")) Fonts.AddFontFile(file);
                FontFamily cinzel = Fonts.Families.FirstOrDefault(f => string.Equals(f.Name, "Cinzel", StringComparison.OrdinalIgnoreCase));
                FontFamily spectral = Fonts.Families.FirstOrDefault(f => string.Equals(f.Name, "Spectral", StringComparison.OrdinalIgnoreCase));
                if (cinzel != null) cinzelName = cinzel.Name;
                if (spectral != null) spectralName = spectral.Name;
            }
            catch { }
        }

        private sealed class ParchmentPanel : Panel
        {
            public ParchmentPanel() { DoubleBuffered = true; }
            protected override void OnPaintBackground(PaintEventArgs e)
            {
                using (var brush = new LinearGradientBrush(ClientRectangle, Color.FromArgb(240, 226, 182), Color.FromArgb(220, 196, 136), 25f)) e.Graphics.FillRectangle(brush, ClientRectangle);
            }
        }

        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    }
}
