<template>
  <div class="page-container">
    <div class="page-header">
      <h2>文件管理</h2>
      <div>
        <el-button v-if="isAdmin" type="primary" @click="showAddDirDialog">添加共享目录</el-button>
      </div>
    </div>

    <el-row :gutter="20">
      <el-col :xs="24" :sm="6">
        <el-card shadow="hover" class="dir-list">
          <template #header><span>共享目录</span></template>
          <div v-if="directories.length === 0" class="empty-text">暂无共享目录</div>
          <div v-for="dir in directories" :key="dir.id" class="dir-item" :class="{ active: currentDir?.id === dir.id }">
            <div class="dir-item-info" @click="selectDirectory(dir)">
              <el-icon><Folder /></el-icon>
              <span>{{ dir.name }}</span>
              <el-tag v-if="dir.visibility === 'public'" type="success" size="small">公开</el-tag>
            </div>
            <el-dropdown trigger="click" @command="(cmd) => handleDirCommand(cmd, dir)">
              <el-icon class="dir-more"><MoreFilled /></el-icon>
              <template #dropdown>
                <el-dropdown-menu>
                  <el-dropdown-item command="edit"><el-icon><Edit /></el-icon>编辑属性</el-dropdown-item>
                  <el-dropdown-item command="share"><el-icon><Share /></el-icon>分享目录</el-dropdown-item>
                  <el-dropdown-item command="delete" style="color:#f56c6c"><el-icon><Delete /></el-icon>删除目录</el-dropdown-item>
                </el-dropdown-menu>
              </template>
            </el-dropdown>
          </div>
        </el-card>
      </el-col>

      <el-col :xs="24" :sm="18">
        <el-card shadow="hover" v-if="currentDir">
          <template #header>
            <div class="file-header">
              <span>{{ currentDir.name }}</span>
              <div class="file-actions">
                <el-input v-model="searchKeyword" placeholder="搜索文件..." :prefix-icon="Search" size="small" style="width:180px;margin-right:8px" clearable @keyup.enter="handleSearch" @clear="loadFiles" />
                <el-radio-group v-model="viewMode" size="small" style="margin-right:8px">
                  <el-radio-button value="table"><el-icon><List /></el-icon></el-radio-button>
                  <el-radio-button value="gallery"><el-icon><Picture /></el-icon></el-radio-button>
                </el-radio-group>
                <el-button v-if="canUpload" @click="showNewFolderDialog"><el-icon><FolderAdd /></el-icon> 新建文件夹</el-button>
                <el-button v-if="canUpload" icon="Upload" :loading="uploading" @click="triggerFolderUpload">上传文件夹</el-button>
                <el-upload v-if="canUpload" :show-file-list="false" :http-request="handleUpload" multiple accept="*/*">
                  <el-button type="primary" icon="Upload" :loading="uploading">上传文件</el-button>
                </el-upload>
                <el-button v-if="clipboard.files.length > 0" type="success" @click="handlePaste"><el-icon><CopyDocument /></el-icon> 粘贴</el-button>
              </div>
            </div>
          </template>

          <el-progress v-if="uploading" :percentage="uploadProgress" :stroke-width="4" style="margin-bottom:12px" />

          <div v-if="selectedFiles.length > 0" class="batch-bar" style="margin-bottom:12px;display:flex;align-items:center;gap:8px;padding:8px 12px;background:var(--el-color-primary-light-9);border-radius:6px;border:1px solid var(--el-color-primary-light-7)">
            <span style="font-size:13px;color:var(--el-text-color-secondary)">已选 {{ selectedFiles.length }} 项</span>
            <el-button size="small" type="primary" @click="handleBatchDownload">批量下载</el-button>
            <el-button size="small" type="warning" @click="handleBatchMove">批量移动</el-button>
            <el-button size="small" type="info" @click="handleBatchCopy">复制到...</el-button>
            <el-button size="small" type="danger" @click="handleBatchDelete">批量删除</el-button>
            <el-button size="small" @click="selectedFiles = []">取消选择</el-button>
          </div>

          <el-breadcrumb v-if="breadcrumbs.length > 0" separator="/" style="margin-bottom: 15px">
            <el-breadcrumb-item @click="navigateTo(null)">根目录</el-breadcrumb-item>
            <el-breadcrumb-item v-for="(crumb, index) in breadcrumbs" :key="index" @click="navigateTo(crumb.path)">{{ crumb.name }}</el-breadcrumb-item>
          </el-breadcrumb>

          <div v-if="viewMode === 'table'" class="filter-bar" style="margin-bottom:12px">
            <el-radio-group v-model="filterType" size="small">
              <el-radio-button value="all">全部</el-radio-button>
              <el-radio-button value="image">图片</el-radio-button>
              <el-radio-button value="video">视频</el-radio-button>
              <el-radio-button value="audio">音频</el-radio-button>
              <el-radio-button value="document">文档</el-radio-button>
            </el-radio-group>
          </div>

          <template v-if="viewMode === 'table'">
            <el-table :data="filteredFileList" v-loading="loading" @row-dblclick="handleRowDblClick" @row-contextmenu="handleRowContextMenu" @selection-change="handleSelectionChange">
              <el-table-column type="selection" width="40" />
              <el-table-column label="名称" min-width="200">
                <template #default="{ row }">
                  <div class="file-name">
                    <el-icon :size="20" :color="getFileIcon(row).color"><component :is="getFileIcon(row).icon" /></el-icon>
                    <div class="file-name-info">
                      <span class="file-name-text">{{ row.name }}</span>
                      <span v-if="isMobile" class="file-meta">{{ row.isDirectory ? '' : formatSize(row.size) }} · {{ formatDate(row.lastModified) }}</span>
                    </div>
                    <el-tooltip v-if="isFileLocked(row)" :content="`已被 ${getLockInfo(row)?.username} 锁定`" placement="top">
                      <el-icon color="#e6a23c" :size="14"><Lock /></el-icon>
                    </el-tooltip>
                    <el-tag v-if="fileTagsMap[row.relativePath]?.tag" :color="fileTagsMap[row.relativePath]?.color" size="small" style="margin-left:4px;color:#fff;border:none" effect="dark">{{ fileTagsMap[row.relativePath].tag }}</el-tag>
                    <el-tooltip v-if="fileTagsMap[row.relativePath]?.note" :content="fileTagsMap[row.relativePath].note" placement="top">
                      <el-icon :size="14" color="#909399" style="margin-left:2px"><Memo /></el-icon>
                    </el-tooltip>
                  </div>
                </template>
              </el-table-column>
              <el-table-column v-if="!isMobile" label="大小" width="100">
                <template #default="{ row }">{{ row.isDirectory ? '-' : formatSize(row.size) }}</template>
              </el-table-column>
              <el-table-column v-if="!isMobile" label="修改时间" width="160">
                <template #default="{ row }">{{ formatDate(row.lastModified) }}</template>
              </el-table-column>
              <el-table-column label="操作" width="80" fixed="right">
                <template #default="{ row }">
                  <el-dropdown trigger="click">
                    <el-icon class="mobile-more"><MoreFilled /></el-icon>
                    <template #dropdown>
                      <el-dropdown-menu>
                        <el-dropdown-item v-if="canPreview(row)" @click="handleFilePreview(row)"><el-icon><View /></el-icon>预览</el-dropdown-item>
                        <el-dropdown-item v-if="isVideoFile(row) || isAudioFile(row)" @click="playMedia(row)"><el-icon><VideoPlay /></el-icon>播放</el-dropdown-item>
                        <el-dropdown-item @click="handleDownload(row)"><el-icon><Download /></el-icon>下载</el-dropdown-item>
                        <el-dropdown-item @click="toggleFavorite(row)"><el-icon><Star /></el-icon>{{ row._favorited ? '取消收藏' : '收藏' }}</el-dropdown-item>
                        <el-dropdown-item @click="showTagDialog(row)"><el-icon><PriceTag /></el-icon>标签备注</el-dropdown-item>
                        <el-dropdown-item v-if="isZipFile(row)" @click="handleExtract(row)"><el-icon><FolderOpened /></el-icon>解压</el-dropdown-item>
                        <el-dropdown-item @click="handleShareFile(row)"><el-icon><Share /></el-icon>分享</el-dropdown-item>
                        <el-dropdown-item v-if="canMove" @click="handleMove(row)"><el-icon><Rank /></el-icon>移动</el-dropdown-item>
                        <el-dropdown-item v-if="canMove" @click="handleCopy(row)"><el-icon><CopyDocument /></el-icon>复制</el-dropdown-item>
                        <el-dropdown-item v-if="canRename" @click="handleRename(row)"><el-icon><Edit /></el-icon>重命名</el-dropdown-item>
                        <el-dropdown-item v-if="canDelete" @click="handleDelete(row)" style="color:#f56c6c"><el-icon><Delete /></el-icon>删除</el-dropdown-item>
                      </el-dropdown-menu>
                    </template>
                  </el-dropdown>
                </template>
              </el-table-column>
            </el-table>
          </template>

          <template v-else>
            <div class="gallery-toolbar" style="display:flex;align-items:center;justify-content:space-between;margin-bottom:12px;padding:8px 12px;background:#f5f7fa;border-radius:6px;border:1px solid #ebeef5">
              <el-radio-group v-model="filterType" size="small">
                <el-radio-button value="all">全部</el-radio-button>
                <el-radio-button value="image">图片</el-radio-button>
                <el-radio-button value="video">视频</el-radio-button>
                <el-radio-button value="audio">音频</el-radio-button>
                <el-radio-button value="document">文档</el-radio-button>
              </el-radio-group>
              <el-button size="small" type="primary" @click="startSlideshow" :disabled="galleryImages.length === 0">
                <el-icon><VideoPlay /></el-icon> 幻灯片播放
              </el-button>
            </div>
            <div v-if="filteredFileList.length === 0" class="empty-text">当前目录无文件</div>
            <div v-else class="gallery-grid">
              <div v-for="row in filteredFileList" :key="row.name" class="gallery-item" @dblclick="handleRowDblClick(row)">
                <div class="gallery-thumb">
                  <template v-if="row.isDirectory">
                    <el-icon :size="40" color="#e6a23c"><Folder /></el-icon>
                  </template>
                  <template v-else-if="isImageFile(row)">
                    <img :src="getImageThumbUrl(row)" style="width:100%;height:100%;object-fit:cover" loading="lazy" />
                  </template>
                  <template v-else>
                    <el-icon :size="40" :color="getFileIcon(row).color"><component :is="getFileIcon(row).icon" /></el-icon>
                  </template>
                </div>
                <div class="gallery-name">{{ row.name }}</div>
                <div class="gallery-size">{{ row.isDirectory ? '' : formatSize(row.size) }}</div>
              </div>
            </div>
          </template>
        </el-card>
        <el-card v-else shadow="hover"><el-empty description="请选择左侧的共享目录" /></el-card>
      </el-col>
    </el-row>

    <el-dialog v-model="dirDialogVisible" :title="isEditingDir ? '编辑共享目录' : '添加共享目录'" width="500px">
      <el-form :model="dirForm" label-width="100px">
        <el-form-item label="目录名称"><el-input v-model="dirForm.name" placeholder="请输入目录名称" /></el-form-item>
        <el-form-item label="目录路径"><el-input v-model="dirForm.path" placeholder="例如: D:\Share" :disabled="isEditingDir" /></el-form-item>
        <el-form-item label="描述"><el-input v-model="dirForm.description" type="textarea" /></el-form-item>
        <el-form-item label="可见性">
          <el-radio-group v-model="dirForm.visibility">
            <el-radio value="private">私有</el-radio>
            <el-radio value="public">公开</el-radio>
          </el-radio-group>
        </el-form-item>
        <el-form-item label="允许上传"><el-switch v-model="dirForm.allowUpload" /></el-form-item>
        <el-form-item label="允许删除"><el-switch v-model="dirForm.allowDelete" /></el-form-item>
        <el-form-item label="允许重命名"><el-switch v-model="dirForm.allowRename" /></el-form-item>
        <el-form-item label="允许移动"><el-switch v-model="dirForm.allowMove" /></el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dirDialogVisible = false">取消</el-button>
        <el-button type="primary" @click="handleSaveDir">确定</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="newFolderVisible" title="新建文件夹" width="400px">
      <el-input v-model="newFolderName" placeholder="请输入文件夹名称" />
      <template #footer>
        <el-button @click="newFolderVisible = false">取消</el-button>
        <el-button type="primary" @click="handleCreateFolder">创建</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="moveDialogVisible" title="移动到" width="500px">
      <p style="margin-bottom:15px;color:#909399">选择目标目录：</p>
      <el-tree :data="moveDirTree" :props="{ label: 'name', children: 'children' }" node-key="id" @node-click="handleMoveTargetSelect" highlight-current style="max-height:400px;overflow:auto" />
      <template #footer>
        <el-button @click="moveDialogVisible = false">取消</el-button>
        <el-button type="primary" @click="handleMoveConfirm" :disabled="!moveTarget">移动</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="shareFileDialog" :title="`分享 - ${shareFileItem?.name || ''}`" width="500px">
      <el-form :model="shareForm" label-width="100px">
        <el-form-item label="访问密码"><el-input v-model="shareForm.password" placeholder="留空则无密码" /></el-form-item>
        <el-form-item label="过期时间"><el-date-picker v-model="shareForm.expiresAt" type="datetime" placeholder="留空则永不过期" style="width:100%" /></el-form-item>
        <el-form-item label="最大下载次数"><el-input-number v-model="shareForm.maxDownloads" :min="-1" /><span style="margin-left:10px;color:#909399">-1 不限</span></el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="shareFileDialog = false">取消</el-button>
        <el-button type="primary" @click="handleShareConfirm">创建分享</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="shareQrVisible" title="分享二维码" width="360px">
      <div style="display:flex;flex-direction:column;align-items:center;gap:16px">
        <QrcodeVue :value="shareQrLink" :size="240" level="M" />
        <div style="font-size:13px;color:#606266;word-break:break-all;text-align:center;max-width:300px">{{ shareQrLink }}</div>
        <el-button type="primary" size="small" @click="copyShareQrLink">复制链接</el-button>
      </div>
    </el-dialog>

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
      <div v-else-if="previewType === 'pdf'" class="preview-pdf-container">
        <iframe :src="previewSrc" style="width:100%;height:calc(100vh - 160px);border:none"></iframe>
      </div>
    </el-dialog>

    <el-dialog v-model="tagDialogVisible" title="标签与备注" width="450px">
      <el-form label-width="80px">
        <el-form-item label="标签">
          <el-input v-model="tagForm.tag" placeholder="输入标签名" />
        </el-form-item>
        <el-form-item label="颜色">
          <el-color-picker v-model="tagForm.color" />
        </el-form-item>
        <el-form-item label="备注">
          <el-input v-model="tagForm.note" type="textarea" :rows="3" placeholder="添加备注说明" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="tagDialogVisible = false">取消</el-button>
        <el-button type="primary" @click="saveTag">保存</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="searchDialogVisible" title="搜索结果" width="700px" fullscreen>
      <el-table :data="searchResults" v-loading="searchLoading">
        <el-table-column label="文件名" min-width="200">
          <template #default="{ row }">
            <el-link type="primary" @click="goToSearchResult(row)">{{ row.name }}</el-link>
          </template>
        </el-table-column>
        <el-table-column label="大小" width="100">
          <template #default="{ row }">{{ row.isDirectory ? '-' : formatSize(row.size) }}</template>
        </el-table-column>
        <el-table-column label="路径" min-width="200">
          <template #default="{ row }">{{ row.relativePath }}</template>
        </el-table-column>
      </el-table>
    </el-dialog>

    <teleport to="body">
      <div v-if="slideshowActive" class="slideshow-overlay" @click="stopSlideshow">
        <div class="slideshow-content" @click.stop>
          <button class="slideshow-close" @click="stopSlideshow">&times;</button>
          <button class="slideshow-prev" @click="slideshowPrev">&lsaquo;</button>
          <img :src="slideshowImages[slideshowIndex]" class="slideshow-image" />
          <button class="slideshow-next" @click="slideshowNext">&rsaquo;</button>
          <div class="slideshow-counter">{{ slideshowIndex + 1 }} / {{ slideshowImages.length }}</div>
          <div class="slideshow-controls">
            <el-button size="small" @click="toggleSlideshowAuto">{{ slideshowAuto ? '暂停' : '自动播放' }}</el-button>
          </div>
        </div>
      </div>
    </teleport>

    <teleport to="body">
      <div v-show="contextMenuVisible" class="context-menu" :style="{ left: contextMenuX + 'px', top: contextMenuY + 'px' }">
        <div v-if="contextMenuRow" class="context-menu-items">
          <div v-if="canPreview(contextMenuRow)" class="context-menu-item" @click="handleFilePreview(contextMenuRow); contextMenuVisible = false"><el-icon><View /></el-icon> 预览</div>
          <div v-if="isVideoFile(contextMenuRow) || isAudioFile(contextMenuRow)" class="context-menu-item" @click="playMedia(contextMenuRow); contextMenuVisible = false"><el-icon><VideoPlay /></el-icon> 播放</div>
          <div v-if="!contextMenuRow.isDirectory" class="context-menu-item" @click="handleDownload(contextMenuRow); contextMenuVisible = false"><el-icon><Download /></el-icon> 下载</div>
          <div v-if="contextMenuRow.isDirectory" class="context-menu-item" @click="handleDownload(contextMenuRow); contextMenuVisible = false"><el-icon><Download /></el-icon> 下载</div>
          <div class="context-menu-item" @click="handleShareFile(contextMenuRow); contextMenuVisible = false"><el-icon><Share /></el-icon> 分享</div>
          <div v-if="canMove" class="context-menu-item" @click="handleMove(contextMenuRow); contextMenuVisible = false"><el-icon><Rank /></el-icon> 移动</div>
          <div v-if="canRename" class="context-menu-item" @click="handleRename(contextMenuRow); contextMenuVisible = false"><el-icon><Edit /></el-icon> 重命名</div>
          <div v-if="canDelete" class="context-menu-item danger" @click="handleDelete(contextMenuRow); contextMenuVisible = false"><el-icon><Delete /></el-icon> 删除</div>
        </div>
      </div>
    </teleport>

    <DocPreview v-model="docPreviewVisible" :title="docPreviewTitle" :src="docPreviewSrc" :fileName="docPreviewFileName" />
  </div>
