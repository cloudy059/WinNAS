import request from '../utils/request'

export function getServerAddress() {
  return request.get('/server/address')
}

export function getSystemInfo() {
  return request.get('/system/info')
}

export function getNetworks() {
  return request.get('/system/networks')
}

export function getConfigs() {
  return request.get('/system/configs')
}

export function updateConfig(data) {
  return request.put('/system/configs', data)
}

export function getLogs(params) {
  return request.get('/system/logs', { params })
}

export function getTraffic(hours) {
  return request.get('/system/traffic', { params: { hours } })
}

export function setAutoStart(enable) {
  return request.post('/system/autostart', { enable })
}

export function getAnnouncements() {
  return request.get('/announcements')
}

export function createAnnouncement(data) {
  return request.post('/announcements', data)
}

export function deleteAnnouncement(id) {
  return request.delete(`/announcements/${id}`)
}

export function updateAnnouncement(id, data) {
  return request.put(`/announcements/${id}`, data)
}

export function getStats() {
  return request.get('/system/stats')
}

export function resetSystem(confirmCode) {
  return request.post('/system/reset', { confirmCode })
}

export function restartService() {
  return request.post('/system/restart')
}

export function getNetworkShareStatus() {
  return request.get('/network-share/status')
}

export function toggleSmbShare(id, enabled) {
  return request.post(`/network-share/toggle-smb/${id}`, { enabled })
}

export function toggleWebDavShare(id, enabled) {
  return request.post(`/network-share/toggle-webdav/${id}`, { enabled })
}

export function toggleWebDavGlobal(enabled) {
  return request.post('/network-share/webdav/toggle', { enabled })
}

export function enableAllSmbShare() {
  return request.post('/network-share/smb/enable-all')
}

export function disableAllSmbShare() {
  return request.post('/network-share/smb/disable-all')
}
