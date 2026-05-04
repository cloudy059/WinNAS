<template>
  <div class="page-container">
    <div class="page-header">
      <h2>网上邻居</h2>
      <el-button @click="loadPeers" :loading="loading">刷新</el-button>
    </div>

    <template v-if="!selectedPeer">
      <el-empty v-if="!loading && peers.length === 0" description="未发现局域网内的其他 WinNAS 设备" />
      <div v-else class="peer-grid">
        <el-card
          v-for="peer in peers"
          :key="`${peer.ip}:${peer.port}`"
          shadow="hover"
          class="peer-card"
          @click="selectPeer(peer)"
        >
          <div class="peer-card-content">
            <div class="peer-icon">
              <el-icon :size="40" color="#409eff"><Monitor /></el-icon>
            </div>
            <div class="peer-info">
              <h3>{{ peer.machineName }}</h3>
              <p class="peer-addr">{{ peer.ip }}:{{ peer.port }}</p>
            </div>
            <el-icon class="peer-arrow"><ArrowRight /></el-icon>
          </div>
        </el-card>
      </div>
    </template>

    <template v-else>
      <div class="peer-detail-header">
        <el-button link @click="goBack">
          <el-icon><ArrowLeft /></el-icon> 返回设备列表
        </el-button>
        <span class="peer-detail-title">
          <el-icon><Monitor /></el-icon>
          {{ selectedPeer.machineName }} ({{ selectedPeer.ip }}:{{ selectedPeer.port }})
        </span>
      </div>

      <template v-if="!selectedDir">
        <el-empty v-if="!dirLoading && peerDirectories.length === 0" description="该设备没有公开目录" />
        <div v-else class="dir-grid">
          <el-card
            v-for="dir in peerDirectories"
            :key="dir.id"
            shadow="hover"
            class="dir-card"
            @click="selectDir(dir)"
          >
            <div class="dir-card-content">
              <el-icon :size="36" color="#e6a23c"><Folder /></el-icon>
              <div class="dir-info">
                <h4>{{ dir.name }}</h4>
                <p v-if="dir.description">{{ dir.description }}</p>
              </div>
              <el-icon class="peer-arrow"><ArrowRight /></el-icon>
            </div>
          </el-card>
        </div>
      </template>

      <template v-else>
        <div class="peer-detail-header">
          <el-button link @click="selectedDir = null; currentPath = ''">
            <el-icon><ArrowLeft /></el-icon> 返回目录列表
          </el-button>
          <span class="peer-detail-title">
            <el-icon><Folder /></el-icon>
            {{ selectedDir.name }}
            <span v-if="currentPath" class="path-breadcrumb"> / {{ currentPath }}</span>
          </span>
        </div>

        <el-table :data="peerFiles" v-loading="fileLoading" @row-dblclick="handleRowDblClick">
          <el-table-column label="名称" min-width="200">
            <template #default="{ row }">
              <div class="file-name">
                <el-icon :size="20" :color="getFileIcon(row).color"><component :is="getFileIcon(row).icon" /></el-icon>
                <div class="file-name-info">
                  <span class="file-name-text">{{ row.name }}</span>
                  <span v-if="isMobile" class="file-meta">{{ row.isDirectory ? '' : formatSize(row.size) }} · {{ formatDate(row.lastModified) }}</span>
                </div>
              </div>
            </template>
          </el-table-column>
          <el-table-column v-if="!isMobile" label="大小" width="120">
            <template #default="{ row }">{{ row.isDirectory ? '-' : formatSize(row.size) }}</template>
          </el-table-column>
          <el-table-column v-if="!isMobile" label="修改时间" width="180">
            <template #default="{ row }">{{ formatDate(row.lastModified) }}</template>
          </el-table-column>
          <el-table-column label="操作" :width="isMobile ? '60' : '160'" :fixed="isMobile ? false : 'right'">
            <template #default="{ row }">
              <template v-if="!isMobile">
                <el-button v-if="isImageFile(row) || isTextFile(row)" link type="primary" @click="handlePreview(row)">预览</el-button>
                <el-button v-if="isVideoFile(row) || isAudioFile(row)" link type="success" @click="playMedia(row)">播放</el-button>
                <el-button v-if="!row.isDirectory" link type="primary" @click="handleDownload(row)">下载</el-button>
              </template>
              <el-dropdown v-else trigger="click">
                <el-icon class="mobile-more"><MoreFilled /></el-icon>
                <template #dropdown>
                  <el-dropdown-menu>
                    <el-dropdown-item v-if="isImageFile(row) || isTextFile(row)" @click="handlePreview(row)"><el-icon><View /></el-icon>预览</el-dropdown-item>
                    <el-dropdown-item v-if="isVideoFile(row) || isAudioFile(row)" @click="playMedia(row)"><el-icon><VideoPlay /></el-icon>播放</el-dropdown-item>
                    <el-dropdown-item v-if="!row.isDirectory" @click="handleDownload(row)"><el-icon><Download /></el-icon>下载</el-dropdown-item>
                  </el-dropdown-menu>
                </template>
              </el-dropdown>
            </template>
          </el-table-column>
        </el-table>
      </template>
    </template>

    <el-dialog v-model="previewVisible" :title="previewTitle" width="800px" top="5vh" destroy-on-close>
      <div v-if="previewType === 'image'" class="preview-image-container">
        <img :src="previewUrl" style="max-width:100%;max-height:calc(100vh - 200px);object-fit:contain" />
      </div>
      <div v-else class="preview-text-container">
        <pre class="preview-text">{{ previewContent }}</pre>
      </div>
    </el-dialog>
  </div>
