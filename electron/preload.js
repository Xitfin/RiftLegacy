const { contextBridge, ipcRenderer } = require('electron');

contextBridge.exposeInMainWorld('rift', {
  state: () => ipcRenderer.invoke('rift:state'),
  updateState: () => ipcRenderer.invoke('rift:update-state'),
  onUpdateStatus: callback => ipcRenderer.on('rift:update-status', (_, value) => callback(value)),
  load: () => ipcRenderer.invoke('rift:load'),
  preferences: () => ipcRenderer.invoke('rift:preferences'),
  savePreferences: value => ipcRenderer.invoke('rift:save-preferences', value),
  pbePath: () => ipcRenderer.invoke('rift:pbe-path'),
  selectPbePath: () => ipcRenderer.invoke('rift:select-pbe-path'),
  minimize: () => ipcRenderer.send('window:minimize'),
  maximize: () => ipcRenderer.send('window:maximize'),
  close: () => ipcRenderer.send('window:close')
});