</template>

<script setup>
import { ref, reactive, onMounted, onUnmounted, computed, watch, nextTick } from 'vue'
import { useRouter, useRoute } from 'vue-router'
import { getDirectories, createDirectory, updateDirectory, deleteDirectory as deleteDirApi, getFileList, uploadFile, downloadFile, getStreamUrl, getPreviewUrl, previewFile, renameFile, deleteFile, moveFile, shareFile, createFolder, lockFile, unlockFile, getFileLocks } from '../../api/file'
import { searchFiles, addFavorite, removeFavorite, checkFavorite, recordRecent, getFileTags, batchGetFileTags, setFileTag, batchDelete, batchDownload, batchMove, extractFile, copyFiles } from '../../api/enhanced'
import { getServerAddress } from '../../api/system'
import { usePlayerStore } from '../../store/player'
import { useUserStore } from '../../store/user'
import { ElMessage, ElMessageBox } from 'element-plus'
import DocPreview from './DocPreview.vue'
import { marked } from 'marked'
import QrcodeVue from 'qrcode.vue'
import { Lock, View } from '@element-plus/icons-vue'

const player = usePlayerStore()
const userStore = useUserStore()
const router = useRouter()
const route = useRoute()
const isAdmin = computed(() => userStore.isAdmin)

const searchKeyword = ref('')
const searchDialogVisible = ref(false)
const searchResults = ref([])
const searchLoading = ref(false)
const selectedFiles = ref([])
const tagDialogVisible = ref(false)
const tagForm = reactive({ dirId: 0, filePath: '', tag: '', color: '', note: '' })
const clipboard = reactive({ files: [], sourceDirId: 0, mode: '' })
const fileTagsMap = reactive({})

