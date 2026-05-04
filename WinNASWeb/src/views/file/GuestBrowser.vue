<template>
  <div class="guest-container">
    <div class="guest-header">
      <div class="guest-logo">
        <img src="/WinNAS.ico" style="width:24px;height:24px" />
        <h1>WinNAS</h1>
      </div>
      <el-button type="primary" size="small" @click="$router.push('/login')">登录</el-button>
    </div>

    <div class="guest-body">
      <el-row :gutter="20">
        <el-col :xs="24" :sm="6">
          <el-card shadow="hover" class="dir-list">
            <template #header><span>公开目录</span></template>
            <div v-if="directories.length === 0" class="empty-text">暂无公开目录</div>
            <div v-for="dir in directories" :key="dir.id" class="dir-item" :class="{ active: currentDir?.id === dir.id }" @click="selectDirectory(dir)">
              <el-icon><Folder /></el-icon>
              <span>{{ dir.name }}</span>
            </div>
          </el-card>
        </el-col>

        <el-col :xs="24" :sm="18">
          <el-card shadow="hover" v-if="currentDir">
            <template #header>
              <div class="file-header">
                <span>{{ currentDir.name }}</span>
              </div>
            </template>

            <el-breadcrumb v-if="breadcrumbs.length > 0" separator="/" style="margin-bottom:15px">
              <el-breadcrumb-item @click="navigateTo(null)">根目录</el-breadcrumb-item>
              <el-breadcrumb-item v-for="(crumb, index) in breadcrumbs" :key="index" @click="navigateTo(crumb.path)">{{ crumb.name }}</el-breadcrumb-item>
            </el-breadcrumb>

            <el-table :data="fileList" v-loading="loading" @row-dblclick="handleRowDblClick">
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
              <el-table-column label="操作" :width="isMobile ? '80' : '200'" :fixed="isMobile ? false : 'right'">
                <template #default="{ row }">
                  <template v-if="!isMobile">
                    <el-button v-if="isImageFile(row) || isTextFile(row)" link type="primary" @click="handlePreview(row)">预览</el-button>
                    <el-button v-if="isVideoFile(row) || isAudioFile(row)" link type="success" @click="playMedia(row)">播放</el-button>
                    <el-button v-if="!row.isDirectory" link type="primary" @click="handleDownload(row)">下载</el-button>
                    <el-button v-if="row.isDirectory" link type="primary" @click="handleDownload(row)">下载</el-button>
                  </template>
                  <el-dropdown v-else trigger="click">
                    <el-icon class="mobile-more"><MoreFilled /></el-icon>
                    <template #dropdown>
                      <el-dropdown-menu>
                        <el-dropdown-item v-if="isImageFile(row) || isTextFile(row)" @click="handlePreview(row)"><el-icon><View /></el-icon>预览</el-dropdown-item>
                        <el-dropdown-item v-if="isVideoFile(row) || isAudioFile(row)" @click="playMedia(row)"><el-icon><VideoPlay /></el-icon>播放</el-dropdown-item>
                        <el-dropdown-item @click="handleDownload(row)"><el-icon><Download /></el-icon>下载</el-dropdown-item>
                      </el-dropdown-menu>
                    </template>
                  </el-dropdown>
                </template>
              </el-table-column>
            </el-table>
          </el-card>
          <el-card v-else shadow="hover"><el-empty description="请选择左侧的公开目录" /></el-card>
        </el-col>
      </el-row>
    </div>

    <el-dialog v-model="previewVisible" :title="previewTitle" width="800px" fullscreen>
      <div v-if="previewType === 'image'" class="preview-image-container">
        <img :src="previewSrc" style="max-width:100%;max-height:calc(100vh - 160px);object-fit:contain" />
      </div>
      <div v-else-if="previewType === 'markdown'" class="preview-text-container">
        <div class="md-preview" v-html="previewContent"></div>
      </div>
      <div v-else-if="previewType === 'text'" class="preview-text-container">
        <pre class="preview-text">{{ previewContent }}</pre>
      </div>
    </el-dialog>
  </div>
</template>

<script setup>
import { ref, onMounted, onUnmounted } from 'vue'
import { getGuestDirectories, getGuestFileList, getGuestDownloadUrl, getGuestPreviewUrl, getGuestStreamUrl } from '../../api/file'
import { usePlayerStore } from '../../store/player'
import { marked } from 'marked'
import { ElMessage } from 'element-plus'
import { View, VideoPlay, Download, MoreFilled } from '@element-plus/icons-vue'

