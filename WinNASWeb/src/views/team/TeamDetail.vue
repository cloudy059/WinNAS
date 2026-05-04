<template>
  <div class="page-container">
    <div class="page-header">
      <h2>{{ team.name }}</h2>
      <el-button @click="$router.push('/teams')">返回团队列表</el-button>
    </div>

    <el-tabs v-model="activeTab">
      <el-tab-pane label="团队文件" name="files">
        <el-card shadow="hover">
          <div class="tab-header">
            <el-breadcrumb v-if="breadcrumbs.length > 0" separator="/" style="flex:1">
              <el-breadcrumb-item @click="navigateTo(null)">根目录</el-breadcrumb-item>
              <el-breadcrumb-item v-for="(crumb, idx) in breadcrumbs" :key="idx" @click="navigateTo(crumb.path)">{{ crumb.name }}</el-breadcrumb-item>
            </el-breadcrumb>
            <div class="tab-actions">
              <el-button @click="showNewFolderDialog"><el-icon><FolderAdd /></el-icon> 新建文件夹</el-button>
              <el-button icon="Upload" :loading="uploading" @click="triggerFolderUpload">上传文件夹</el-button>
              <el-upload :show-file-list="false" :http-request="handleUpload" multiple accept="*/*">
                <el-button type="primary" icon="Upload" :loading="uploading">上传文件</el-button>
              </el-upload>
            </div>
          </div>

          <el-progress v-if="uploading" :percentage="uploadProgress" :stroke-width="4" style="margin-bottom:12px" />

          <el-table :data="files" v-loading="loading" @row-dblclick="handleRowDblClick">
            <el-table-column label="名称" min-width="200">
              <template #default="{ row }">
                <div class="file-name">
                  <el-icon :size="20" :color="getFileIcon(row).color"><component :is="getFileIcon(row).icon" /></el-icon>
                  <div class="file-name-info">
                    <span class="file-name-text">{{ row.name }}</span>
                    <span v-if="isMobile" class="file-meta">{{ row.mimeType === 'folder' ? '' : formatSize(row.size) }} · {{ formatDate(row.lastModified) }}</span>
                  </div>
                </div>
              </template>
            </el-table-column>
            <el-table-column v-if="!isMobile" label="大小" width="120">
              <template #default="{ row }">{{ row.mimeType === 'folder' ? '-' : formatSize(row.size) }}</template>
            </el-table-column>
            <el-table-column v-if="!isMobile" label="修改时间" width="180">
              <template #default="{ row }">{{ formatDate(row.lastModified) }}</template>
            </el-table-column>
            <el-table-column label="操作" :width="isMobile ? '60' : '280'" :fixed="isMobile ? false : 'right'">
              <template #default="{ row }">
                <template v-if="!isMobile">
                  <el-button v-if="isImageFile(row)" link type="primary" @click="handlePreview(row)">预览</el-button>
                  <el-button v-if="isTextFile(row)" link type="primary" @click="handlePreview(row)">预览</el-button>
                  <el-button v-if="isVideoFile(row)" link type="success" @click="playMedia(row)">播放</el-button>
                  <el-button v-if="isAudioFile(row)" link type="success" @click="playMedia(row)">播放</el-button>
                  <el-button v-if="row.mimeType !== 'folder'" link type="primary" @click="handleDownload(row)">下载</el-button>
                  <el-button v-if="row.mimeType === 'folder'" link type="primary" @click="handleDownload(row)">下载</el-button>
                  <el-button link type="danger" @click="handleDelete(row)">删除</el-button>
                </template>
                <el-dropdown v-else trigger="click">
                  <el-icon class="mobile-more"><MoreFilled /></el-icon>
                  <template #dropdown>
                    <el-dropdown-menu>
                      <el-dropdown-item v-if="isImageFile(row) || isTextFile(row)" @click="handlePreview(row)"><el-icon><View /></el-icon>预览</el-dropdown-item>
                      <el-dropdown-item v-if="isVideoFile(row) || isAudioFile(row)" @click="playMedia(row)"><el-icon><VideoPlay /></el-icon>播放</el-dropdown-item>
                      <el-dropdown-item @click="handleDownload(row)"><el-icon><Download /></el-icon>下载</el-dropdown-item>
                      <el-dropdown-item @click="handleDelete(row)" style="color:#f56c6c"><el-icon><Delete /></el-icon>删除</el-dropdown-item>
                    </el-dropdown-menu>
                  </template>
                </el-dropdown>
              </template>
            </el-table-column>
          </el-table>
        </el-card>
      </el-tab-pane>

      <el-tab-pane label="团队成员" name="members">
        <el-card shadow="hover">
          <template #header>
            <div style="display:flex;justify-content:space-between;align-items:center">
              <span>团队成员</span>
              <el-button type="primary" size="small" @click="showAddMemberDialog">添加成员</el-button>
            </div>
          </template>
          <el-table :data="members">
            <el-table-column label="用户名" prop="username" />
            <el-table-column label="昵称" prop="nickname" />
            <el-table-column label="角色" width="120">
              <template #default="{ row }">
                <el-tag :type="row.role === 'owner' ? 'warning' : row.role === 'admin' ? '' : 'info'" size="small">{{ row.role === 'owner' ? '创建者' : row.role === 'admin' ? '管理员' : '成员' }}</el-tag>
              </template>
            </el-table-column>
            <el-table-column label="加入时间" width="180">
              <template #default="{ row }">{{ formatDate(row.joinedAt) }}</template>
            </el-table-column>
            <el-table-column label="操作" width="120">
              <template #default="{ row }">
                <el-button v-if="row.role !== 'owner'" link type="danger" @click="handleRemoveMember(row)">移除</el-button>
              </template>
            </el-table-column>
          </el-table>
        </el-card>
      </el-tab-pane>

      <el-tab-pane label="团队信息" name="info">
        <el-card shadow="hover">
          <el-descriptions :column="2" border>
            <el-descriptions-item label="团队名称">{{ team.name }}</el-descriptions-item>
            <el-descriptions-item label="创建者">{{ team.creatorName || '未知' }}</el-descriptions-item>
            <el-descriptions-item label="描述" :span="2">{{ team.description || '暂无描述' }}</el-descriptions-item>
            <el-descriptions-item label="存储路径">{{ team.storagePath || '默认路径' }}</el-descriptions-item>
            <el-descriptions-item label="创建时间">{{ formatDate(team.createdAt) }}</el-descriptions-item>
          </el-descriptions>
        </el-card>
      </el-tab-pane>
    </el-tabs>

    <el-dialog v-model="newFolderVisible" title="新建文件夹" width="400px">
      <el-input v-model="newFolderName" placeholder="请输入文件夹名称" />
      <template #footer>
        <el-button @click="newFolderVisible = false">取消</el-button>
        <el-button type="primary" @click="handleCreateFolder">创建</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="addMemberVisible" title="添加成员" width="500px">
      <el-select v-model="selectedMemberIds" multiple filterable placeholder="选择用户" style="width:100%">
        <el-option v-for="u in allUsers" :key="u.id" :label="`${u.nickname || u.username} (${u.username})`" :value="u.id" />
      </el-select>
      <template #footer>
        <el-button @click="addMemberVisible = false">取消</el-button>
        <el-button type="primary" @click="handleAddMembers">添加</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="previewVisible" :title="previewTitle" width="800px" fullscreen>
      <div v-if="previewType === 'image'" class="preview-image-container">
        <img :src="previewSrc" style="max-width:100%;max-height:calc(100vh - 160px);object-fit:contain" />
      </div>
      <div v-else-if="previewType === 'text'" class="preview-text-container">
        <pre class="preview-text">{{ previewContent }}</pre>
      </div>
    </el-dialog>
  </div>