const isMobile = ref(window.innerWidth < 768)

const canUpload = computed(() => isAdmin.value || currentDir.value?.allowUpload)
const canDelete = computed(() => isAdmin.value || currentDir.value?.allowDelete)
const canRename = computed(() => isAdmin.value || currentDir.value?.allowRename)
const canMove = computed(() => isAdmin.value || currentDir.value?.allowMove)

const directories = ref([])
const currentDir = ref(null)
const currentDirPath = ref('')
const fileList = ref([])
const loading = ref(false)
const dirDialogVisible = ref(false)
const isEditingDir = ref(false)
const editingDirId = ref(null)
const currentPath = ref('')
const breadcrumbs = ref([])
const serverUrl = ref('')

const uploading = ref(false)
const uploadProgress = ref(0)

const moveDialogVisible = ref(false)
const moveTarget = ref(null)
const moveFileRow = ref(null)

const shareFileDialog = ref(false)
const shareFileItem = ref(null)
const shareForm = reactive({ password: '', expiresAt: null, maxDownloads: -1 })

const shareQrVisible = ref(false)
const shareQrLink = ref('')

const contextMenuVisible = ref(false)
const contextMenuX = ref(0)
const contextMenuY = ref(0)
const contextMenuRow = ref(null)

const previewVisible = ref(false)
const previewType = ref('')
const previewTitle = ref('')
const previewSrc = ref('')
const previewContent = ref('')
const docPreviewVisible = ref(false)
const docPreviewTitle = ref('')
const docPreviewSrc = ref('')
const docPreviewFileName = ref('')