const player = usePlayerStore()
const isMobile = ref(window.innerWidth < 768)
const directories = ref([])
const currentDir = ref(null)
const currentDirPath = ref('')
const fileList = ref([])
const loading = ref(false)
const breadcrumbs = ref([])
const currentPath = ref('')

const previewVisible = ref(false)
const previewType = ref('')
const previewTitle = ref('')
const previewSrc = ref('')
const previewContent = ref('')

onMounted(() => { loadDirectories(); window.addEventListener('resize', checkMobile) })
onUnmounted(() => { window.removeEventListener('resize', checkMobile) })

function checkMobile() { isMobile.value = window.innerWidth < 768 }

async function loadDirectories() {
  const res = await getGuestDirectories()
  if (res.success) directories.value = res.data
  else { directories.value = []; ElMessage.warning(res.message || '访客模式未开启') }
}

async function selectDirectory(dir) {
  currentDir.value = dir
  currentDirPath.value = dir.path || ''
  currentPath.value = ''
  breadcrumbs.value = []
  await loadFiles()
}

async function loadFiles() {
  if (!currentDir.value) return
  loading.value = true
  try {
    const res = await getGuestFileList(currentDir.value.id, currentPath.value)
    if (res.success) {
      fileList.value = res.data
      currentDirPath.value = res.dirPath || currentDir.value.path
    }
  } finally { loading.value = false }
}

function handleRowDblClick(row) {
  if (row.isDirectory) {
    currentPath.value = row.relativePath
    breadcrumbs.value.push({ name: row.name, path: row.relativePath })
    loadFiles()
  } else if (isImageFile(row) || isTextFile(row)) {
    handlePreview(row)
  } else if (isVideoFile(row) || isAudioFile(row)) {
    playMedia(row)
  }
}

function navigateTo(path) {
  if (path === null) { currentPath.value = ''; breadcrumbs.value = [] }
  else { currentPath.value = path; const idx = breadcrumbs.value.findIndex(b => b.path === path); breadcrumbs.value = breadcrumbs.value.slice(0, idx + 1) }
  loadFiles()
}

async function handlePreview(row) {
  previewTitle.value = row.name
  if (isImageFile(row)) {
    previewSrc.value = getGuestPreviewUrl(currentDir.value.id, row.relativePath)
    previewType.value = 'image'
    previewContent.value = ''
    previewVisible.value = true
  } else if (isMarkdownFile(row)) {
    try {
      const res = await fetch(getGuestPreviewUrl(currentDir.value.id, row.relativePath))
      const data = await res.json()
      if (data.success) {
        previewContent.value = marked(data.data.content || '')
        previewType.value = 'markdown'
        previewSrc.value = ''
        previewVisible.value = true
      }
    } catch { ElMessage.error('预览失败') }
  } else if (isTextFile(row)) {
    try {
      const res = await fetch(getGuestPreviewUrl(currentDir.value.id, row.relativePath))
      const data = await res.json()
      if (data.success) {
        previewContent.value = data.data.content
        previewType.value = 'text'
        previewSrc.value = ''
        previewVisible.value = true
      }
    } catch { ElMessage.error('预览失败') }
  }
}

async function handleDownload(row) {
  const url = getGuestDownloadUrl(currentDir.value.id, row.relativePath)
  const link = document.createElement('a')
  link.href = url
  link.download = row.isDirectory ? row.name + '.zip' : row.name
  link.click()
}

function playMedia(row) {
  const streamUrl = getGuestStreamUrl(currentDir.value.id, row.relativePath)
  const mediaFiles = fileList.value.filter(f => !f.isDirectory && (isVideoFile(f) || isAudioFile(f)))
  const mediaIndex = mediaFiles.findIndex(f => f.name === row.name)
  const items = mediaFiles.map(f => ({
    url: getGuestStreamUrl(currentDir.value.id, f.relativePath),
    title: f.name,
    isVideo: isVideoFile(f),
    isAudio: isAudioFile(f)
  }))
  player.playList(items, mediaIndex >= 0 ? mediaIndex : 0)
}