</template>

<script setup>
import { ref, onMounted, onUnmounted } from 'vue'
import { getPeers, getPeerDirectories, getPeerFiles, getPeerDownloadUrl, getPeerPreviewUrl, getPeerStreamUrl } from '../../api/discovery'
import { usePlayerStore } from '../../store/player'
import { ElMessage } from 'element-plus'
import { Monitor, Folder, ArrowRight, ArrowLeft, MoreFilled, View, VideoPlay, Download } from '@element-plus/icons-vue'

const player = usePlayerStore()

const peers = ref([])
const loading = ref(false)
const selectedPeer = ref(null)
const peerDirectories = ref([])
const dirLoading = ref(false)
const selectedDir = ref(null)
const currentPath = ref('')
const peerFiles = ref([])
const fileLoading = ref(false)
const isMobile = ref(window.innerWidth < 768)

const previewVisible = ref(false)
const previewTitle = ref('')
const previewType = ref('')
const previewUrl = ref('')
const previewContent = ref('')

let refreshTimer = null

onMounted(() => {
  loadPeers()
  window.addEventListener('resize', checkMobile)
  refreshTimer = setInterval(loadPeers, 10000)
})

onUnmounted(() => {
  window.removeEventListener('resize', checkMobile)
  if (refreshTimer) clearInterval(refreshTimer)
})

function checkMobile() { isMobile.value = window.innerWidth < 768 }

async function loadPeers() {
  loading.value = true
  try {
    const res = await getPeers()
    if (res.success) peers.value = res.data
  } finally {
    loading.value = false
  }
}

async function selectPeer(peer) {
  selectedPeer.value = peer
  selectedDir.value = null
  currentPath.value = ''
  dirLoading.value = true
  try {
    const res = await getPeerDirectories(peer.ip, peer.port)
    if (res.success) {
      peerDirectories.value = res.data || []
    } else {
      ElMessage.error(res.message || '无法获取目录列表')
    }
  } finally {
    dirLoading.value = false
  }
}

async function selectDir(dir) {
  selectedDir.value = dir
  currentPath.value = ''
  await loadPeerFiles()
}

async function loadPeerFiles() {
  if (!selectedPeer.value || !selectedDir.value) return
  fileLoading.value = true
  try {
    const res = await getPeerFiles(selectedPeer.value.ip, selectedPeer.value.port, selectedDir.value.id, currentPath.value || undefined)
    if (res.success) {
      peerFiles.value = res.data || []
    } else {
      ElMessage.error(res.message || '无法获取文件列表')
    }
  } finally {
    fileLoading.value = false
  }
}

function goBack() {
  selectedPeer.value = null
  selectedDir.value = null
  currentPath.value = ''
  peerDirectories.value = []
  peerFiles.value = []
}

