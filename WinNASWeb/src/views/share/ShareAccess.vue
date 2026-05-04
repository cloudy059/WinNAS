<template>
  <div class="share-access-container">
    <el-card class="share-card" v-if="!accessed">
      <div class="share-header">
        <el-icon :size="48" color="#409eff"><Folder /></el-icon>
        <h2>WinNAS 文件分享</h2>
        <p>分享码: {{ code }}</p>
      </div>
      <div v-if="shareInfo?.needPassword" style="margin-top: 20px">
        <el-input v-model="password" placeholder="请输入访问密码" type="password" show-password @keyup.enter="handleAccess" />
      </div>
      <el-button type="primary" size="large" style="width: 100%; margin-top: 20px" @click="handleAccess" :loading="loading">
        访问分享
      </el-button>
    </el-card>

    <el-card v-else-if="isFileShare" style="width: 100%; max-width: 600px">
      <div class="file-download-card">
        <el-icon :size="64" color="#409eff"><Document /></el-icon>
        <h2>{{ fileName }}</h2>
        <p class="file-size-text">{{ formatSize(fileSize) }}</p>
        <el-button type="primary" size="large" @click="handleDirectDownload" :loading="downloading" style="margin-top: 20px">
          <el-icon><Download /></el-icon> 下载文件
        </el-button>
      </div>
    </el-card>

    <el-card v-else style="width: 100%; max-width: 900px">
      <template #header>
        <div class="file-header">
          <span>分享文件</span>
          <div>
            <el-button type="primary" @click="handleDownloadAll" :loading="downloading">
              <el-icon><Download /></el-icon> 下载全部
            </el-button>
            <el-button @click="accessed = false; files = []">返回</el-button>
          </div>
        </div>
      </template>
      <el-table :data="files" v-loading="loading">
        <el-table-column label="名称" min-width="300">
          <template #default="{ row }">
            <div style="display: flex; align-items: center; gap: 8px">
              <el-icon :size="18" :color="getFileIcon(row).color">
                <component :is="getFileIcon(row).icon" />
              </el-icon>
              <span>{{ row.name }}</span>
            </div>
          </template>
        </el-table-column>
        <el-table-column label="大小" width="120">
          <template #default="{ row }">
            {{ row.isDirectory ? '-' : formatSize(row.size) }}
          </template>
        </el-table-column>
        <el-table-column label="修改时间" width="180">
          <template #default="{ row }">
            {{ formatDate(row.lastModified) }}
          </template>
        </el-table-column>
        <el-table-column label="操作" width="100">
          <template #default="{ row }">
            <el-button link type="primary" @click="handleDownload(row)" :loading="downloadingId === row.name">下载</el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>
  </div>
</template>

<script setup>
import { ref, onMounted } from 'vue'
import { useRoute } from 'vue-router'
import { getShareInfo, accessShare, downloadShareFile } from '../../api/share'
import { ElMessage } from 'element-plus'
import axios from 'axios'

const route = useRoute()
const code = route.params.code
const shareInfo = ref(null)
const password = ref('')
const accessed = ref(false)
const files = ref([])
const loading = ref(false)
const downloadingId = ref('')
const isFileShare = ref(false)
const fileName = ref('')
const fileSize = ref(0)
const downloading = ref(false)

onMounted(async () => {
  try {
    const res = await getShareInfo(code)
    if (res.success) shareInfo.value = res.data
  } catch {
    ElMessage.error('分享不存在或已过期')
  }
})

async function handleAccess() {
  loading.value = true
  try {
    const res = await accessShare(code, password.value)
    if (res.success) {
      accessed.value = true
      isFileShare.value = res.data.isFile === true
      fileName.value = res.data.fileName || ''
      fileSize.value = res.data.fileSize || 0
      files.value = res.data.files || []
    }
  } finally {
    loading.value = false
  }
}

