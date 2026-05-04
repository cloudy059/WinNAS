<template>
  <div class="page-container">
    <div class="page-header">
      <h2>分享管理</h2>
      <el-button type="primary" @click="showCreateDialog">创建分享</el-button>
    </div>

    <el-table :data="shares" v-loading="loading">
      <el-table-column prop="code" label="分享码" :width="isMobile ? '100' : '120'" />
      <el-table-column prop="path" label="路径" min-width="200" show-overflow-tooltip />
      <el-table-column v-if="!isMobile" label="密码保护" width="100">
        <template #default="{ row }">
          <el-tag :type="row.password ? 'danger' : 'info'" size="small">
            {{ row.password ? '有密码' : '无密码' }}
          </el-tag>
        </template>
      </el-table-column>
      <el-table-column v-if="!isMobile" label="过期时间" width="180">
        <template #default="{ row }">
          {{ row.expiresAt ? formatDate(row.expiresAt) : '永久' }}
        </template>
      </el-table-column>
      <el-table-column v-if="!isMobile" label="下载次数" width="120">
        <template #default="{ row }">
          {{ row.downloadCount }} / {{ row.maxDownloads === -1 ? '不限' : row.maxDownloads }}
        </template>
      </el-table-column>
      <el-table-column v-if="!isMobile" label="状态" width="80">
        <template #default="{ row }">
          <el-tag :type="row.isEnabled ? 'success' : 'danger'" size="small">
            {{ row.isEnabled ? '有效' : '已禁用' }}
          </el-tag>
        </template>
      </el-table-column>
      <el-table-column label="操作" :width="isMobile ? '60' : '240'" :fixed="isMobile ? false : 'right'">
        <template #default="{ row }">
          <template v-if="!isMobile">
            <el-button link type="primary" @click="copyShareLink(row)">复制链接</el-button>
            <el-button link type="primary" @click="showQrCode(row)">二维码</el-button>
            <el-button link type="warning" @click="handleDisable(row)" v-if="row.isEnabled">禁用</el-button>
            <el-button link type="success" @click="handleEnable(row)" v-if="!row.isEnabled">启用</el-button>
            <el-button link type="danger" @click="handleDelete(row)">删除</el-button>
          </template>
          <el-dropdown v-else trigger="click">
            <el-icon class="mobile-more"><MoreFilled /></el-icon>
            <template #dropdown>
              <el-dropdown-menu>
                <el-dropdown-item @click="copyShareLink(row)"><el-icon><Link /></el-icon>复制链接</el-dropdown-item>
                <el-dropdown-item @click="showQrCode(row)"><el-icon><View /></el-icon>二维码</el-dropdown-item>
                <el-dropdown-item v-if="row.isEnabled" @click="handleDisable(row)"><el-icon><Lock /></el-icon>禁用</el-dropdown-item>
                <el-dropdown-item v-if="!row.isEnabled" @click="handleEnable(row)"><el-icon><Unlock /></el-icon>启用</el-dropdown-item>
                <el-dropdown-item @click="handleDelete(row)" style="color:#f56c6c"><el-icon><Delete /></el-icon>删除</el-dropdown-item>
              </el-dropdown-menu>
            </template>
          </el-dropdown>
        </template>
      </el-table-column>
    </el-table>

    <el-dialog v-model="createVisible" title="创建分享" width="500px">
      <el-form :model="shareForm" label-width="100px">
        <el-form-item label="选择目录">
          <el-select v-model="shareForm.directoryId" placeholder="请选择目录" style="width: 100%">
            <el-option v-for="dir in directories" :key="dir.id" :label="dir.name" :value="dir.id" />
          </el-select>
        </el-form-item>
        <el-form-item label="访问密码">
          <el-input v-model="shareForm.password" placeholder="留空则无密码" />
        </el-form-item>
        <el-form-item label="过期时间">
          <el-date-picker v-model="shareForm.expiresAt" type="datetime" placeholder="留空则永不过期" style="width: 100%" />
        </el-form-item>
        <el-form-item label="最大下载次数">
          <el-input-number v-model="shareForm.maxDownloads" :min="-1" :step="1" />
          <span style="margin-left: 10px; color: #909399">-1 表示不限</span>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="createVisible = false">取消</el-button>
        <el-button type="primary" @click="handleCreate">创建</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="qrVisible" title="分享二维码" width="360px">
      <div style="display:flex;flex-direction:column;align-items:center;gap:16px">
        <QrcodeVue :value="qrLink" :size="240" level="M" />
        <div style="font-size:13px;color:#606266;word-break:break-all;text-align:center;max-width:300px">{{ qrLink }}</div>
        <el-button type="primary" size="small" @click="copyQrLink">复制链接</el-button>
      </div>
    </el-dialog>
  </div>