const dirForm = reactive({ name: '', path: '', description: '', visibility: 'private', allowUpload: true, allowDelete: false, allowRename: false, allowMove: false })

const newFolderVisible = ref(false)
const newFolderName = ref('')

const moveDirTree = ref([])

const viewMode = ref('table')
const filterType = ref('all')
const fileLocks = ref({})

const slideshowActive = ref(false)
const slideshowImages = ref([])
const slideshowIndex = ref(0)
const slideshowAuto = ref(false)
let slideshowTimer = null

const filteredFileList = computed(() => {
  if (filterType.value === 'all') return fileList.value
  return fileList.value.filter(row => {
    if (row.isDirectory) return filterType.value === 'all'
    switch (filterType.value) {
      case 'image': return isImageFile(row)
      case 'video': return isVideoFile(row)
      case 'audio': return isAudioFile(row)
      case 'document': return isDocumentFile(row)
      default: return true
    }
  })
})

const galleryImages = computed(() => {
  return fileList.value.filter(f => !f.isDirectory && isImageFile(f))
})

watch(filterType, () => {
  if (viewMode.value === 'gallery' && filterType.value !== 'all' && filterType.value !== 'image') {
    viewMode.value = 'table'
  }
})

onMounted(async () => {
  await loadDirectories()
  try { const res = await getServerAddress(); if (res.success) serverUrl.value = res.data.url } catch {}
  document.addEventListener('click', hideContextMenu)
  document.addEventListener('contextmenu', hideContextMenu)
  document.addEventListener('keydown', handleSlideshowKey)
  document.addEventListener('keydown', handleShortcutKey)
  window.addEventListener('resize', checkMobile)

  handleRouteQuery()
})

watch(() => route.query, () => {
  if (route.query.dirId) {
    handleRouteQuery()
  }
})

async function handleRouteQuery() {
  if (!route.query.dirId) return
  const dirId = parseInt(route.query.dirId)
  const dir = directories.value.find(d => d.id === dirId)
  if (!dir) return
  currentDir.value = dir
  currentDirPath.value = dir.path
  filterType.value = 'all'
  if (route.query.path) {
    const pathStr = route.query.path
    currentPath.value = pathStr
    const parts = pathStr.split('/').filter(Boolean)
    breadcrumbs.value = []
    let accumulated = ''
    for (const part of parts) {
      accumulated = accumulated ? accumulated + '/' + part : part
      breadcrumbs.value.push({ name: part, path: accumulated })
    }
  } else {
    currentPath.value = ''
    breadcrumbs.value = []
  }
  await loadFiles()
}

onUnmounted(() => {
  document.removeEventListener('click', hideContextMenu)
  document.removeEventListener('contextmenu', hideContextMenu)
  document.removeEventListener('keydown', handleSlideshowKey)
  document.removeEventListener('keydown', handleShortcutKey)
  window.removeEventListener('resize', checkMobile)
  stopSlideshow()
})

function checkMobile() {
  isMobile.value = window.innerWidth < 768
}

function handleShortcutKey(e) {
  if (e.ctrlKey && e.key === 'f') {
    e.preventDefault()
    const input = document.querySelector('.file-actions .el-input__inner')
    if (input) input.focus()
  }
  if (e.key === 'Delete' && selectedFiles.value.length > 0) {
    handleBatchDelete()
  }
  if (e.ctrlKey && e.key === 'v' && clipboard.files.length > 0) {
    handlePaste()
  }
}

function hideContextMenu() { contextMenuVisible.value = false }

async function loadDirectories() {
  const res = await getDirectories()
  if (res.success) directories.value = res.data
}

async function selectDirectory(dir) {
  currentDir.value = dir
  currentDirPath.value = dir.path
  currentPath.value = ''
  breadcrumbs.value = []
  filterType.value = 'all'
  await loadFiles()
}

async function loadFiles() {
  if (!currentDir.value) return
  loading.value = true
  try {
    const res = await getFileList(currentDir.value.id, currentPath.value)
    if (res.success) {
      fileList.value = res.data
      currentDirPath.value = res.dirPath || currentDir.value.path
    }
    loadFileLocks()
    loadFileTagsMap()
  } finally { loading.value = false }
}

async function loadFileTagsMap() {
  if (!currentDir.value) return
  try {
    const res = await batchGetFileTags(currentDir.value.id)
    if (res.success && res.data) {
      const map = {}
      for (const tag of res.data) {
        map[tag.filePath] = tag
      }
      Object.keys(fileTagsMap).forEach(k => delete fileTagsMap[k])
      Object.assign(fileTagsMap, map)
    }
  } catch {}
}

