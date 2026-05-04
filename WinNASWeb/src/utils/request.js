import axios from 'axios'
import { ElMessage } from 'element-plus'
import router from '../router'

const request = axios.create({
  baseURL: '/api',
  timeout: 30000
})

let isHandling401 = false

request.interceptors.request.use(
  config => {
    const token = localStorage.getItem('winnas_token')
    if (token) {
      config.headers.Authorization = `Bearer ${token}`
    }
    return config
  },
  error => Promise.reject(error)
)

request.interceptors.response.use(
  response => {
    const res = response.data
    if (res.success === false && res.message) {
      ElMessage.error(res.message)
    }
    return res
  },
  error => {
    if (error.response?.status === 401) {
      if (!isHandling401) {
        isHandling401 = true
        localStorage.removeItem('winnas_token')
        localStorage.removeItem('winnas_user')
        ElMessage.error('登录已过期，请重新登录')
        router.push('/login').finally(() => {
          setTimeout(() => { isHandling401 = false }, 2000)
        })
      }
    } else {
      ElMessage.error(error.response?.data?.message || '网络请求失败')
    }
    return Promise.reject(error)
  }
)

export default request