</template>

<script setup>
import { ref, reactive, onMounted, onUnmounted } from 'vue'
import { getShares, createShare, disableShare, enableShare, deleteShare } from '../../api/share'
import { getDirectories } from '../../api/file'
import { getServerAddress } from '../../api/system'
import { ElMessage, ElMessageBox } from 'element-plus'
import { MoreFilled, View, Link, Lock, Unlock, Delete } from '@element-plus/icons-vue'
import QrcodeVue from 'qrcode.vue'

const shares = ref([])
const directories = ref([])
const loading = ref(false)
const createVisible = ref(false)
const serverUrl = ref('')
const qrVisible = ref(false)
const qrLink = ref('')
const isMobile = ref(window.innerWidth < 768)

const shareForm = reactive({
  directoryId: null,
  path: '',
  password: '',
  expiresAt: null,
  maxDownloads: -1
})

onMounted(() => { loadShares(); loadDirectories(); loadServerAddress(); window.addEventListener('resize', checkMobile) })
onUnmounted(() => { window.removeEventListener('resize', checkMobile) })

function checkMobile() { isMobile.value = window.innerWidth < 768 }

async function loadServerAddress() {
  try {
    const res = await getServerAddress()
    if (res.success) serverUrl.value = res.data.url
  } catch {}
}

async function loadShares() {
  loading.value = true
  try {
    const res = await getShares()
    if (res.success) shares.value = res.data
  } finally {
    loading.value = false
  }
}

async function loadDirectories() {
  const res = await getDirectories()
  if (res.success) directories.value = res.data
}

function showCreateDialog() {
  Object.assign(shareForm, { directoryId: null, path: '', password: '', expiresAt: null, maxDownloads: -1 })
  createVisible.value = true
}

async function handleCreate() {
  const res = await createShare(shareForm)
  if (res.success) {
    ElMessage.success('分享创建成功')
    createVisible.value = false
    loadShares()
  }
}

async function copyShareLink(row) {
  const base = serverUrl.value || window.location.origin
  const link = `${base}/#/share/${row.code}`
  try {
    await navigator.clipboard.writeText(link)
    ElMessage.success('链接已复制到剪贴板')
  } catch {
    const input = document.createElement('input')
    input.value = link
    document.body.appendChild(input)
    input.select()
    document.execCommand('copy')
    document.body.removeChild(input)
    ElMessage.success('链接已复制到剪贴板')
  }
}

function showQrCode(row) {
  const base = serverUrl.value || window.location.origin
  qrLink.value = `${base}/#/share/${row.code}`
  qrVisible.value = true
}

function copyQrLink() {
  if (navigator.clipboard && navigator.clipboard.writeText) {
    navigator.clipboard.writeText(qrLink.value).then(() => {
      ElMessage.success('链接已复制到剪贴板')
    })
  } else {
    const input = document.createElement('input')
    input.value = qrLink.value
    document.body.appendChild(input)
    input.select()
    document.execCommand('copy')
    document.body.removeChild(input)
    ElMessage.success('链接已复制到剪贴板')
  }
}

async function handleDisable(row) {
  await ElMessageBox.confirm('确定禁用此分享吗？', '确认', { type: 'warning' })
  await disableShare(row.id)
  ElMessage.success('已禁用')
  loadShares()
}

async function handleEnable(row) {
  await enableShare(row.id)
  ElMessage.success('已启用')
  loadShares()
}

async function handleDelete(row) {
  await ElMessageBox.confirm('确定删除此分享吗？', '确认', { type: 'warning' })
  await deleteShare(row.id)
  ElMessage.success('已删除')
  loadShares()
}

function formatDate(dateStr) {
  if (!dateStr) return ''
  return new Date(dateStr).toLocaleString('zh-CN')
}
</script>

<style scoped>
.mobile-more { cursor: pointer; font-size: 18px; color: #606266; }
</style>
