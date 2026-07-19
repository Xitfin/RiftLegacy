const { app, BrowserWindow, ipcMain, dialog } = require('electron');
const fs = require('fs');
const path = require('path');
const https = require('https');
const crypto = require('crypto');
const { execFile, execFileSync, spawn } = require('child_process');

const root = path.resolve(__dirname, '..');
if (process.env.LOCALAPPDATA) app.setPath('userData', path.join(process.env.LOCALAPPDATA, 'Rift Legacy'));
const agent = new https.Agent({ rejectUnauthorized: false });
let window;
let quitting = false;
let updateState = { phase: 'checking', progress: 0, message: 'CHECKING FOR UPDATES...' };
const updateManifestUrl = 'https://raw.githubusercontent.com/Xitfin/RiftLegacy-Updates/main/update/latest.json';
const playerCache = new Map();
const streakCache = new Map();

function readJson(file, fallback = {}) {
  try { return JSON.parse(fs.readFileSync(file, 'utf8')); } catch { return fallback; }
}

function userFile(name) { return path.join(app.getPath('userData'), name); }
function writeJson(file, value) { fs.mkdirSync(path.dirname(file), { recursive: true }); fs.writeFileSync(file, JSON.stringify(value, null, 2)); }
function resolveChampionsDirectory(selected) {
  if (!selected) return null;
  const candidates = [selected,
    path.join(selected, 'Game', 'DATA', 'FINAL', 'Champions'),
    path.join(selected, 'DATA', 'FINAL', 'Champions'),
    path.join(selected, 'Riot Games', 'League of Legends (PBE)', 'Game', 'DATA', 'FINAL', 'Champions')];
  return candidates.find(candidate => fs.existsSync(candidate) && path.basename(candidate).toLowerCase() === 'champions') || null;
}
function autoDetectPbe() {
  const saved = readJson(userFile('config.json')).pbeChampionsDirectory;
  const validSaved = resolveChampionsDirectory(saved);
  if (validSaved) return validSaved;
  const candidates = [];
  const riotInstalls = readJson(path.join(process.env.ProgramData || 'C:\\ProgramData', 'Riot Games', 'RiotClientInstalls.json'));
  for (const value of [...Object.keys(riotInstalls.associated_client || {}), ...Object.values(riotInstalls.associated_client || {})]) if (/pbe/i.test(String(value))) candidates.push(path.dirname(String(value)));
  for (const drive of ['C:','D:','E:','F:']) {
    candidates.push(`${drive}\\Riot Games\\League of Legends (PBE)`, `${drive}\\PBE\\Riot Games\\League of Legends (PBE)`);
  }
  for (const candidate of candidates) { const found = resolveChampionsDirectory(candidate); if (found) return found; }
  return null;
}
function savePbePath(champions) {
  const config = { pbeChampionsDirectory: champions, liveClientEndpoint: 'https://127.0.0.1:2999/liveclientdata/playerlist', pollIntervalMs: 3000, modLibrary: 'mods', selectionManifest: 'state\\selection.json', adapter: { enabled: false, command: '', arguments: ['--manifest','{manifest}'] } };
  writeJson(userFile('config.json'), config);
  writeJson(path.join(root, 'config.json'), config);
  return config;
}
async function selectPbePath(firstRun = false) {
  const result = await dialog.showOpenDialog(window, { title: firstRun ? 'Select your League of Legends PBE folder' : 'Change League of Legends PBE folder', properties: ['openDirectory'], buttonLabel: 'Select PBE folder' });
  if (result.canceled || !result.filePaths[0]) return { ok: false, canceled: true };
  const champions = resolveChampionsDirectory(result.filePaths[0]);
  if (!champions) { await dialog.showMessageBox(window, { type: 'error', title: 'Invalid PBE folder', message: 'Rift Legacy could not find Game\\DATA\\FINAL\\Champions in this folder.' }); return { ok: false, message: 'Invalid PBE folder.' }; }
  savePbePath(champions); return { ok: true, path: champions };
}

function requestJson(url, authorization, timeout = 1200) {
  return new Promise((resolve, reject) => {
    const req = https.get(url, { agent, timeout, headers: authorization ? { Authorization: authorization } : {} }, res => {
      let body = '';
      res.on('data', chunk => body += chunk);
      res.on('end', () => { try { resolve(JSON.parse(body)); } catch (error) { reject(error); } });
    });
    req.on('timeout', () => req.destroy(new Error('timeout')));
    req.on('error', reject);
  });
}