async function loadFileLocks() {
  if (!currentDir.value) return
  try {
    const res = await getFileLocks(currentDir.value.id)
    if (res.success) {
      const map = {}
      for (const lock of res.data) {
        map[lock.filePath] = lock
      }
      fileLocks.value = map
    }
  } catch {}
}

function isFileLocked(row) {
  if (row.isDirectory) return false
  const lock = fileLocks.value[row.relativePath]
  if (!lock) return false
  return lock.userId !== userStore.userId
}

function getLockInfo(row) {
  return fileLocks.value[row.relativePath]
}

function handleRowDblClick(row) {
  if (row.isDirectory) {
    currentPath.value = row.relativePath
    breadcrumbs.value.push({ name: row.name, path: row.relativePath })
    loadFiles()
  } else if (isImageFile(row) || isTextFile(row)) {
    handlePreview(row)
  } else if (isOfficeFile(row) || isPdfFile(row)) {
    handleFilePreview(row)
  } else if (isVideoFile(row) || isAudioFile(row)) {
    playMedia(row)
  }
}

function handleRowContextMenu(row, column, event) {
  event.preventDefault()
  contextMenuRow.value = row
  contextMenuX.value = event.clientX
  contextMenuY.value = event.clientY
  contextMenuVisible.value = true
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
    const res = await uploadFile(currentDir.value.id, currentPath.value, formData, (e) => {
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
      const res = await uploadFile(currentDir.value.id, currentPath.value, formData)
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

function getImageThumbUrl(row) {
  const token = localStorage.getItem('winnas_token')
  return getPreviewUrl(currentDir.value.id, row.relativePath) + '&token=Bearer%20' + encodeURIComponent(token)
}

async function handlePreview(row) {
  previewTitle.value = row.name
  if (isImageFile(row)) {
    const token = localStorage.getItem('winnas_token')
    previewSrc.value = getPreviewUrl(currentDir.value.id, row.relativePath) + '&token=Bearer%20' + encodeURIComponent(token)
    previewType.value = 'image'
    previewContent.value = ''
    previewVisible.value = true
  } else if (isMarkdownFile(row)) {
    try {
      const res = await previewFile(currentDir.value.id, row.relativePath)
      if (res.success) {
        previewContent.value = marked(res.data.content || '')
        previewType.value = 'markdown'
        previewSrc.value = ''
        previewVisible.value = true
      } else {
        ElMessage.warning(res.message || '不支持预览')
      }
    } catch {
      ElMessage.error('预览失败')
    }
  } else if (isTextFile(row)) {
    try {
      const res = await previewFile(currentDir.value.id, row.relativePath)
      if (res.success) {
        previewContent.value = res.data.content
        previewType.value = 'text'
        previewSrc.value = ''
        previewVisible.value = true
      } else {
        ElMessage.warning(res.message || '不支持预览')
      }
    } catch {
      ElMessage.error('预览失败')
    }
  }
}

async function handleDownload(row) {
  const res = await downloadFile(currentDir.value.id, row.relativePath)
  const blob = new Blob([res])
  const url = window.URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = row.isDirectory ? row.name + '.zip' : row.name
  link.click()
  window.URL.revokeObjectURL(url)
}

function playMedia(row) {
  const token = localStorage.getItem('winnas_token')
  const streamUrl = getStreamUrl(currentDir.value.id, row.relativePath) + '&token=' + encodeURIComponent('Bearer ' + token)

  const mediaFiles = fileList.value.filter(f => !f.isDirectory && (isVideoFile(f) || isAudioFile(f)))
  const mediaIndex = mediaFiles.findIndex(f => f.name === row.name)
  const items = mediaFiles.map(f => ({
    url: getStreamUrl(currentDir.value.id, f.relativePath) + '&token=' + encodeURIComponent('Bearer ' + token),
    title: f.name,
    isVideo: isVideoFile(f),
    isAudio: isAudioFile(f)
  }))

  player.playList(items, mediaIndex >= 0 ? mediaIndex : 0)
}

async function handleRename(row) {
  if (isFileLocked(row)) {
    const lock = getLockInfo(row)
    ElMessage.warning(`文件已被 ${lock.username} 锁定，无法重命名`)
    return
  }
  const { value } = await ElMessageBox.prompt('请输入新名称', '重命名', { inputValue: row.name })
  if (value && value !== row.name) {
    await lockFile(currentDir.value.id, row.relativePath)
    try {
      await renameFile({ recordId: 0, directoryId: currentDir.value.id, newName: value, relativePath: row.relativePath })
      ElMessage.success('重命名成功'); loadFiles()
    } finally {
      await unlockFile(currentDir.value.id, row.relativePath)
    }
  }
}

async function handleDelete(row) {
  await ElMessageBox.confirm(`确定删除 ${row.name} 吗？`, '删除确认', { type: 'warning' })
  await deleteFile({ recordId: 0, directoryId: currentDir.value.id, relativePath: row.relativePath })
  ElMessage.success('删除成功'); loadFiles()
}

function handleMove(row) {
  moveFileRow.value = row
  moveTarget.value = null
  moveDirTree.value = directories.value.map(d => ({ id: d.id, name: d.name, path: d.path }))
  moveDialogVisible.value = true
}

function handleMoveTargetSelect(node) {
  moveTarget.value = node
}

async function handleMoveConfirm() {
  if (!moveTarget.value || !moveFileRow.value) return
  try {
    const res = await moveFile({
      recordId: 0,
      directoryId: currentDir.value.id,
      targetDirectoryId: moveTarget.value.id,
      targetSubPath: '',
      sourceRelativePath: moveFileRow.value.relativePath || '',
      sourceFileName: moveFileRow.value.name || ''
    })
    if (res.success) { ElMessage.success('移动成功'); moveDialogVisible.value = false; loadFiles() }
    else { ElMessage.error(res.message || '移动失败') }
  } catch (e) {
    ElMessage.error('移动失败: ' + (e.message || '未知错误'))
  }
}

function handleShareFile(row) {
  shareFileItem.value = row
  shareForm.password = ''
  shareForm.expiresAt = null
  shareForm.maxDownloads = -1
  shareFileDialog.value = true
}

async function handleShareConfirm() {
  const row = shareFileItem.value
  if (!row) return
  let filePath = row.relativePath || ''
  if (!filePath && row.path) filePath = ''
  const res = await shareFile({
    directoryId: currentDir.value.id,
    filePath: filePath || '',
    password: shareForm.password || null,
    expiresAt: shareForm.expiresAt,
    maxDownloads: shareForm.maxDownloads
  })
  if (res.success) {
    const base = serverUrl.value || window.location.origin
    const link = `${base}/#/share/${res.data.code}`
    try { await navigator.clipboard.writeText(link) } catch {}
    ElMessage.success(`分享创建成功，链接已复制`)
    shareFileDialog.value = false
    shareQrLink.value = link
    shareQrVisible.value = true
  }
}

function copyShareQrLink() {
  if (navigator.clipboard && navigator.clipboard.writeText) {
    navigator.clipboard.writeText(shareQrLink.value).then(() => {
      ElMessage.success('链接已复制到剪贴板')
    })
  } else {
    const input = document.createElement('input')
    input.value = shareQrLink.value
    document.body.appendChild(input)
    input.select()
    document.execCommand('copy')
    document.body.removeChild(input)
    ElMessage.success('链接已复制到剪贴板')
  }
}

function handleDirCommand(cmd, dir) {
  if (cmd === 'edit') showEditDirDialog(dir)
  else if (cmd === 'delete') handleDeleteDir(dir)
  else if (cmd === 'share') { shareFileItem.value = dir; shareForm.password = ''; shareForm.expiresAt = null; shareForm.maxDownloads = -1; shareFileDialog.value = true }
}

function showAddDirDialog() {
  isEditingDir.value = false; editingDirId.value = null
  Object.assign(dirForm, { name: '', path: '', description: '', visibility: 'private', allowUpload: true, allowDelete: false, allowRename: false, allowMove: false })
  dirDialogVisible.value = true
}

function showNewFolderDialog() {
  newFolderName.value = ''
  newFolderVisible.value = true
}

async function handleCreateFolder() {
  if (!newFolderName.value.trim()) { ElMessage.warning('请输入文件夹名称'); return }
  const res = await createFolder(currentDir.value.id, currentPath.value, newFolderName.value)
  if (res.success) { ElMessage.success('创建成功'); newFolderVisible.value = false; loadFiles() }
  else { ElMessage.error(res.message || '创建失败') }
}

function showEditDirDialog(dir) {
  isEditingDir.value = true; editingDirId.value = dir.id
  Object.assign(dirForm, { name: dir.name, path: dir.path, description: dir.description || '', visibility: dir.visibility || 'private', allowUpload: dir.allowUpload ?? true, allowDelete: dir.allowDelete ?? false, allowRename: dir.allowRename ?? false, allowMove: dir.allowMove ?? false })
  dirDialogVisible.value = true
}

async function handleSaveDir() {
  if (isEditingDir.value) {
    const res = await updateDirectory(editingDirId.value, dirForm)
    if (res.success) { ElMessage.success('更新成功'); dirDialogVisible.value = false; loadDirectories(); if (currentDir.value?.id === editingDirId.value) currentDir.value = { ...currentDir.value, ...dirForm } }
  } else {
    const res = await createDirectory(dirForm)
    if (res.success) { ElMessage.success('添加成功'); dirDialogVisible.value = false; loadDirectories() }
  }
}

async function handleDeleteDir(dir) {
  await ElMessageBox.confirm(`确定删除共享目录「${dir.name}」吗？`, '删除确认', { type: 'warning' })
  const res = await deleteDirApi(dir.id)
  if (res.success) { ElMessage.success('删除成功'); if (currentDir.value?.id === dir.id) { currentDir.value = null; fileList.value = [] } loadDirectories() }
}

function isImageFile(row) {
  return ['.jpg', '.jpeg', '.png', '.gif', '.bmp', '.webp', '.svg'].includes(row.extension?.toLowerCase() || '')
}

function isMarkdownFile(row) {
  return (row.extension?.toLowerCase() || '') === '.md'
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

function isDocumentFile(row) {
  const ext = row.extension?.toLowerCase() || ''
  return ['.doc', '.docx', '.xls', '.xlsx', '.ppt', '.pptx', '.pdf', '.txt', '.md', '.json', '.xml', '.csv', '.yml', '.yaml', '.html', '.css', '.js', '.ts', '.py', '.java', '.c', '.cpp', '.h', '.cs', '.go', '.rs', '.sh', '.bat', '.sql', '.log', '.ini', '.cfg', '.conf'].includes(ext)
}

function isPdfFile(row) {
  return (row.extension?.toLowerCase() || '') === '.pdf'
}

function isOfficeFile(row) {
  const ext = row.extension?.toLowerCase() || ''
  return ['.doc', '.docx', '.xls', '.xlsx', '.ppt', '.pptx'].includes(ext)
}

function canPreview(row) {
  return isImageFile(row) || isTextFile(row) || isPdfFile(row) || isOfficeFile(row)
}

function isZipFile(row) {
  return ['.zip'].includes(row.extension?.toLowerCase() || '')
}

async function handleSearch() {
  if (!searchKeyword.value.trim()) return
  searchLoading.value = true
  searchDialogVisible.value = true
  try {
    const res = await searchFiles(searchKeyword.value.trim(), null, currentDir.value?.id)
    if (res.success) searchResults.value = res.data
  } finally { searchLoading.value = false }
}

function goToSearchResult(row) {
  searchDialogVisible.value = false
  const dir = directories.value.find(d => d.id === row.directoryId)
  if (dir) {
    selectDirectory(dir)
    if (row.relativePath) {
      const parentPath = row.relativePath.includes('/') || row.relativePath.includes('\\')
        ? row.relativePath.substring(0, row.relativePath.lastIndexOf(/[\/\\]/.test(row.relativePath) ? row.relativePath.match(/[\/\\]/)[0] : '/'))
        : ''
      if (parentPath) navigateTo(parentPath)
    }
  }
}

function handleSelectionChange(selection) {
  selectedFiles.value = selection
}

async function toggleFavorite(row) {
  try {
    if (row._favorited) {
      await removeFavorite(currentDir.value.id, row.relativePath)
      row._favorited = false
      ElMessage.success('已取消收藏')
    } else {
      await addFavorite(currentDir.value.id, row.relativePath, row.name, row.isDirectory)
      row._favorited = true
      ElMessage.success('已收藏')
    }
  } catch { ElMessage.error('操作失败') }
}

async function showTagDialog(row) {
  tagForm.dirId = currentDir.value.id
  tagForm.filePath = row.relativePath
  try {
    const res = await getFileTags(currentDir.value.id, row.relativePath)
    if (res.success && res.data?.length > 0) {
      tagForm.tag = res.data[0].tag || ''
      tagForm.color = res.data[0].color || ''
      tagForm.note = res.data[0].note || ''
    } else {
      tagForm.tag = ''
      tagForm.color = ''
      tagForm.note = ''
    }
  } catch {
    tagForm.tag = ''
    tagForm.color = ''
    tagForm.note = ''
  }
  tagDialogVisible.value = true
}

async function saveTag() {
  try {
    await setFileTag(tagForm.dirId, tagForm.filePath, tagForm.tag, tagForm.color, tagForm.note)
    ElMessage.success('标签已保存')
    tagDialogVisible.value = false
    loadFileTagsMap()
  } catch { ElMessage.error('保存失败') }
}

async function handlePdfPreview(row) {
  const token = localStorage.getItem('winnas_token')
  previewSrc.value = getStreamUrl(currentDir.value.id, row.relativePath) + '&token=' + encodeURIComponent('Bearer ' + token)
  previewType.value = 'pdf'
  previewContent.value = ''
  previewTitle.value = row.name
  previewVisible.value = true
}

function handleFilePreview(row) {
  if (isOfficeFile(row) || isPdfFile(row)) {
    const token = localStorage.getItem('winnas_token')
    docPreviewSrc.value = getStreamUrl(currentDir.value.id, row.relativePath) + '&token=' + encodeURIComponent('Bearer ' + token)
    docPreviewTitle.value = row.name
    docPreviewFileName.value = row.name
    docPreviewVisible.value = true
  } else {
    handlePreview(row)
  }
}

async function handleExtract(row) {
  try {
    await ElMessageBox.confirm(`确定要解压 ${row.name} 吗？`, '解压文件')
    const res = await extractFile(currentDir.value.id, row.relativePath, null)
    if (res.success) {
      ElMessage.success(res.message)
      loadFiles()
    } else {
      ElMessage.error(res.message)
    }
  } catch {}
}

function handleCopy(row) {
  clipboard.files = [row.relativePath]
  clipboard.sourceDirId = currentDir.value.id
  clipboard.mode = 'copy'
  ElMessage.success('已复制，请到目标目录粘贴')
}

async function handleBatchDelete() {
  try {
    await ElMessageBox.confirm(`确定要删除选中的 ${selectedFiles.value.length} 个文件吗？`, '批量删除')
    const files = selectedFiles.value.map(f => ({ directoryId: currentDir.value.id, relativePath: f.relativePath }))
    const res = await batchDelete(files)
    if (res.success) {
      ElMessage.success(res.message)
      selectedFiles.value = []
      loadFiles()
    }
  } catch {}
}

async function handleBatchDownload() {
  try {
    const files = selectedFiles.value.map(f => ({ directoryId: currentDir.value.id, relativePath: f.relativePath }))
    const res = await batchDownload(files)
    const blob = new Blob([res])
    const url = window.URL.createObjectURL(blob)
    const link = document.createElement('a')
    link.href = url
    link.download = `batch_download_${Date.now()}.zip`
    link.click()
    window.URL.revokeObjectURL(url)
  } catch { ElMessage.error('批量下载失败') }
}

async function handleBatchMove() {
  showMoveDialog.value = true
}

async function handleBatchCopy() {
  clipboard.files = selectedFiles.value.map(f => f.relativePath)
  clipboard.sourceDirId = currentDir.value.id
  clipboard.mode = 'copy'
  ElMessage.success('已复制，请到目标目录粘贴')
}

async function handlePaste() {
  if (!clipboard.files.length) return
  try {
    const res = await copyFiles(clipboard.sourceDirId, currentDir.value.id, currentPath.value, clipboard.files)
    if (res.success) {
      ElMessage.success(res.message)
      loadFiles()
      clipboard.files = []
      clipboard.mode = ''
    }
  } catch { ElMessage.error('粘贴失败') }
}

function getFileIcon(row) {
  if (row.isDirectory) return { icon: 'Folder', color: '#e6a23c' }
  const ext = row.extension?.toLowerCase() || ''
  if (isImageFile(row)) return { icon: 'Picture', color: '#67c23a' }
  if (isVideoFile(row)) return { icon: 'VideoPlay', color: '#409eff' }
  if (isAudioFile(row)) return { icon: 'Headset', color: '#f56c6c' }
  if (['.doc', '.docx'].includes(ext)) return { icon: 'Document', color: '#2b579a' }
  if (['.xls', '.xlsx'].includes(ext)) return { icon: 'Document', color: '#217346' }
  if (['.ppt', '.pptx'].includes(ext)) return { icon: 'Document', color: '#d24726' }
  if (ext === '.pdf') return { icon: 'Document', color: '#f56c6c' }
  if (isTextFile(row)) return { icon: 'Document', color: '#409eff' }
  return { icon: 'Document', color: '#909399' }
}

function startSlideshow() {
  const images = galleryImages.value
  if (images.length === 0) return
  const token = localStorage.getItem('winnas_token')
  slideshowImages.value = images.map(f =>
    getPreviewUrl(currentDir.value.id, f.relativePath) + '&token=Bearer%20' + encodeURIComponent(token)
  )
  slideshowIndex.value = 0
  slideshowActive.value = true
  slideshowAuto.value = false
}

function stopSlideshow() {
  slideshowActive.value = false
  slideshowAuto.value = false
  if (slideshowTimer) { clearInterval(slideshowTimer); slideshowTimer = null }
}

function slideshowPrev() {
  slideshowIndex.value = (slideshowIndex.value - 1 + slideshowImages.value.length) % slideshowImages.value.length
}

function slideshowNext() {
  slideshowIndex.value = (slideshowIndex.value + 1) % slideshowImages.value.length
}

function toggleSlideshowAuto() {
  slideshowAuto.value = !slideshowAuto.value
  if (slideshowAuto.value) {
    slideshowTimer = setInterval(() => slideshowNext(), 3000)
  } else {
    if (slideshowTimer) { clearInterval(slideshowTimer); slideshowTimer = null }
  }
}

function handleSlideshowKey(e) {
  if (!slideshowActive.value) return
  if (e.key === 'ArrowLeft') slideshowPrev()
  else if (e.key === 'ArrowRight') slideshowNext()
  else if (e.key === 'Escape') stopSlideshow()
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
.dir-list { height: calc(100vh - 160px); overflow-y: auto; }
.dir-item { display: flex; align-items: center; justify-content: space-between; padding: 10px 12px; cursor: default; border-radius: 6px; transition: background 0.3s; }
.dir-item:hover { background: #f5f7fa; }
.dir-item.active { background: #ecf5ff; color: #409eff; }
.dir-item-info { display: flex; align-items: center; gap: 8px; cursor: pointer; flex: 1; min-width: 0; }
.dir-item-info span { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.dir-more { cursor: pointer; color: #909399; flex-shrink: 0; }
.dir-more:hover { color: #409eff; }
.file-header { display: flex; justify-content: space-between; align-items: center; }
.file-actions { display: flex; gap: 8px; align-items: center; }
.file-name { display: flex; align-items: center; gap: 8px; }
.file-name-info { display: flex; flex-direction: column; min-width: 0; }
.file-name-text { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.file-meta { font-size: 11px; color: #909399; margin-top: 2px; }
.mobile-more { cursor: pointer; font-size: 18px; color: #606266; }
.empty-text { text-align: center; color: #c0c4cc; padding: 20px; }
.context-menu { position: fixed; z-index: 9999; background: #fff; border-radius: 8px; box-shadow: 0 4px 20px rgba(0,0,0,0.15); padding: 6px 0; min-width: 160px; }
.context-menu-item { display: flex; align-items: center; gap: 8px; padding: 8px 16px; cursor: pointer; font-size: 14px; color: #303133; transition: background 0.2s; }
.context-menu-item:hover { background: #f5f7fa; }
.context-menu-item.danger { color: #f56c6c; }
.context-menu-item.danger:hover { background: #fef0f0; }
.preview-image-container { display: flex; justify-content: center; align-items: center; min-height: 400px; }
.preview-text-container { max-height: calc(100vh - 160px); overflow: auto; }
.preview-text { background: #1e1e1e; color: #d4d4d4; padding: 20px; border-radius: 8px; font-family: 'Consolas', 'Monaco', 'Courier New', monospace; font-size: 13px; line-height: 1.6; white-space: pre-wrap; word-wrap: break-word; margin: 0; }

.md-preview { padding: 20px; background: #fff; border-radius: 8px; line-height: 1.8; color: #303133; }
.md-preview h1 { font-size: 24px; font-weight: 700; margin: 20px 0 12px; padding-bottom: 8px; border-bottom: 2px solid #eaecef; }
.md-preview h2 { font-size: 20px; font-weight: 600; margin: 18px 0 10px; padding-bottom: 6px; border-bottom: 1px solid #eaecef; }
.md-preview h3 { font-size: 17px; font-weight: 600; margin: 16px 0 8px; }
.md-preview h4 { font-size: 15px; font-weight: 600; margin: 14px 0 6px; }
.md-preview p { margin: 8px 0; }
.md-preview ul, .md-preview ol { padding-left: 24px; margin: 8px 0; }
.md-preview li { margin: 4px 0; }
.md-preview code { background: #f0f0f0; color: #e6a23c; padding: 2px 6px; border-radius: 3px; font-family: 'Consolas', 'Monaco', monospace; font-size: 13px; }
.md-preview pre { background: #1e1e1e; color: #d4d4d4; padding: 16px; border-radius: 6px; overflow-x: auto; margin: 12px 0; }
.md-preview pre code { background: transparent; color: inherit; padding: 0; font-size: 13px; }
.md-preview blockquote { border-left: 4px solid #409eff; padding: 8px 16px; margin: 12px 0; color: #666; background: #f9f9f9; border-radius: 0 4px 4px 0; }
.md-preview table { border-collapse: collapse; width: 100%; margin: 12px 0; }
.md-preview th { background: #f5f7fa; font-weight: 600; padding: 8px 12px; border: 1px solid #ebeef5; text-align: left; }
.md-preview td { padding: 8px 12px; border: 1px solid #ebeef5; }
.md-preview a { color: #409eff; text-decoration: none; }
.md-preview a:hover { text-decoration: underline; }
.md-preview img { max-width: 100%; border-radius: 4px; }
.md-preview hr { border: none; border-top: 1px solid #eaecef; margin: 16px 0; }

.gallery-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(150px, 1fr)); gap: 12px; }
.gallery-item { background: #f5f7fa; border-radius: 8px; overflow: hidden; cursor: pointer; transition: all 0.3s; border: 1px solid transparent; }
.gallery-item:hover { background: #ecf5ff; border-color: #409eff; transform: translateY(-2px); }
.gallery-thumb { width: 100%; height: 120px; display: flex; align-items: center; justify-content: center; overflow: hidden; }
.gallery-name { padding: 6px 8px 2px; font-size: 12px; color: #303133; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.gallery-size { padding: 0 8px 6px; font-size: 11px; color: #909399; }

.slideshow-overlay { position: fixed; top: 0; left: 0; right: 0; bottom: 0; background: rgba(0,0,0,0.9); z-index: 10000; display: flex; align-items: center; justify-content: center; }
.slideshow-content { position: relative; width: 100%; height: 100%; display: flex; align-items: center; justify-content: center; }
.slideshow-image { max-width: 90%; max-height: 85%; object-fit: contain; }
.slideshow-close { position: absolute; top: 20px; right: 20px; background: none; border: none; color: #fff; font-size: 36px; cursor: pointer; z-index: 10001; width: 48px; height: 48px; display: flex; align-items: center; justify-content: center; border-radius: 50%; transition: background 0.3s; }
.slideshow-close:hover { background: rgba(255,255,255,0.2); }
.slideshow-prev, .slideshow-next { position: absolute; top: 50%; transform: translateY(-50%); background: rgba(255,255,255,0.15); border: none; color: #fff; font-size: 48px; cursor: pointer; width: 60px; height: 80px; display: flex; align-items: center; justify-content: center; border-radius: 8px; transition: background 0.3s; z-index: 10001; }
.slideshow-prev { left: 20px; }
.slideshow-next { right: 20px; }
.slideshow-prev:hover, .slideshow-next:hover { background: rgba(255,255,255,0.3); }
.slideshow-counter { position: absolute; bottom: 60px; left: 50%; transform: translateX(-50%); color: #fff; font-size: 14px; background: rgba(0,0,0,0.5); padding: 4px 16px; border-radius: 12px; }
.slideshow-controls { position: absolute; bottom: 20px; left: 50%; transform: translateX(-50%); }

@media (max-width: 768px) {
  .page-container { padding: 12px; }
  .page-header { flex-direction: column; align-items: flex-start; gap: 10px; }
  .page-header h2 { font-size: 18px; }
  .dir-list { height: auto !important; margin-bottom: 12px; max-height: 200px; }
  .file-header { flex-direction: column; align-items: flex-start; gap: 8px; }
  .file-actions { flex-wrap: wrap; gap: 6px; }
  .gallery-grid { grid-template-columns: repeat(auto-fill, minmax(100px, 1fr)); gap: 8px; }
  .gallery-thumb { height: 80px; }
  .filter-bar .el-radio-group { flex-wrap: wrap; }
  .el-table { font-size: 12px; }
}

@media (max-width: 480px) {
  .gallery-grid { grid-template-columns: repeat(auto-fill, minmax(80px, 1fr)); }
  .slideshow-prev, .slideshow-next { width: 40px; height: 60px; font-size: 32px; }
}
</style>
