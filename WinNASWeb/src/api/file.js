import request from '../utils/request'

export function getDirectories() {
  return request.get('/files/directories')
}

export function createDirectory(data) {
  return request.post('/files/directories', data)
}

export function updateDirectory(id, data) {
  return request.put(`/files/directories/${id}`, data)
}

export function deleteDirectory(id) {
  return request.delete(`/files/directories/${id}`)
}

export function getFileList(dirId, path) {
  return request.get('/files/list', { params: { dirId, path } })
}

export function uploadFile(dirId, path, formData, onProgress) {
  return request.post('/files/upload', formData, {
    params: { dirId, path },
    headers: { 'Content-Type': 'multipart/form-data' },
    onUploadProgress: onProgress
  })
}

export function downloadFile(dirId, path) {
  return request.get('/files/download', {
    params: { dirId, path },
    responseType: 'blob'
  })
}

export function getStreamUrl(dirId, path) {
  return `/api/files/stream?dirId=${dirId}&path=${encodeURIComponent(path)}`
}

export function renameFile(data) {
  return request.put('/files/rename', data)
}

export function deleteFile(data) {
  return request.delete('/files/delete', { data })
}

export function moveFile(data) {
  return request.put('/files/move', data)
}

export function shareFile(data) {
  return request.post('/files/share', data)
}

export function setPermission(data) {
  return request.post('/files/permissions', data)
}

export function getPreviewUrl(dirId, path) {
  return `/api/files/preview?dirId=${dirId}&path=${encodeURIComponent(path)}`
}

export function previewFile(dirId, path) {
  return request.get('/files/preview', { params: { dirId, path } })
}

export function createFolder(dirId, path, name) {
  return request.post('/files/folder', { dirId, path, name })
}

export function lockFile(directoryId, filePath) {
  return request.post('/files/lock', { directoryId, filePath })
}

export function unlockFile(directoryId, filePath) {
  return request.delete('/files/lock', { data: { directoryId, filePath } })
}

export function getFileLocks(dirId) {
  return request.get('/files/locks', { params: { dirId } })
}

export function getGuestDirectories() {
  return request.get('/guest/directories')
}

export function getGuestFileList(dirId, path) {
  return request.get('/guest/files', { params: { dirId, path } })
}

export function getGuestDownloadUrl(dirId, path) {
  return `/api/guest/download?dirId=${dirId}&path=${encodeURIComponent(path)}`
}

export function getGuestPreviewUrl(dirId, path) {
  return `/api/guest/preview?dirId=${dirId}&path=${encodeURIComponent(path)}`
}

export function getGuestStreamUrl(dirId, path) {
  return `/api/guest/stream?dirId=${dirId}&path=${encodeURIComponent(path)}`
}
