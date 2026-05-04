import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { login as loginApi, register as registerApi, getProfile, logout as logoutApi } from '../api/auth'

export const useUserStore = defineStore('user', () => {
  const token = ref(localStorage.getItem('winnas_token') || '')
  const userInfo = ref(JSON.parse(localStorage.getItem('winnas_user') || 'null'))

  const isLoggedIn = computed(() => !!token.value)
  const isAdmin = computed(() => userInfo.value?.role === 'admin')
  const username = computed(() => userInfo.value?.username || '')

  async function login(form) {
    const res = await loginApi(form)
    if (res.success) {
      token.value = res.data.token
      userInfo.value = res.data.user
      localStorage.setItem('winnas_token', res.data.token)
      localStorage.setItem('winnas_user', JSON.stringify(res.data.user))
    }
    return res
  }

  async function register(form) {
    return await registerApi(form)
  }

  async function fetchProfile() {
    const res = await getProfile()
    if (res.success) {
      userInfo.value = res.data
      localStorage.setItem('winnas_user', JSON.stringify(res.data))
    }
    return res
  }

  async function logout() {
    try {
      await logoutApi()
    } catch {}
    token.value = ''
    userInfo.value = null
    localStorage.removeItem('winnas_token')
    localStorage.removeItem('winnas_user')
  }

  return { token, userInfo, isLoggedIn, isAdmin, username, login, register, fetchProfile, logout }
})