</template>

<script setup>
import { ref, onMounted, onUnmounted } from 'vue'
import { useRoute } from 'vue-router'
import { getTeam, getTeamFiles, uploadTeamFile, deleteTeamFile, createTeamFolder, getTeamStreamUrl, previewTeamFile, getTeamMembers, addMember, removeMember } from '../../api/team'
import { getUserList } from '../../api/auth'
import { usePlayerStore } from '../../store/player'
import { ElMessage, ElMessageBox } from 'element-plus'
import { MoreFilled, View, VideoPlay, Download, Delete } from '@element-plus/icons-vue'

const route = useRoute()
const player = usePlayerStore()
const teamId = parseInt(route.params.id)
const isMobile = ref(window.innerWidth < 768)

const team = ref({})
const files = ref([])
const members = ref([])
const allUsers = ref([])
const loading = ref(false)
const activeTab = ref('files')
const currentPath = ref('')
const breadcrumbs = ref([])
const uploading = ref(false)
const uploadProgress = ref(0)

const newFolderVisible = ref(false)
const newFolderName = ref('')

const addMemberVisible = ref(false)
const selectedMemberIds = ref([])

const previewVisible = ref(false)
const previewType = ref('')
const previewTitle = ref('')
const previewSrc = ref('')
const previewContent = ref('')

