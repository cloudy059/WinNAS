<template>
  <div class="page-container">
    <div class="page-header">
      <h2>网络共享</h2>
    </div>

    <el-row :gutter="20">
      <el-col :xs="24" :sm="12">
        <el-card shadow="hover">
          <template #header>
            <div style="display:flex;align-items:center;justify-content:space-between">
              <span>SMB 共享</span>
              <el-switch v-model="status.smbEnabled" :loading="smbLoading" @change="handleSmbGlobalToggle" />
            </div>
          </template>
          <el-form label-width="80px" label-position="left">
            <el-form-item label="访问地址">
              <div style="display:flex;gap:8px;width:100%">
                <el-input :model-value="status.smbPath" readonly size="small" style="flex:1" />
                <el-button size="small" type="primary" @click="copyToClipboard(status.smbPath)">复制</el-button>
              </div>
            </el-form-item>
          </el-form>

          <div v-if="status.smbShares.length > 0" style="margin-bottom:12px">
            <p style="font-size:12px;color:#303133;margin:0 0 6px;font-weight:600">当前共享列表</p>
            <el-table :data="status.smbShares" size="small" border>
              <el-table-column prop="shareName" label="共享名" min-width="120" />
              <el-table-column prop="currentUses" label="连接" width="60" align="center" />
              <el-table-column label="地址" min-width="140">
                <template #default="{ row }">
                  <div style="display:flex;align-items:center;gap:4px">
                    <span style="font-size:11px">\\{{ status.localIP }}\{{ row.shareName }}</span>
                    <el-button size="small" link type="primary" @click="copyToClipboard(`\\\\${status.localIP}\\${row.shareName}`)">复制</el-button>
                  </div>
                </template>
              </el-table-column>
            </el-table>
          </div>

          <div class="usage-tip" style="padding:10px;background:#f5f7fa;border-radius:4px">
            <p style="font-size:12px;color:#303133;margin:0 0 4px;font-weight:600">使用方法：</p>
            <p style="font-size:12px;color:#606266;margin:0 0 2px">1. 右键"此电脑"→"映射网络驱动器"</p>
            <p style="font-size:12px;color:#606266;margin:0 0 2px">2. 输入共享地址，选择盘符</p>
            <p style="font-size:12px;color:#606266;margin:0 0 2px">3. 勾选"使用其他凭据连接"</p>
            <p style="font-size:12px;color:#606266;margin:0">4. 输入用户名和密码</p>
          </div>
          <div style="margin-top:8px;padding:6px 10px;background:#fdf6ec;border-radius:4px;border:1px solid #faecd8">
            <p style="font-size:12px;color:#e6a23c;margin:0">⚠️ 需要管理员身份运行 WinNAS</p>
          </div>
        </el-card>

        <el-card shadow="hover" style="margin-top:20px">
          <template #header>
            <div style="display:flex;align-items:center;justify-content:space-between">
              <span>WebDAV 服务</span>
              <el-switch v-model="status.webdavEnabled" :loading="webdavLoading" @change="handleWebDavGlobalToggle" />
            </div>
          </template>
          <el-form label-width="80px" label-position="left">
            <el-form-item label="访问地址">
              <div style="display:flex;gap:8px;width:100%">
                <el-input :model-value="status.webdavUrl" readonly size="small" style="flex:1" />
                <el-button size="small" type="primary" @click="copyToClipboard(status.webdavUrl)">复制</el-button>
              </div>
            </el-form-item>
            <el-form-item label="认证方式">
              <span style="font-size:13px;color:#606266">WinNAS 账号密码</span>
            </el-form-item>
          </el-form>
          <div class="usage-tip" style="padding:10px;background:#f5f7fa;border-radius:4px">
            <p style="font-size:12px;color:#303133;margin:0 0 4px;font-weight:600">使用方法：</p>
            <p style="font-size:12px;color:#606266;margin:0 0 2px">1. 打开文件资源管理器</p>
            <p style="font-size:12px;color:#606266;margin:0 0 2px">2. 地址栏输入 WebDAV 地址</p>
            <p style="font-size:12px;color:#606266;margin:0 0 2px">3. 输入用户名和密码</p>
            <p style="font-size:12px;color:#606266;margin:0">4. 即可像本地文件夹操作</p>
          </div>
        </el-card>
      </el-col>

      <el-col :xs="24" :sm="12">
        <el-card shadow="hover">
          <template #header><span>共享目录管理</span></template>
          <el-table :data="directories" size="small">
            <el-table-column prop="name" label="目录名称" min-width="100" />
            <el-table-column prop="path" label="路径" min-width="160" show-overflow-tooltip />
            <el-table-column label="SMB" width="70" align="center">
              <template #default="{ row }">
                <el-switch v-model="row.allowSmb" size="small" @change="val => handleToggleSmb(row, val)" />
              </template>
            </el-table-column>
            <el-table-column label="WebDAV" width="80" align="center">
              <template #default="{ row }">
                <el-switch v-model="row.allowWebDav" size="small" @change="val => handleToggleWebDav(row, val)" />
              </template>
            </el-table-column>
          </el-table>
        </el-card>
      </el-col>
    </el-row>
  </div>