async function handleDirectDownload() {
  downloading.value = true
  try {
    const params = { path: '' }
    if (password.value) params.password = password.value

    const response = await axios.get(`/api/shares/${code}/download`, {
      params,
      responseType: 'blob'
    })

    const blob = new Blob([response.data])
    const url = window.URL.createObjectURL(blob)
    const link = document.createElement('a')
    link.href = url
    link.download = fileName.value
    document.body.appendChild(link)
    link.click()
    document.body.removeChild(link)
    window.URL.revokeObjectURL(url)
  } catch (e) {
    ElMessage.error('下载失败: ' + (e.response?.statusText || e.message))
  } finally {
    downloading.value = false
  }
}

async function handleDownload(row) {
  downloadingId.value = row.name
  try {
    const response = await axios.get(`/api/shares/${code}/download`, {
      params: { path: row.relativePath, password: password.value || undefined },
      responseType: 'blob'
    })

    const blob = new Blob([response.data])
    const url = window.URL.createObjectURL(blob)
    const link = document.createElement('a')
    link.href = url
    if (row.isDirectory) {
      link.download = row.name + '.zip'
    } else {
      link.download = row.name
    }
    document.body.appendChild(link)
    link.click()
    document.body.removeChild(link)
    window.URL.revokeObjectURL(url)
  } catch (e) {
    ElMessage.error('下载失败: ' + (e.response?.statusText || e.message))
  } finally {
    downloadingId.value = ''
  }
}

async function handleDownloadAll() {
  downloading.value = true
  try {
    const response = await axios.get(`/api/shares/${code}/download`, {
      params: { password: password.value || undefined },
      responseType: 'blob'
    })

    const blob = new Blob([response.data])
    const url = window.URL.createObjectURL(blob)
    const link = document.createElement('a')
    link.href = url
    link.download = 'share.zip'
    document.body.appendChild(link)
    link.click()
    document.body.removeChild(link)
    window.URL.revokeObjectURL(url)
  } catch (e) {
    ElMessage.error('下载失败: ' + (e.response?.statusText || e.message))
  } finally {
    downloading.value = false
  }
}

function getFileIcon(row) {
  if (row.isDirectory) return { icon: 'Folder', color: '#e6a23c' }
  const ext = row.extension?.toLowerCase() || ''
  if (['.jpg', '.jpeg', '.png', '.gif', '.bmp', '.webp'].includes(ext)) return { icon: 'Picture', color: '#67c23a' }
  if (['.mp4', '.avi', '.mkv', '.mov'].includes(ext)) return { icon: 'VideoPlay', color: '#409eff' }
  if (['.mp3', '.wav', '.flac'].includes(ext)) return { icon: 'Headset', color: '#f56c6c' }
  return { icon: 'Document', color: '#909399' }
}

function formatSize(bytes) {
  if (!bytes) return '0 B'
  const units = ['B', 'KB', 'MB', 'GB', 'TB']
  const i = Math.floor(Math.log(bytes) / Math.log(1024))
  return (bytes / Math.pow(1024, i)).toFixed(1) + ' ' + units[i]
}

function formatDate(dateStr) {
  if (!dateStr) return ''
  return new Date(dateStr).toLocaleString('zh-CN')
}
</script>

<style scoped>
.share-access-container {
  min-height: 100vh;
  display: flex;
  align-items: center;
  justify-content: center;
  background: #f0f2f5;
  padding: 20px;
}

.share-card {
  width: 400px;
}

.share-header {
  text-align: center;
}

.share-header h2 {
  margin: 15px 0 5px;
  color: #303133;
}

.share-header p {
  color: #909399;
  font-size: 14px;
}

.file-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.file-download-card {
  text-align: center;
  padding: 40px 20px;
}

.file-download-card h2 {
  margin: 20px 0 8px;
  color: #303133;
  word-break: break-all;
}

.file-size-text {
  color: #909399;
  font-size: 14px;
}
</style>