function handleRowDblClick(row) {
  if (row.isDirectory) {
    currentPath.value = currentPath.value ? `${currentPath.value}/${row.name}` : row.name
    loadPeerFiles()
  } else if (isImageFile(row) || isTextFile(row)) {
    handlePreview(row)
  } else if (isVideoFile(row) || isAudioFile(row)) {
    playMedia(row)
  }
}

function handleDownload(row) {
  const url = getPeerDownloadUrl(selectedPeer.value.ip, selectedPeer.value.port, selectedDir.value.id, currentPath.value ? `${currentPath.value}/${row.name}` : row.name)
  window.open(url, '_blank')
}

async function handlePreview(row) {
  const filePath = currentPath.value ? `${currentPath.value}/${row.name}` : row.name
  if (isImageFile(row)) {
    previewType.value = 'image'
    previewUrl.value = getPeerPreviewUrl(selectedPeer.value.ip, selectedPeer.value.port, selectedDir.value.id, filePath)
    previewTitle.value = row.name
    previewVisible.value = true
  } else if (isTextFile(row)) {
    try {
      const url = getPeerPreviewUrl(selectedPeer.value.ip, selectedPeer.value.port, selectedDir.value.id, filePath)
      const resp = await fetch(url)
      if (resp.ok) {
        previewContent.value = await resp.text()
        previewType.value = 'text'
        previewTitle.value = row.name
        previewVisible.value = true
      } else {
        ElMessage.error('预览失败')
      }
    } catch {
      ElMessage.error('预览失败')
    }
  }
}

function playMedia(row) {
  const filePath = currentPath.value ? `${currentPath.value}/${row.name}` : row.name
  const url = getPeerStreamUrl(selectedPeer.value.ip, selectedPeer.value.port, selectedDir.value.id, filePath)
  const mediaFiles = peerFiles.value.filter(f => !f.isDirectory && (isVideoFile(f) || isAudioFile(f)))
  const index = mediaFiles.findIndex(f => f.name === row.name)
  player.playList({
    list: mediaFiles.map(f => ({
      name: f.name,
      url: getPeerStreamUrl(selectedPeer.value.ip, selectedPeer.value.port, selectedDir.value.id, currentPath.value ? `${currentPath.value}/${f.name}` : f.name),
      type: isVideoFile(f) ? 'video' : 'audio'
    })),
    index: index >= 0 ? index : 0
  })
}

function getFileIcon(row) {
  if (row.isDirectory) return { icon: 'Folder', color: '#e6a23c' }
  const ext = row.name.split('.').pop()?.toLowerCase() || ''
  const imageExts = ['jpg', 'jpeg', 'png', 'gif', 'bmp', 'webp', 'svg', 'ico']
  const videoExts = ['mp4', 'webm', 'avi', 'mkv', 'mov', 'wmv', 'flv']
  const audioExts = ['mp3', 'wav', 'flac', 'aac', 'ogg', 'wma', 'm4a']
  const docExts = ['pdf', 'doc', 'docx', 'xls', 'xlsx', 'ppt', 'pptx']
  const codeExts = ['js', 'ts', 'py', 'java', 'c', 'cpp', 'cs', 'html', 'css', 'json', 'xml', 'yaml', 'yml', 'md', 'sh', 'go', 'rs', 'rb', 'php']
  if (imageExts.includes(ext)) return { icon: 'Picture', color: '#67c23a' }
  if (videoExts.includes(ext)) return { icon: 'VideoPlay', color: '#f56c6c' }
  if (audioExts.includes(ext)) return { icon: 'Headset', color: '#e6a23c' }
  if (docExts.includes(ext)) return { icon: 'Document', color: '#409eff' }
  if (codeExts.includes(ext)) return { icon: 'DocumentCopy', color: '#909399' }
  if (ext === 'zip' || ext === 'rar' || ext === '7z' || ext === 'tar' || ext === 'gz') return { icon: 'Box', color: '#f56c6c' }
  return { icon: 'Document', color: '#909399' }
}

function isImageFile(row) {
  const ext = row.name.split('.').pop()?.toLowerCase() || ''
  return ['jpg', 'jpeg', 'png', 'gif', 'bmp', 'webp', 'svg', 'ico'].includes(ext)
}

