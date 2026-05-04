import request from '../utils/request'

export function createShare(data) {
  return request.post('/shares', data)
}

export function getShares() {
  return request.get('/shares')
}

export function getShareInfo(code) {
  return request.get(`/shares/${code}`)
}

export function accessShare(code, password) {
  return request.post(`/shares/${code}/access`, { password })
}

export function downloadShareFile(code, path, password) {
  return request.get(`/shares/${code}/download`, {
    params: { path, password },
    responseType: 'blob'
  })
}

export function deleteShare(id) {
  return request.delete(`/shares/${id}`)
}

export function disableShare(id) {
  return request.put(`/shares/${id}/disable`)
}

export function enableShare(id) {
  return request.put(`/shares/${id}/enable`)
}
