<template>
  <div class="home-container">
    <el-row :gutter="20" class="stat-row">
      <el-col :xs="12" :sm="6" v-for="card in statCards" :key="card.title">
        <el-card shadow="hover" class="stat-card">
          <div class="stat-icon" :style="{ background: card.color }">
            <el-icon :size="28"><component :is="card.icon" /></el-icon>
          </div>
          <div class="stat-info">
            <div class="stat-value">{{ card.value }}</div>
            <div class="stat-title">{{ card.title }}</div>
          </div>
        </el-card>
      </el-col>
    </el-row>

    <el-row :gutter="20" style="margin-top:16px">
      <el-col :xs="24" :sm="12">
        <el-card shadow="hover">
          <template #header><span><el-icon><Monitor /></el-icon> 系统状态</span></template>
          <div class="system-status-list">
            <div class="system-status-item">
              <span class="system-status-label">CPU 使用率</span>
              <el-progress :percentage="stats.cpuUsage" :color="getProgressColor(stats.cpuUsage)" :stroke-width="10" :show-text="true" style="flex:1" />
            </div>
            <div class="system-status-item">
              <span class="system-status-label">磁盘使用率</span>
              <el-progress :percentage="stats.diskUsage" :color="getProgressColor(stats.diskUsage)" :stroke-width="10" :show-text="true" style="flex:1" />
            </div>
            <div class="system-status-item">
              <span class="system-status-label">磁盘空间</span>
              <span class="system-status-value">{{ formatSize(stats.diskTotal - stats.diskFree) }} / {{ formatSize(stats.diskTotal) }}</span>
            </div>
          </div>
        </el-card>
        <el-card shadow="hover" style="margin-top:16px">
          <template #header><span>快捷操作</span></template>
          <div class="quick-actions">
            <div class="action-card" @click="$router.push('/files')">
              <div class="action-icon" style="background: linear-gradient(135deg, #409eff, #337ecc)">
                <el-icon :size="24"><Folder /></el-icon>
              </div>
              <div class="action-text">
                <div class="action-title">文件管理</div>
                <div class="action-desc">管理共享目录与文件</div>
              </div>
            </div>
            <div class="action-card" @click="$router.push('/shares')">
              <div class="action-icon" style="background: linear-gradient(135deg, #67c23a, #529b2e)">
                <el-icon :size="24"><Share /></el-icon>
              </div>
              <div class="action-text">
                <div class="action-title">分享管理</div>
                <div class="action-desc">创建与管理分享链接</div>
              </div>
            </div>
            <div class="action-card" @click="$router.push('/teams')">
              <div class="action-icon" style="background: linear-gradient(135deg, #e6a23c, #cf8e24)">
                <el-icon :size="24"><UserFilled /></el-icon>
              </div>
              <div class="action-text">
                <div class="action-title">团队协作</div>
                <div class="action-desc">团队文件与成员管理</div>
              </div>
            </div>
            <div class="action-card" @click="$router.push('/chat')">
              <div class="action-icon" style="background: linear-gradient(135deg, #9b59b6, #8e44ad)">
                <el-icon :size="24"><ChatDotRound /></el-icon>
              </div>
              <div class="action-text">
                <div class="action-title">在线聊天</div>
                <div class="action-desc">私聊与群聊沟通</div>
              </div>
            </div>
            <div v-if="userStore.isAdmin" class="action-card" @click="$router.push('/network')">
              <div class="action-icon" style="background: linear-gradient(135deg, #3498db, #2980b9)">
                <el-icon :size="24"><Link /></el-icon>
              </div>
              <div class="action-text">
                <div class="action-title">网络共享</div>
                <div class="action-desc">SMB 与 WebDAV 服务</div>
              </div>
            </div>
            <div class="action-card" @click="$router.push('/neighborhood')">
              <div class="action-icon" style="background: linear-gradient(135deg, #1abc9c, #16a085)">
                <el-icon :size="24"><Connection /></el-icon>
              </div>
              <div class="action-text">
                <div class="action-title">网上邻居</div>
                <div class="action-desc">发现局域网设备</div>
              </div>
            </div>
            <div class="action-card" @click="$router.push('/system')">
              <div class="action-icon" style="background: linear-gradient(135deg, #909399, #73767a)">
                <el-icon :size="24"><Setting /></el-icon>
              </div>
              <div class="action-text">
                <div class="action-title">系统设置</div>
                <div class="action-desc">系统信息与配置管理</div>
              </div>
            </div>
          </div>
        </el-card>
      </el-col>
      <el-col :xs="24" :sm="12">
        <el-card v-if="announcements.length > 0" shadow="hover">
          <template #header><span><el-icon><Bell /></el-icon> 公告通知</span></template>
          <div class="announcement-list">
            <div v-for="item in announcements" :key="item.id" class="announcement-item">
              <div class="announcement-item-header">
                <el-tag v-if="item.isPinned" type="danger" size="small">置顶</el-tag>
                <span class="announcement-item-title">{{ item.title }}</span>
                <span class="announcement-item-time">{{ formatDate(item.createdAt) }}</span>
              </div>
              <div class="announcement-item-content">{{ item.content }}</div>
            </div>
          </div>
        </el-card>
        <el-card v-if="favorites.length > 0" shadow="hover" class="fav-card" style="margin-top:16px">
          <template #header><span><el-icon><Star /></el-icon> 收藏夹</span></template>
          <div class="fav-grid">
            <div v-for="fav in favorites" :key="fav.id" class="fav-item">
              <el-icon :color="fav.isDirectory ? '#e6a23c' : '#409eff'"><component :is="fav.isDirectory ? 'Folder' : 'Document'" /></el-icon>
              <div style="flex:1;min-width:0">
                <span @click="goToFav(fav)" style="cursor:pointer;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;display:block">{{ fav.fileName }}</span>
                <span v-if="fav.fullPath" class="fav-path" @click="goToFav(fav)">{{ fav.fullPath }}</span>
              </div>
              <el-icon class="fav-remove" @click="removeFavorite(fav)"><Close /></el-icon>
            </div>
          </div>
        </el-card>
      </el-col>
    </el-row>

    <el-row v-if="recentFiles.length > 0" :gutter="20" style="margin-top:16px">
      <el-col :xs="24" :sm="12">
        <el-card shadow="hover">
          <template #header><span><el-icon><Clock /></el-icon> 最近访问</span></template>
          <div class="fav-list">
            <div v-for="item in recentFiles" :key="item.id" class="fav-item" @click="goToRecent(item)">
              <el-icon :color="item.isDirectory ? '#e6a23c' : '#409eff'"><component :is="item.isDirectory ? 'Folder' : 'Document'" /></el-icon>
              <span style="flex:1;overflow:hidden;text-overflow:ellipsis;white-space:nowrap">{{ item.fileName }}</span>
              <el-tag size="small" type="info">{{ item.accessType === 'download' ? '下载' : '浏览' }}</el-tag>
            </div>
          </div>
        </el-card>
      </el-col>
    </el-row>
  </div>
