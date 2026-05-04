import { defineStore } from 'pinia'
import { ref, watch } from 'vue'

const SIDEBAR_COLORS = [
  {
    name: '午夜',
    bg: 'linear-gradient(180deg, #1c2541 0%, #0b132b 100%)',
    text: '#c5d0e0',
    active: '#5fa8d3',
    menuBg: '#1c2541'
  },
  {
    name: '石墨',
    bg: 'linear-gradient(180deg, #3a3a3c 0%, #1c1c1e 100%)',
    text: '#c8c8c8',
    active: '#989898',
    menuBg: '#2c2c2e'
  },
  {
    name: '紫罗兰',
    bg: 'linear-gradient(180deg, #3a1d6e 0%, #1a0a3e 100%)',
    text: '#d4c4f0',
    active: '#b07cf7',
    menuBg: '#2d1b4e'
  },
  {
    name: '玫瑰金',
    bg: 'linear-gradient(180deg, #4a2040 0%, #2a0e20 100%)',
    text: '#e8c4d0',
    active: '#f472b6',
    menuBg: '#3d1a2a'
  },
  {
    name: '日落',
    bg: 'linear-gradient(180deg, #4a2810 0%, #2a1508 100%)',
    text: '#e8d4c0',
    active: '#f59e0b',
    menuBg: '#3d2211'
  },
  {
    name: '翡翠',
    bg: 'linear-gradient(180deg, #0f3b22 0%, #061a10 100%)',
    text: '#b8dcc8',
    active: '#34d399',
    menuBg: '#0f2b1e'
  },
  {
    name: '深空',
    bg: 'linear-gradient(180deg, #2a2a3e 0%, #12121a 100%)',
    text: '#c8c8d0',
    active: '#7c7cf0',
    menuBg: '#1a1a1e'
  },
  {
    name: '靛蓝',
    bg: 'linear-gradient(180deg, #1a1a50 0%, #0a0a28 100%)',
    text: '#c0c0f0',
    active: '#6366f1',
    menuBg: '#1a1a40'
  }
]

export const useThemeStore = defineStore('theme', () => {
  const isDark = ref(localStorage.getItem('winnas_theme') === 'dark')
  const sidebarColorIndex = ref(parseInt(localStorage.getItem('winnas_sidebar_color') || '0'))

  const sidebarColors = SIDEBAR_COLORS

  function toggleTheme() {
    isDark.value = !isDark.value
  }

  function setSidebarColor(index) {
    sidebarColorIndex.value = index
  }

  watch(isDark, (val) => {
    localStorage.setItem('winnas_theme', val ? 'dark' : 'light')
    if (val) {
      document.documentElement.classList.add('dark')
    } else {
      document.documentElement.classList.remove('dark')
    }
  }, { immediate: true })

  watch(sidebarColorIndex, (val) => {
    localStorage.setItem('winnas_sidebar_color', val.toString())
    applySidebarColor()
  }, { immediate: true })

  function applySidebarColor() {
    const color = SIDEBAR_COLORS[sidebarColorIndex.value] || SIDEBAR_COLORS[0]
    document.documentElement.style.setProperty('--sidebar-bg', color.menuBg)
    document.documentElement.style.setProperty('--sidebar-bg-gradient', color.bg)
    document.documentElement.style.setProperty('--sidebar-text', color.text)
    document.documentElement.style.setProperty('--sidebar-active', color.active)
  }

  return { isDark, toggleTheme, sidebarColorIndex, sidebarColors, setSidebarColor }
})
