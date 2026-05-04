import request from '../utils/request'

export function searchFiles(keyword, ext, dirId) {
  return request.get('/files/search', { params: { keyword, ext, dirId } })
}

export function getRecycleList() {
  return request.get('/recycle/list')
}

export function restoreFromRecycle(id) {
  return request.post('/recycle/restore', { id })
}

export function purgeFromRecycle(id) {
  return request.delete('/recycle/purge', { data: { id } })
}

export function emptyRecycle() {
  return request.delete('/recycle/empty')
}

export function getFavorites() {
  return request.get('/favorites/list')
}

export function addFavorite(dirId, filePath, fileName, isDirectory) {
  return request.post('/favorites/add', { directoryId: dirId, filePath, fileName, isDirectory })
}

export function removeFavorite(dirId, filePath) {
  return request.delete('/favorites/remove', { data: { directoryId: dirId, filePath } })
}

export function checkFavorite(dirId, path) {
  return request.get('/favorites/check', { params: { dirId, path } })
}

export function getRecentList(limit) {
  return request.get('/recent/list', { params: { limit } })
}

export function recordRecent(dirId, filePath, fileName, accessType) {
  return request.post('/recent/record', { directoryId: dirId, filePath, fileName, accessType })
}

export function getFileTags(dirId, path) {
  return request.get('/tags/list', { params: { dirId, path } })
}

export function batchGetFileTags(dirId) {
  return request.get('/tags/batch', { params: { dirId } })
}

export function setFileTag(dirId, filePath, tag, color, note) {
  return request.post('/tags/set', { directoryId: dirId, filePath, tag, color, note })
}

export function batchDelete(files) {
  return request.post('/files/batch-delete', { files })
}

export function batchDownload(files) {
  return request.post('/files/batch-download', { files }, { responseType: 'blob' })
}

export function batchMove(files, targetDirectoryId, targetSubPath) {
  return request.post('/files/batch-move', { files, targetDirectoryId, targetSubPath })
}

export function getDownloadStats(limit) {
  return request.get('/stats/downloads', { params: { limit } })
}

export function compressFiles(directoryId, targetPath, zipName, files) {
  return request.post('/files/compress', { directoryId, targetPath, zipName, files })
}

export function extractFile(directoryId, filePath, targetPath) {
  return request.post('/files/extract', { directoryId, filePath, targetPath })
}

export function copyFiles(sourceDirectoryId, targetDirectoryId, targetSubPath, relativePaths) {
  return request.post('/files/copy', { sourceDirectoryId, targetDirectoryId, targetSubPath, relativePaths })
}

export function getNotifications() {
  return request.get('/notifications/list')
}

export function readNotification(id) {
  return request.post('/notifications/read', { id })
}

export function clearNotifications() {
  return request.delete('/notifications/clear')
}

export function createBackup() {
  return request.post('/backup/create')
}

export function restoreBackup(formData) {
  return request.post('/backup/restore', formData, { headers: { 'Content-Type': 'multipart/form-data' } })
}

export function getBackupList() {
  return request.get('/backup/list')
}

export function downloadBackup(name) {
  return request.get('/backup/download', { params: { name }, responseType: 'blob' })
}

export function deleteBackup(name) {
  return request.delete('/backup/delete', { params: { name } })
}
