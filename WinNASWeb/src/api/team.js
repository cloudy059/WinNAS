import request from '../utils/request'

export function getTeams() {
  return request.get('/teams')
}

export function createTeam(data) {
  return request.post('/teams', data)
}

export function getTeam(id) {
  return request.get(`/teams/${id}`)
}

export function updateTeam(id, data) {
  return request.put(`/teams/${id}`, data)
}

export function deleteTeam(id) {
  return request.delete(`/teams/${id}`)
}

export function getTeamMembers(teamId) {
  return request.get(`/teams/${teamId}/members`)
}

export function addMember(teamId, data) {
  return request.post(`/teams/${teamId}/members`, data)
}

export function removeMember(teamId, userId) {
  return request.delete(`/teams/${teamId}/members/${userId}`)
}

export function updateMemberRole(teamId, userId, role) {
  return request.put(`/teams/${teamId}/members/${userId}/role`, { role })
}

export function getTeamFiles(teamId, path) {
  return request.get(`/teams/${teamId}/files`, { params: { path } })
}

export function uploadTeamFile(teamId, path, formData, onProgress) {
  return request.post(`/teams/${teamId}/files/upload`, formData, {
    params: { path },
    headers: { 'Content-Type': 'multipart/form-data' },
    onUploadProgress: onProgress
  })
}

export function downloadTeamFile(teamId, fileId) {
  return request.get(`/teams/${teamId}/files/${fileId}/download`, {
    responseType: 'blob'
  })
}

export function createTeamFolder(teamId, data) {
  return request.post(`/teams/${teamId}/files/folder`, data)
}

export function renameTeamFile(teamId, fileId, newName) {
  return request.put(`/teams/${teamId}/files/${fileId}/rename`, { newName })
}

export function deleteTeamFile(teamId, fileId) {
  return request.delete(`/teams/${teamId}/files/${fileId}`)
}

export function lockTeamFile(teamId, fileId) {
  return request.put(`/teams/${teamId}/files/${fileId}/lock`)
}

export function unlockTeamFile(teamId, fileId) {
  return request.put(`/teams/${teamId}/files/${fileId}/unlock`)
}

export function getFileVersions(teamId, fileId) {
  return request.get(`/teams/${teamId}/files/${fileId}/versions`)
}

export function editDocument(teamId, fileId, mode = 'edit') {
  return request.post(`/teams/${teamId}/files/${fileId}/edit`, { mode })
}

export function getTeamStreamUrl(teamId, fileId) {
  return `/api/teams/${teamId}/files/${fileId}/stream`
}

export function getTeamPreviewUrl(teamId, fileId) {
  return `/api/teams/${teamId}/files/${fileId}/preview`
}

export function previewTeamFile(teamId, fileId) {
  return request.get(`/teams/${teamId}/files/${fileId}/preview`)
}

export function getChatHistory(params) {
  return request.get('/chat/history', { params })
}

export function sendMessage(data) {
  return request.post('/chat/send', data)
}