</template>

<script setup>
import { ref, reactive, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { getStats, getAnnouncements } from '../api/system'
import { getFavorites, removeFavorite as removeFavoriteApi, getRecentList } from '../api/enhanced'
import { useUserStore } from '../store/user'
import { Connection, Star, Clock, Close, Bell } from '@element-plus/icons-vue'
import { ElMessage } from 'element-plus'

const userStore = useUserStore()
const router = useRouter()
const favorites = ref([])
const recentFiles = ref([])
const announcements = ref([])

const stats = reactive({
  directoryCount: 0,
  shareCount: 0,
  teamCount: 0,
  userCount: 0,
  cpuUsage: 0,
  diskUsage: 0,
  diskTotal: 0,
  diskFree: 0
})

const statCards = ref([
  { title: '共享目录', value: '0', icon: 'FolderOpened', color: 'linear-gradient(135deg, #409eff, #337ecc)' },
  { title: '分享链接', value: '0', icon: 'Share', color: 'linear-gradient(135deg, #67c23a, #529b2e)' },
  { title: '团队数量', value: '0', icon: 'UserFilled', color: 'linear-gradient(135deg, #e6a23c, #cf8e24)' },
  { title: '注册用户', value: '0', icon: 'User', color: 'linear-gradient(135deg, #f56c6c, #dd5a5a)' }
])

onMounted(async () => {
  try {
    const res = await getStats()
    if (res.success) {
      Object.assign(stats, res.data)
      statCards.value[0].value = stats.directoryCount
      statCards.value[1].value = stats.shareCount
      statCards.value[2].value = stats.teamCount
      statCards.value[3].value = stats.userCount
    }
  } catch {}

  try {
    const res = await getAnnouncements()
    if (res.success) {
      announcements.value = (res.data || []).sort((a, b) => {
        if (a.isPinned && !b.isPinned) return -1
        if (!a.isPinned && b.isPinned) return 1
        return new Date(b.createdAt) - new Date(a.createdAt)
      })
    }
  } catch {}

  await loadFavorites()

  try {
    const res = await getRecentList(10)
    if (res.success) recentFiles.value = res.data || []
  } catch {}
})

async function loadFavorites() {
  try {
    const res = await getFavorites()
    if (res.success) favorites.value = (res.data || []).slice(0, 10)
  } catch {}
}

async function removeFavorite(fav) {
  try {
    await removeFavoriteApi(fav.directoryId, fav.filePath)
    favorites.value = favorites.value.filter(f => f.id !== fav.id)
    ElMessage.success('已移除收藏')
  } catch { ElMessage.error('移除失败') }
}

function goToFav(fav) {
  let targetPath = fav.filePath || ''
  if (!fav.isDirectory && targetPath) {
    const lastSlash = Math.max(targetPath.lastIndexOf('/'), targetPath.lastIndexOf('\\'))
    if (lastSlash > 0) {
      targetPath = targetPath.substring(0, lastSlash)
    } else {
      targetPath = ''
    }
  }
  router.push({ path: '/files', query: { dirId: fav.directoryId, path: targetPath || undefined } })
}

function goToRecent(item) {
  let targetPath = item.filePath || ''
  if (!item.isDirectory && targetPath) {
    const lastSlash = Math.max(targetPath.lastIndexOf('/'), targetPath.lastIndexOf('\\'))
    if (lastSlash > 0) {
      targetPath = targetPath.substring(0, lastSlash)
    } else {
      targetPath = ''
    }
  }
  router.push({ path: '/files', query: { dirId: item.directoryId, path: targetPath || undefined } })
}

function getProgressColor(val) {
  if (val > 80) return '#f56c6c'
  if (val > 60) return '#e6a23c'
  return '#67c23a'
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
.home-container {
  padding: 20px;
}

@media (max-width: 768px) {
  .home-container { padding: 12px; }
  .stat-card :deep(.el-card__body) { gap: 8px; }
  .stat-icon { width: 40px; height: 40px; }
  .stat-icon .el-icon { font-size: 20px; }
  .stat-value { font-size: 20px; }
  .quick-actions { grid-template-columns: 1fr; }
}

@media (max-width: 480px) {
  .stat-value { font-size: 16px; }
  .stat-title { font-size: 11px; }
}

.stat-card :deep(.el-card__body) {
  display: flex;
  align-items: center;
  gap: 15px;
}

.stat-icon {
  width: 56px;
  height: 56px;
  border-radius: 12px;
  display: flex;
  align-items: center;
  justify-content: center;
  color: #fff;
  flex-shrink: 0;
}

.stat-value {
  font-size: 28px;
  font-weight: 700;
  color: #303133;
}

.stat-title {
  font-size: 13px;
  color: #909399;
  margin-top: 4px;
}

.system-status-list {
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.system-status-item {
  display: flex;
  align-items: center;
  gap: 10px;
}

.system-status-label {
  font-size: 13px;
  color: #606266;
  width: 80px;
  flex-shrink: 0;
}

.system-status-value {
  font-size: 13px;
  color: #303133;
  font-weight: 500;
}

.quick-actions {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 12px;
}

.action-card {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 14px 16px;
  border-radius: 8px;
  cursor: pointer;
  transition: all 0.3s;
  background: #f5f7fa;
  border: 1px solid transparent;
}

.action-card:hover {
  background: #ecf5ff;
  border-color: #409eff;
  transform: translateY(-2px);
}

.action-icon {
  width: 44px;
  height: 44px;
  border-radius: 10px;
  display: flex;
  align-items: center;
  justify-content: center;
  color: #fff;
  flex-shrink: 0;
}

.action-title {
  font-size: 14px;
  font-weight: 600;
  color: #303133;
}

.action-desc {
  font-size: 12px;
  color: #909399;
  margin-top: 2px;
}

.announcement-list {
  display: flex;
  flex-direction: column;
  gap: 10px;
  max-height: 300px;
  overflow-y: auto;
}

.announcement-item {
  padding: 10px 12px;
  border-radius: 6px;
  background: #fdf6ec;
  border: 1px solid #faecd8;
}

.announcement-item-header {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 4px;
}

.announcement-item-title {
  font-size: 14px;
  font-weight: 600;
  color: #303133;
  flex: 1;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.announcement-item-time {
  font-size: 12px;
  color: #909399;
  flex-shrink: 0;
}

.announcement-item-content {
  font-size: 13px;
  color: #606266;
  line-height: 1.5;
}

.fav-card :deep(.el-card__body) {
  overflow-y: auto;
}

.fav-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 8px;
}

.fav-path {
  font-size: 11px;
  color: #909399;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  display: block;
  cursor: pointer;
}

.fav-list {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.fav-item {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 8px 12px;
  border-radius: 6px;
  cursor: pointer;
  transition: background 0.2s;
  font-size: 13px;
  color: var(--el-text-color-primary);
}

.fav-item:hover {
  background: var(--el-fill-color-light);
}

.fav-remove {
  color: var(--el-text-color-placeholder);
  cursor: pointer;
  transition: color 0.2s;
  flex-shrink: 0;
}

.fav-remove:hover {
  color: #f56c6c;
}
</style>