function isImageFile(row) { return ['.jpg', '.jpeg', '.png', '.gif', '.bmp', '.webp', '.svg'].includes(row.extension?.toLowerCase() || '') }
function isMarkdownFile(row) { return (row.extension?.toLowerCase() || '') === '.md' }
function isTextFile(row) { return ['.txt', '.log', '.md', '.json', '.xml', '.csv', '.ini', '.cfg', '.conf', '.yml', '.yaml', '.html', '.css', '.js', '.ts', '.py', '.java', '.c', '.cpp', '.h', '.cs', '.go', '.rs', '.sh', '.bat', '.sql'].includes(row.extension?.toLowerCase() || '') }
function isVideoFile(row) { return ['.mp4', '.webm', '.avi', '.mkv', '.mov', '.wmv', '.flv'].includes(row.extension?.toLowerCase() || '') }
function isAudioFile(row) { return ['.mp3', '.wav', '.flac', '.aac', '.ogg', '.m4a', '.wma'].includes(row.extension?.toLowerCase() || '') }

function getFileIcon(row) {
  if (row.isDirectory) return { icon: 'Folder', color: '#e6a23c' }
  if (isImageFile(row)) return { icon: 'Picture', color: '#67c23a' }
  if (isVideoFile(row)) return { icon: 'VideoPlay', color: '#409eff' }
  if (isAudioFile(row)) return { icon: 'Headset', color: '#f56c6c' }
  if (isTextFile(row)) return { icon: 'Document', color: '#409eff' }
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
.guest-container { min-height: 100vh; background: #f0f2f5; }
.guest-header { display: flex; align-items: center; justify-content: space-between; padding: 12px 20px; background: #fff; border-bottom: 1px solid #e6e6e6; box-shadow: 0 1px 4px rgba(0,0,0,0.08); }
.guest-logo { display: flex; align-items: center; gap: 8px; }
.guest-logo h1 { font-size: 20px; color: #303133; margin: 0; }
.guest-body { padding: 20px; }
.dir-list { height: calc(100vh - 160px); overflow-y: auto; }
.dir-item { display: flex; align-items: center; gap: 8px; padding: 10px 12px; cursor: pointer; border-radius: 6px; transition: background 0.3s; }
.dir-item:hover { background: #f5f7fa; }
.dir-item.active { background: #ecf5ff; color: #409eff; }
.dir-item span { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.file-header { display: flex; justify-content: space-between; align-items: center; }
.file-name { display: flex; align-items: center; gap: 8px; }
.file-name-info { display: flex; flex-direction: column; min-width: 0; }
.file-name-text { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.file-meta { font-size: 11px; color: #909399; margin-top: 2px; }
.mobile-more { cursor: pointer; font-size: 18px; color: #606266; }
.empty-text { text-align: center; color: #c0c4cc; padding: 20px; }
.preview-image-container { display: flex; justify-content: center; align-items: center; min-height: 400px; }
.preview-text-container { max-height: calc(100vh - 160px); overflow: auto; }
.preview-text { background: #1e1e1e; color: #d4d4d4; padding: 20px; border-radius: 8px; font-family: 'Consolas', 'Monaco', monospace; font-size: 13px; line-height: 1.6; white-space: pre-wrap; word-wrap: break-word; margin: 0; }
.md-preview { padding: 20px; background: #fff; border-radius: 8px; line-height: 1.8; color: #303133; }
.md-preview h1 { font-size: 24px; font-weight: 700; margin: 20px 0 12px; padding-bottom: 8px; border-bottom: 2px solid #eaecef; }
.md-preview h2 { font-size: 20px; font-weight: 600; margin: 18px 0 10px; padding-bottom: 6px; border-bottom: 1px solid #eaecef; }
.md-preview h3 { font-size: 17px; font-weight: 600; margin: 16px 0 8px; }
.md-preview p { margin: 8px 0; }
.md-preview code { background: #f0f0f0; color: #e6a23c; padding: 2px 6px; border-radius: 3px; font-size: 13px; }
.md-preview pre { background: #1e1e1e; color: #d4d4d4; padding: 16px; border-radius: 6px; overflow-x: auto; margin: 12px 0; }
.md-preview pre code { background: transparent; color: inherit; padding: 0; }
.md-preview blockquote { border-left: 4px solid #409eff; padding: 8px 16px; margin: 12px 0; color: #666; background: #f9f9f9; }
.md-preview table { border-collapse: collapse; width: 100%; margin: 12px 0; }
.md-preview th { background: #f5f7fa; font-weight: 600; padding: 8px 12px; border: 1px solid #ebeef5; text-align: left; }
.md-preview td { padding: 8px 12px; border: 1px solid #ebeef5; }
.md-preview a { color: #409eff; text-decoration: none; }
.md-preview img { max-width: 100%; border-radius: 4px; }

@media (max-width: 768px) {
  .guest-body { padding: 12px; }
  .dir-list { height: auto !important; margin-bottom: 12px; max-height: 200px; }
}
</style>