onMounted(() => { loadTeam(); loadFiles(); loadMembers(); loadAllUsers(); window.addEventListener('resize', checkMobile) })
onUnmounted(() => { window.removeEventListener('resize', checkMobile) })

function checkMobile() { isMobile.value = window.innerWidth < 768 }

async function loadTeam() {
  const res = await getTeam(teamId)
  if (res.success) team.value = res.data
}

async function loadFiles() {
  loading.value = true
  try {
    const res = await getTeamFiles(teamId, currentPath.value)
    if (res.success) files.value = res.data
  } finally { loading.value = false }
}

async function loadMembers() {
  try {
    const res = await getTeamMembers(teamId)
    if (res.success) members.value = res.data
  } catch {}
}

async function loadAllUsers() {
  try {
    const res = await getUserList()
    if (res.success) allUsers.value = res.data
  } catch {}
}

function handleRowDblClick(row) {
  if (row.mimeType === 'folder') {
    const folderPath = row.directoryPath ? `${row.directoryPath}/${row.name}` : row.name
    currentPath.value = folderPath
    breadcrumbs.value.push({ name: row.name, path: folderPath })
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

async function handleUpload({ file }) {
  const formData = new FormData()
  formData.append('file', file)
  uploading.value = true
  uploadProgress.value = 0
  try {
    const res = await uploadTeamFile(teamId, currentPath.value, formData, (e) => {
      if (e.total > 0) uploadProgress.value = Math.round((e.loaded / e.total) * 100)
    })
    if (res.success) { ElMessage.success('上传成功'); loadFiles() }
  } finally {
    uploading.value = false
    uploadProgress.value = 0
  }
}

function triggerFolderUpload() {
  const input = document.createElement('input')
  input.type = 'file'
  input.webkitdirectory = true
  input.directory = true
  input.multiple = true
  input.onchange = (e) => handleFolderUpload(e)
  input.click()
}

async function handleFolderUpload(event) {
  const files = event.target.files
  if (!files || files.length === 0) return

  uploading.value = true
  uploadProgress.value = 0
  let successCount = 0

  for (let i = 0; i < files.length; i++) {
    const file = files[i]
    const relativePath = file.webkitRelativePath || file.name
    const formData = new FormData()
    formData.append('file', file, relativePath)

    try {
      const res = await uploadTeamFile(teamId, currentPath.value, formData)
      if (res.success) successCount++
      uploadProgress.value = Math.round(((i + 1) / files.length) * 100)
    } catch {}
  }

  uploading.value = false
  uploadProgress.value = 0
  event.target.value = ''
  ElMessage.success(`成功上传 ${successCount} 个文件`)
  loadFiles()
}

function showNewFolderDialog() {
  newFolderName.value = ''
  newFolderVisible.value = true
}

async function handleCreateFolder() {
  if (!newFolderName.value.trim()) { ElMessage.warning('请输入文件夹名称'); return }
  const res = await createTeamFolder(teamId, { name: newFolderName.value, parentPath: currentPath.value })
  if (res.success) { ElMessage.success('创建成功'); newFolderVisible.value = false; loadFiles() }
  else { ElMessage.error(res.message || '创建失败') }
}

async function handleDownload(row) {
  try {
    const res = await fetch(`/api/teams/${teamId}/files/${row.id}/download`, {
      headers: { 'Authorization': 'Bearer ' + localStorage.getItem('winnas_token') }
    })
    const blob = await res.blob()
    const url = window.URL.createObjectURL(blob)
    const link = document.createElement('a')
    link.href = url
    link.download = row.mimeType === 'folder' ? row.name + '.zip' : row.name
    link.click()
    window.URL.revokeObjectURL(url)
  } catch { ElMessage.error('下载失败') }
}

async function handleDelete(row) {
  await ElMessageBox.confirm(`确定删除 ${row.name} 吗？`, '删除确认', { type: 'warning' })
  const res = await deleteTeamFile(teamId, row.id)
  if (res.success) { ElMessage.success('删除成功'); loadFiles() }
}

async function handlePreview(row) {
  previewTitle.value = row.name
  if (isImageFile(row)) {
    const token = localStorage.getItem('winnas_token')
    previewSrc.value = `/api/teams/${teamId}/files/${row.id}/preview?token=Bearer%20${encodeURIComponent(token)}`
    previewType.value = 'image'
    previewContent.value = ''
    previewVisible.value = true
  } else if (isTextFile(row)) {
    try {
      const res = await previewTeamFile(teamId, row.id)
      if (res.success) {
        previewContent.value = res.data.content
        previewType.value = 'text'
        previewSrc.value = ''
        previewVisible.value = true
      } else {
        ElMessage.warning(res.message || '不支持预览')
      }
    } catch { ElMessage.error('预览失败') }
  }
}

function playMedia(row) {
  const token = localStorage.getItem('winnas_token')
  const mediaFiles = files.value.filter(f => f.mimeType !== 'folder' && (isVideoFile(f) || isAudioFile(f)))
  const mediaIndex = mediaFiles.findIndex(f => f.id === row.id)
  const items = mediaFiles.map(f => ({
    url: getTeamStreamUrl(teamId, f.id) + '?token=' + encodeURIComponent('Bearer ' + token),
    title: f.name,
    isVideo: isVideoFile(f),
    isAudio: isAudioFile(f)
  }))
  player.playList(items, mediaIndex >= 0 ? mediaIndex : 0)
}

function showAddMemberDialog() {
  selectedMemberIds.value = []
  addMemberVisible.value = true
}

async function handleAddMembers() {
  if (selectedMemberIds.value.length === 0) { ElMessage.warning('请选择成员'); return }
  for (const uid of selectedMemberIds.value) {
    await addMember(teamId, { userId: uid, role: 'member' })
  }
  ElMessage.success('添加成功')
  addMemberVisible.value = false
  loadMembers()
}

async function handleRemoveMember(row) {
  await ElMessageBox.confirm(`确定移除成员 ${row.nickname || row.username} 吗？`, '确认', { type: 'warning' })
  const res = await removeMember(teamId, row.userId)
  if (res.success) { ElMessage.success('已移除'); loadMembers() }
}

function isImageFile(row) {
  return ['.jpg', '.jpeg', '.png', '.gif', '.bmp', '.webp', '.svg'].includes(row.extension?.toLowerCase() || '')
}
function isTextFile(row) {
  return ['.txt', '.log', '.md', '.json', '.xml', '.csv', '.ini', '.cfg', '.conf', '.yml', '.yaml', '.html', '.css', '.js', '.ts', '.py', '.java', '.c', '.cpp', '.h', '.cs', '.go', '.rs', '.sh', '.bat', '.sql'].includes(row.extension?.toLowerCase() || '')
}
function isVideoFile(row) {
  return ['.mp4', '.webm', '.avi', '.mkv', '.mov', '.wmv', '.flv'].includes(row.extension?.toLowerCase() || '')
}
function isAudioFile(row) {
  return ['.mp3', '.wav', '.flac', '.aac', '.ogg', '.m4a', '.wma'].includes(row.extension?.toLowerCase() || '')
}
function getFileIcon(row) {
  if (row.mimeType === 'folder') return { icon: 'Folder', color: '#e6a23c' }
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
.tab-header { display: flex; align-items: center; justify-content: space-between; margin-bottom: 15px; flex-wrap: wrap; gap: 8px; }
.tab-actions { display: flex; gap: 8px; flex-wrap: wrap; }
.file-name { display: flex; align-items: center; gap: 8px; }
.file-name-info { display: flex; flex-direction: column; min-width: 0; }
.file-name-text { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.file-meta { font-size: 11px; color: #909399; margin-top: 2px; }
.mobile-more { cursor: pointer; font-size: 18px; color: #606266; }
.preview-image-container { display: flex; justify-content: center; align-items: center; min-height: 400px; }
.preview-text-container { max-height: calc(100vh - 160px); overflow: auto; }
.preview-text { background: #1e1e1e; color: #d4d4d4; padding: 20px; border-radius: 8px; font-family: 'Consolas', 'Monaco', 'Courier New', monospace; font-size: 13px; line-height: 1.6; white-space: pre-wrap; word-wrap: break-word; margin: 0; }

@media (max-width: 768px) {
  .tab-actions .el-button { padding: 6px 10px; font-size: 12px; }
  .el-table { font-size: 12px; }
}
</style>