function isVideoFile(row) {
  const ext = row.name.split('.').pop()?.toLowerCase() || ''
  return ['mp4', 'webm', 'avi', 'mkv', 'mov', 'wmv', 'flv'].includes(ext)
}

function isAudioFile(row) {
  const ext = row.name.split('.').pop()?.toLowerCase() || ''
  return ['mp3', 'wav', 'flac', 'aac', 'ogg', 'wma', 'm4a'].includes(ext)
}

function isTextFile(row) {
  const ext = row.name.split('.').pop()?.toLowerCase() || ''
  return ['txt', 'log', 'json', 'xml', 'csv', 'yaml', 'yml', 'md', 'html', 'css', 'js', 'ts', 'py', 'java', 'c', 'cpp', 'cs', 'sh', 'go', 'rs', 'rb', 'php', 'ini', 'cfg', 'conf', 'env', 'sql'].includes(ext)
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
.peer-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(320px, 1fr));
  gap: 16px;
}

.peer-card {
  cursor: pointer;
  transition: transform 0.2s, box-shadow 0.2s;
}

.peer-card:hover {
  transform: translateY(-2px);
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.1);
}

.peer-card-content {
  display: flex;
  align-items: center;
  gap: 16px;
}

.peer-icon {
  flex-shrink: 0;
  width: 56px;
  height: 56px;
  border-radius: 12px;
  background: linear-gradient(135deg, #e8f4fd 0%, #d1ecfa 100%);
  display: flex;
  align-items: center;
  justify-content: center;
}

.peer-info {
  flex: 1;
  min-width: 0;
}

.peer-info h3 {
  margin: 0 0 4px;
  font-size: 16px;
  color: var(--el-text-color-primary);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.peer-addr {
  margin: 0;
  font-size: 13px;
  color: var(--el-text-color-secondary);
}

.peer-arrow {
  color: var(--el-text-color-placeholder);
  font-size: 16px;
  flex-shrink: 0;
}

.dir-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(280px, 1fr));
  gap: 16px;
}

.dir-card {
  cursor: pointer;
  transition: transform 0.2s, box-shadow 0.2s;
}

.dir-card:hover {
  transform: translateY(-2px);
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.1);
}

.dir-card-content {
  display: flex;
  align-items: center;
  gap: 12px;
}

.dir-info {
  flex: 1;
  min-width: 0;
}

.dir-info h4 {
  margin: 0 0 4px;
  font-size: 15px;
  color: var(--el-text-color-primary);
}

.dir-info p {
  margin: 0;
  font-size: 12px;
  color: var(--el-text-color-secondary);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.peer-detail-header {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-bottom: 16px;
  padding-bottom: 12px;
  border-bottom: 1px solid var(--el-border-color-lighter);
}

.peer-detail-title {
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 15px;
  color: var(--el-text-color-primary);
  font-weight: 500;
}

.path-breadcrumb {
  color: var(--el-text-color-secondary);
  font-weight: 400;
}

.file-name {
  display: flex;
  align-items: center;
  gap: 8px;
}

.file-name-info {
  display: flex;
  flex-direction: column;
  min-width: 0;
}

.file-name-text {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.file-meta {
  font-size: 11px;
  color: var(--el-text-color-secondary);
  margin-top: 2px;
}

.mobile-more {
  cursor: pointer;
  font-size: 18px;
  color: var(--el-text-color-regular);
}

.preview-image-container {
  display: flex;
  justify-content: center;
  align-items: center;
  min-height: 400px;
}

.preview-text-container {
  max-height: calc(100vh - 160px);
  overflow: auto;
}

.preview-text {
  background: #1e1e1e;
  color: #d4d4d4;
  padding: 20px;
  border-radius: 8px;
  font-family: 'Consolas', 'Monaco', 'Courier New', monospace;
  font-size: 13px;
  line-height: 1.6;
  white-space: pre-wrap;
  word-wrap: break-word;
  margin: 0;
}

@media (max-width: 768px) {
  .peer-grid {
    grid-template-columns: 1fr;
  }

  .dir-grid {
    grid-template-columns: 1fr;
  }

  .el-table {
    font-size: 12px;
  }
}
</style>