function publishUpdate(state) {
  updateState = { ...updateState, ...state };
  if (window && !window.isDestroyed()) window.webContents.send('rift:update-status', updateState);
}
function getRemoteBuffer(url, timeout = 8000, onProgress = null, redirects = 0) {
  return new Promise((resolve, reject) => {
    if (redirects > 5) return reject(new Error('Too many update redirects.'));
    const req = https.get(url, { timeout, headers: { 'User-Agent': 'Rift-Legacy-Updater', Accept: 'application/octet-stream' } }, res => {
      if (res.statusCode >= 300 && res.statusCode < 400 && res.headers.location) {
        res.resume(); return resolve(getRemoteBuffer(new URL(res.headers.location, url).href, timeout, onProgress, redirects + 1));
      }
      if (res.statusCode !== 200) { res.resume(); return reject(new Error(`Update server returned ${res.statusCode}.`)); }
      const total = Number(res.headers['content-length'] || 0); let received = 0; const chunks = [];
      res.on('data', chunk => { chunks.push(chunk); received += chunk.length; if (onProgress && total) onProgress(Math.min(100, Math.round(received * 100 / total))); });
      res.on('end', () => resolve(Buffer.concat(chunks)));
    });
    req.on('timeout', () => req.destroy(new Error('Update check timed out.')));
    req.on('error', reject);
  });
}
function versionParts(value) { return String(value || '0').split('.').map(part => Number(part.replace(/\D.*$/, '')) || 0); }
function isNewerVersion(remote, local) {
  const a = versionParts(remote), b = versionParts(local);
  for (let i = 0; i < Math.max(a.length, b.length); i++) { if ((a[i] || 0) !== (b[i] || 0)) return (a[i] || 0) > (b[i] || 0); }
  return false;
}
function schedulePortableReplacement(downloadedFile) {
  const installedFile = process.env.PORTABLE_EXECUTABLE_FILE;
  if (!installedFile) throw new Error('Portable executable path is unavailable.');
  const helper = path.join(root, 'RiftLegacyBackend.exe');
  const child = spawn(helper, ['--apply-update',String(process.pid),downloadedFile,installedFile], { detached: true, windowsHide: true, stdio: 'ignore' });
  child.unref();
}
async function checkForUpdates() {
  if (!app.isPackaged || !process.env.PORTABLE_EXECUTABLE_FILE) { publishUpdate({ phase: 'ready', progress: 0, message: 'READY TO LOAD' }); return false; }
  try {
    publishUpdate({ phase: 'checking', progress: 0, message: 'CHECKING FOR UPDATES...' });
    const manifest = JSON.parse((await getRemoteBuffer(updateManifestUrl, 6000)).toString('utf8'));
    if (!isNewerVersion(manifest.version, app.getVersion())) { publishUpdate({ phase: 'ready', progress: 0, message: 'READY TO LOAD' }); return false; }
    if (!/^https:\/\/github\.com\/Xitfin\/RiftLegacy-Updates\/releases\/download\//i.test(String(manifest.downloadUrl || ''))) throw new Error('Untrusted update URL.');
    publishUpdate({ phase: 'downloading', progress: 0, message: `DOWNLOADING UPDATE ${manifest.version} · 0%` });
    const bytes = await getRemoteBuffer(manifest.downloadUrl, 30000, progress => publishUpdate({ phase: 'downloading', progress, message: `DOWNLOADING UPDATE ${manifest.version} · ${progress}%` }));
    if (manifest.size && bytes.length !== Number(manifest.size)) throw new Error('Update size verification failed.');
    const digest = crypto.createHash('sha256').update(bytes).digest('hex').toUpperCase();
    if (!manifest.sha256 || digest !== String(manifest.sha256).toUpperCase()) throw new Error('Update integrity verification failed.');
    const updateDirectory = path.join(app.getPath('userData'), 'updates'); fs.mkdirSync(updateDirectory, { recursive: true });
    const downloadedFile = path.join(updateDirectory, path.basename(manifest.file || `Rift-Legacy-${manifest.version}.exe`)); fs.writeFileSync(downloadedFile, bytes);
    publishUpdate({ phase: 'applying', progress: 100, message: 'INSTALLING UPDATE...' });
    schedulePortableReplacement(downloadedFile); setTimeout(() => app.quit(), 350); return true;
  } catch (error) {
    publishUpdate({ phase: 'ready', progress: 0, message: 'READY TO LOAD', warning: error.message }); return false;
  }
}

function lcuConnection() {
  try {
    const config = readJson(userFile('config.json'));
    const champions = config.pbeChampionsDirectory;
    if (!champions) return null;
    const install = path.resolve(champions, '..', '..', '..', '..');
    const lock = fs.readFileSync(path.join(install, 'lockfile'), 'utf8').split(':');
    if (lock.length < 5) return null;
    return { base: `https://127.0.0.1:${lock[2]}`, auth: `Basic ${Buffer.from(`riot:${lock[3]}`).toString('base64')}` };
  } catch { return null; }
}

function jade(queueMap = {}) { return queueMap.JADE_RANKED_SOLO_5x5 || {}; }
function playerStats(entry = {}) {
  const wins = Number(entry.wins || 0), losses = Number(entry.losses || 0), total = wins + losses;
  const tier = String(entry.tier || 'Unranked').toLowerCase();
  const rank = tier === 'unranked' ? 'Unranked' : tier[0].toUpperCase() + tier.slice(1) + (entry.division && entry.division !== 'NA' ? ` ${entry.division}` : '');
  return { rank, lp: Number(entry.leaguePoints || 0), wins, losses, winrate: total ? Math.round(wins * 100 / total) : 0 };
}

async function matchStreak(connection, puuid) {
  if (!puuid) return 0;
  const cached = streakCache.get(puuid); if (cached && Date.now() - cached.time < 60000) return cached.value;
  try {
    const history = await requestJson(`${connection.base}/lol-match-history/v1/products/lol/${encodeURIComponent(puuid)}/matches?begIndex=0&endIndex=20`, connection.auth, 3500);
    const rankedJadeGames = (history?.games?.games || []).filter(game => Number(game.queueId) === 4310 && game.gameMode === 'JADE' && game.gameType === 'MATCHED_GAME');
    let direction = 0, count = 0;
    for (const game of rankedJadeGames) {
      const identity = (game.participantIdentities || []).find(item => item?.player?.puuid === puuid);
      const participant = (game.participants || []).find(item => Number(item.participantId) === Number(identity?.participantId));
      if (!participant || typeof participant.stats?.win !== 'boolean') continue;
      const result = participant.stats.win ? 1 : -1;
      if (!direction) direction = result;
      if (result !== direction) break;
      count++;
    }
    const value = direction * count; streakCache.set(puuid, { value, time: Date.now() }); return value;
  } catch { streakCache.set(puuid, { value: 0, time: Date.now() }); return 0; }
}

async function rankedPlayer(connection, riotId) {
  const cacheKey = String(riotId || '').toLowerCase(); const cached = playerCache.get(cacheKey);
  if (cached && Date.now() - cached.time < 60000) return cached.value;
  try {
    const summoner = await requestJson(`${connection.base}/lol-summoner/v1/summoners?name=${encodeURIComponent(riotId)}`, connection.auth, 2200);
    const ranked = await requestJson(`${connection.base}/lol-ranked/v1/ranked-stats/${encodeURIComponent(summoner.puuid)}`, connection.auth, 2200);
    const value = { ...playerStats(jade(ranked.queueMap)), streak: await matchStreak(connection, summoner.puuid) };
    playerCache.set(cacheKey, { value, time: Date.now() }); return value;
  } catch { return { ...playerStats(), streak: 0 }; }
}

async function applicationState() {
  const connection = lcuConnection();
  if (!connection) return { client: false, game: false };
  let profile = null;
  try {
    const [summoner, ranked] = await Promise.all([
      requestJson(`${connection.base}/lol-summoner/v1/current-summoner`, connection.auth),
      requestJson(`${connection.base}/lol-ranked/v1/current-ranked-stats`, connection.auth)
    ]);
    profile = { name: `${summoner.gameName}#${summoner.tagLine}`, ...playerStats(jade(ranked.queueMap)), streak: await matchStreak(connection, summoner.puuid) };
  } catch { return { client: false, game: false }; }
  try {
    const [game, livePlayers] = await Promise.all([
      requestJson('https://127.0.0.1:2999/liveclientdata/gamestats'),
      requestJson('https://127.0.0.1:2999/liveclientdata/playerlist')
    ]);
    const players = await Promise.all(livePlayers.map(async p => ({
      name: p.riotId || p.summonerName, champion: p.championName, team: p.team,
      ...(p.riotId === profile.name ? profile : await rankedPlayer(connection, p.riotId || p.summonerName))
    })));
    return { client: true, profile, game: true, gameTime: Number(game.gameTime || 0), players };
  } catch { return { client: true, profile, game: false }; }
}

function createWindow() {
  window = new BrowserWindow({ width: 1280, height: 940, minWidth: 1100, minHeight: 760, frame: false, icon: path.join(root, 'assets', 'rift-legacy-icon.png'),
    backgroundColor: '#010a13', show: false, webPreferences: { preload: path.join(__dirname, 'preload.js'), contextIsolation: true, nodeIntegration: false } });
  window.loadFile(path.join(__dirname, 'index.html'));
  window.once('ready-to-show', async () => {
    window.show();
    const updating = await checkForUpdates();
    if (updating) return;
    const detected = autoDetectPbe();
    if (detected) savePbePath(detected); else await selectPbePath(true);
  });
}

app.whenReady().then(createWindow);
app.on('window-all-closed', () => app.quit());
app.on('before-quit', event => {
  if (quitting) return;
  event.preventDefault(); quitting = true;
  try { execFileSync(path.join(root, 'RiftLegacyBackend.exe'), ['--backend-restore'], { cwd: root, windowsHide: true, timeout: 15000 }); } catch {}
  app.quit();
});
ipcMain.handle('rift:state', applicationState);
ipcMain.handle('rift:update-state', () => updateState);
ipcMain.handle('rift:preferences', () => {
  const value = readJson(userFile('user-preferences.json'), { LoadingScreen: true, DisabledChampions: [] });
  try { value.AvailablePackages = fs.readdirSync(path.join(root, 'mods')).filter(x => /-(classic|base)\.fantome$/i.test(x)).map(x => x.replace(/\.fantome$/i, '')); }
  catch { value.AvailablePackages = []; }
  return value;
});
ipcMain.handle('rift:save-preferences', (_, value) => { writeJson(userFile('user-preferences.json'), value); writeJson(path.join(root, 'state', 'user-preferences.json'), value); return true; });
ipcMain.handle('rift:pbe-path', () => ({ path: readJson(userFile('config.json')).pbeChampionsDirectory || '' }));
ipcMain.handle('rift:select-pbe-path', () => selectPbePath(false));
ipcMain.handle('rift:load', async () => {
  const config = readJson(userFile('config.json'));
  if (!resolveChampionsDirectory(config.pbeChampionsDirectory)) return { ok: false, message: 'Select a valid PBE folder first.' };
  writeJson(path.join(root, 'config.json'), config);
  const persistentPreferences = readJson(userFile('user-preferences.json'), { LoadingScreen: true, DisabledChampions: [] });
  writeJson(path.join(root, 'state', 'user-preferences.json'), persistentPreferences);
  const resultPath = path.join(root, 'state', 'backend-result.json');
  try { fs.unlinkSync(resultPath); } catch {}
  const result = await new Promise(resolve => execFile(path.join(root, 'RiftLegacyBackend.exe'), ['--backend-load'], { cwd: root, windowsHide: true }, error => resolve(error)));
  if (result) return { ok: false, message: result.message };
  const backend = readJson(resultPath, { ok: false, message: 'LTK backend did not return a result.' });
  if (!backend.ok) return backend;
  const deadline = Date.now() + 120000;
  while (Date.now() < deadline) {
    const running = await new Promise(resolve => execFile('tasklist.exe', ['/FI', 'IMAGENAME eq cslol-host.exe', '/NH'], { windowsHide: true }, (_, out) => resolve(/cslol-host\.exe/i.test(out || ''))));
    if (running) return backend;
    await new Promise(resolve => setTimeout(resolve, 750));
  }
  return { ok: false, message: 'LTK patcher did not become ready in time.' };
});
ipcMain.on('window:minimize', () => window.minimize());
ipcMain.on('window:maximize', () => window.isMaximized() ? window.unmaximize() : window.maximize());
ipcMain.on('window:close', () => window.close());