</template>

<script setup>
import { ref, reactive, onMounted } from 'vue'
import { getNetworkShareStatus, toggleSmbShare, toggleWebDavShare, toggleWebDavGlobal, enableAllSmbShare, disableAllSmbShare } from '../../api/system'
import { getDirectories } from '../../api/file'
import { ElMessage } from 'element-plus'

const smbLoading = ref(false)
const webdavLoading = ref(false)
const directories = ref([])

const status = reactive({
  webdavEnabled: true,
  webdavUrl: '',
  smbEnabled: false,
  smbPath: '',
  smbShares: [],
  localIP: ''
})

onMounted(() => {
  loadStatus()
  loadDirectories()
})

async function loadStatus() {
  try {
    const res = await getNetworkShareStatus()
    if (res.success) {
      status.webdavEnabled = res.data.webdavEnabled
      status.webdavUrl = res.data.webdavUrl
      status.smbEnabled = res.data.smbEnabled
      status.smbPath = res.data.smbPath
      status.smbShares = res.data.smbShares || []
      status.localIP = res.data.localIP || ''
    }
  } catch {}
}

async function loadDirectories() {
  try {
    const res = await getDirectories()
    if (res.success) {
      directories.value = res.data
    }
  } catch {}
}

async function handleSmbGlobalToggle(val) {
  smbLoading.value = true
  try {
    const res = val ? await enableAllSmbShare() : await disableAllSmbShare()
    if (res.success) {
      ElMessage.success(res.message || (val ? 'SMB共享已启用' : 'SMB共享已禁用'))
      await loadStatus()
    } else {
      status.smbEnabled = !val
      ElMessage.error(res.message || '操作失败')
    }
  } catch {
    status.smbEnabled = !val
    ElMessage.error('操作失败')
  } finally {
    smbLoading.value = false
  }
}

async function handleWebDavGlobalToggle(val) {
  webdavLoading.value = true
  try {
    const res = await toggleWebDavGlobal(val)
    if (res.success) {
      ElMessage.success(res.message || (val ? 'WebDAV已启用' : 'WebDAV已禁用'))
      await loadStatus()
    } else {
      status.webdavEnabled = !val
      ElMessage.error(res.message || '操作失败')
    }
  } catch {
    status.webdavEnabled = !val
    ElMessage.error('操作失败')
  } finally {
    webdavLoading.value = false
  }
}

async function handleToggleSmb(dir, val) {
  try {
    const res = await toggleSmbShare(dir.id, val)
    if (res.success) {
      ElMessage.success(res.message || (val ? 'SMB共享已启用' : 'SMB共享已关闭'))
      await loadStatus()
    } else {
      dir.allowSmb = !val
      ElMessage.error(res.message || '操作失败')
    }
  } catch {
    dir.allowSmb = !val
    ElMessage.error('操作失败')
  }
}

async function handleToggleWebDav(dir, val) {
  try {
    const res = await toggleWebDavShare(dir.id, val)
    if (res.success) {
      ElMessage.success(res.message || (val ? 'WebDAV共享已启用' : 'WebDAV共享已关闭'))
    } else {
      dir.allowWebDav = !val
      ElMessage.error(res.message || '操作失败')
    }
  } catch {
    dir.allowWebDav = !val
    ElMessage.error('操作失败')
  }
}

function copyToClipboard(text) {
  if (!text) {
    ElMessage.warning('地址为空')
    return
  }
  if (navigator.clipboard && navigator.clipboard.writeText) {
    navigator.clipboard.writeText(text).then(() => {
      ElMessage.success('已复制到剪贴板')
    }).catch(() => {
      fallbackCopy(text)
    })
  } else {
    fallbackCopy(text)
  }
}

function fallbackCopy(text) {
  const textarea = document.createElement('textarea')
  textarea.value = text
  textarea.style.position = 'fixed'
  textarea.style.opacity = '0'
  document.body.appendChild(textarea)
  textarea.select()
  try {
    document.execCommand('copy')
    ElMessage.success('已复制到剪贴板')
  } catch {
    ElMessage.error('复制失败，请手动复制')
  }
  document.body.removeChild(textarea)
}
</script>

<style scoped>
.el-form-item {
  margin-bottom: 10px;
}

.usage-tip p {
  white-space: nowrap;
}
</style>
