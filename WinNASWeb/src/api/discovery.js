import request from '../utils/request'

export function getPeers() {
  return request.get('/discovery/peers')
}

export function getDiscoveryInfo() {
  return request.get('/discovery/info')
}

export function getPeerInfo(ip, port) {
  return request.get(`/discovery/peer/${ip}/${port}/info`)
}

export function getPeerDirectories(ip, port) {
  return request.get(`/discovery/peer/${ip}/${port}/directories`)
}

export function getPeerFiles(ip, port, dirId, path) {
  let url = `/discovery/peer/${ip}/${port}/files?dirId=${dirId}`
  if (path) url += `&path=${encodeURIComponent(path)}`
  return request.get(url)
}

export function getPeerDownloadUrl(ip, port, dirId, path) {
  let url = `/api/discovery/peer/${ip}/${port}/download?dirId=${dirId}`
  if (path) url += `&path=${encodeURIComponent(path)}`
  return url
}

export function getPeerPreviewUrl(ip, port, dirId, path) {
  let url = `/api/discovery/peer/${ip}/${port}/preview?dirId=${dirId}`
  if (path) url += `&path=${encodeURIComponent(path)}`
  return url
}

export function getPeerStreamUrl(ip, port, dirId, path) {
  let url = `/api/discovery/peer/${ip}/${port}/stream?dirId=${dirId}`
  if (path) url += `&path=${encodeURIComponent(path)}`
  return url
}
