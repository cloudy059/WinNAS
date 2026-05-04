<template>
  <div class="page-container">
    <div class="page-header">
      <h2>系统设置</h2>
    </div>

    <el-row :gutter="20">
      <el-col :xs="24" :sm="24" :md="10">
        <el-card shadow="hover">
          <template #header><span>基本设置</span></template>
          <el-form label-width="120px" label-position="left">
            <el-form-item label="HTTP端口">
              <el-input-number v-model="settings.httpPort" :min="1024" :max="65535" />
            </el-form-item>
            <el-row :gutter="20">
              <el-col :span="12">
                <el-form-item label="允许注册">
                  <el-switch v-model="settings.allowRegister" />
                </el-form-item>
              </el-col>
              <el-col :span="12">
                <el-form-item label="访客模式">
                  <el-switch v-model="settings.allowGuest" />
                </el-form-item>
              </el-col>
            </el-row>
            <el-row :gutter="20">
              <el-col :span="12">
                <el-form-item label="重复文件检查">
                  <el-switch v-model="settings.duplicateCheck" />
                </el-form-item>
              </el-col>
              <el-col :span="12">
                <el-form-item label="开机自启">
                  <el-switch v-model="settings.autoStart" @change="handleAutoStartChange" />
                </el-form-item>
              </el-col>
            </el-row>
            <el-form-item label="最大上传(MB)">
              <el-input-number v-model="settings.maxUploadMB" :min="1" :max="10240" />
            </el-form-item>
            <el-form-item>
              <el-button type="primary" @click="handleSave">保存设置</el-button>
            </el-form-item>
          </el-form>
        </el-card>

        <el-card shadow="hover" style="margin-top:20px">
          <template #header><span style="color:#e6a23c">服务管理</span></template>
          <p style="color:#909399;font-size:13px;margin:0 0 12px">重启服务将重新加载所有配置项，期间服务会短暂中断。修改端口等配置后需要重启才能生效。</p>
          <el-button type="warning" @click="handleRestart" :loading="restarting">重启服务</el-button>
        </el-card>

        <el-card shadow="hover" style="margin-top:20px">
          <template #header><span style="color:#f56c6c">危险操作</span></template>
          <p style="color:#909399;font-size:13px;margin:0 0 12px">初始化将清除所有数据（用户、文件记录、分享、团队、聊天记录等），恢复管理员密码为默认值，系统配置恢复默认。此操作不可逆！</p>
          <el-button type="danger" @click="handleReset">初始化系统</el-button>
        </el-card>

        <el-card shadow="hover" style="margin-top:20px">
          <template #header><span>数据备份与恢复</span></template>
          <div style="display:flex;gap:12px;flex-wrap:wrap;margin-bottom:16px">
            <el-button type="primary" @click="handleBackup" :loading="backupLoading">创建备份</el-button>
            <el-upload :show-file-list="false" :http-request="handleRestore" accept=".zip">
              <el-button type="warning" :loading="restoreLoading">恢复备份</el-button>
            </el-upload>
          </div>
          <el-table :data="backupList" v-if="backupList.length > 0" size="small">
            <el-table-column prop="name" label="备份文件" min-width="200" />
            <el-table-column label="大小" width="100">
              <template #default="{ row }">{{ formatSize(row.size) }}</template>
            </el-table-column>
            <el-table-column label="时间" width="160">
              <template #default="{ row }">{{ new Date(row.createdAt).toLocaleString('zh-CN') }}</template>
            </el-table-column>
            <el-table-column label="操作" width="120">
              <template #default="{ row }">
                <el-button link type="primary" size="small" @click="downloadBackupFile(row.name)">下载</el-button>
                <el-button link type="danger" size="small" @click="handleDeleteBackup(row.name)">删除</el-button>
              </template>
            </el-table-column>
          </el-table>
        </el-card>
      </el-col>

      <el-col :xs="24" :sm="24" :md="14">
        <el-card shadow="hover">
          <template #header><span>配置项</span></template>
          <el-table :data="configs" min-height="200" max-height="600">
            <el-table-column prop="key" label="键" width="140" />
            <el-table-column prop="value" label="值" min-width="200">
              <template #default="{ row }">
                <el-input v-model="row.value" size="small" @blur="handleConfigChange(row)" />
              </template>
            </el-table-column>
            <el-table-column prop="description" label="说明" min-width="160" />
          </el-table>
        </el-card>
      </el-col>
    </el-row>
  </div>
</template>

<script setup>
import { ref, reactive, onMounted } from 'vue'
import { getConfigs, updateConfig, setAutoStart, resetSystem, restartService } from '../../api/system'
import { createBackup, restoreBackup, getBackupList, downloadBackup, deleteBackup } from '../../api/enhanced'
import { ElMessage, ElMessageBox } from 'element-plus'
import { useRouter } from 'vue-router'

const router = useRouter()
const configs = ref([])
const restarting = ref(false)
const settings = reactive({
  httpPort: 8080,
  allowRegister: true,
  allowGuest: true,
  duplicateCheck: true,
  autoStart: false,
  maxUploadMB: 1024
})

onMounted(() => { loadConfigs(); loadBackups() })

const backupLoading = ref(false)
const restoreLoading = ref(false)
const backupList = ref([])

async function loadConfigs() {
  const res = await getConfigs()
  if (res.success) {
    configs.value = res.data.filter(c => !c.key.startsWith('onlyoffice'))
    res.data.forEach(c => {
      if (c.key === 'http_port') settings.httpPort = parseInt(c.value)
      if (c.key === 'allow_register') settings.allowRegister = c.value === 'true'
      if (c.key === 'allow_guest') settings.allowGuest = c.value === 'true'
      if (c.key === 'duplicate_check') settings.duplicateCheck = c.value === 'true'
      if (c.key === 'auto_start') settings.autoStart = c.value === 'true'
      if (c.key === 'max_upload_size') settings.maxUploadMB = Math.round(parseInt(c.value) / 1024 / 1024)
    })
  }
}

async function handleConfigChange(row) {
  await updateConfig({ key: row.key, value: row.value })
  ElMessage.success('配置已更新')
}

async function handleAutoStartChange(val) {
  await setAutoStart(val)
}

async function handleSave() {
  const updates = [
    { key: 'http_port', value: settings.httpPort.toString() },
    { key: 'allow_register', value: settings.allowRegister.toString() },
    { key: 'allow_guest', value: settings.allowGuest.toString() },
    { key: 'duplicate_check', value: settings.duplicateCheck.toString() },
    { key: 'max_upload_size', value: (settings.maxUploadMB * 1024 * 1024).toString() }
  ]
  for (const u of updates) {
    await updateConfig(u)
  }
  await setAutoStart(settings.autoStart)
  ElMessage.success('设置已保存，部分设置需要重启后生效')
}

async function handleRestart() {
  try {
    await ElMessageBox.confirm(
      '重启服务将短暂中断所有连接，确定要重启吗？',
      '重启确认',
      { confirmButtonText: '确认重启', cancelButtonText: '取消', type: 'warning' }
    )
    restarting.value = true
    const res = await restartService()
    if (res.success) {
      ElMessage.success('服务正在重启，请稍候...')
      setTimeout(() => {
        restarting.value = false
        loadConfigs()
      }, 5000)
    } else {
      ElMessage.error(res.message || '重启失败')
      restarting.value = false
    }
  } catch {
    restarting.value = false
  }
}

async function handleBackup() {
  backupLoading.value = true
  try {
    const res = await createBackup()
    if (res.success) {
      ElMessage.success('备份成功')
      loadBackups()
    } else {
      ElMessage.error(res.message || '备份失败')
    }
  } catch { ElMessage.error('备份失败') }
  finally { backupLoading.value = false }
}

async function handleRestore(params) {
  try {
    await ElMessageBox.confirm('恢复备份将覆盖当前数据，确定要继续吗？', '恢复确认', { type: 'warning' })
    restoreLoading.value = true
    const formData = new FormData()
    formData.append('file', params.file)
    const res = await restoreBackup(formData)
    if (res.success) {
      ElMessage.success(res.message)
    } else {
      ElMessage.error(res.message || '恢复失败')
    }
  } catch {}
  finally { restoreLoading.value = false }
}

async function loadBackups() {
  try {
    const res = await getBackupList()
    if (res.success) backupList.value = res.data || []
  } catch {}
}

async function downloadBackupFile(name) {
  try {
    const res = await downloadBackup(name)
    const blob = new Blob([res])
    const url = window.URL.createObjectURL(blob)
    const link = document.createElement('a')
    link.href = url
    link.download = name
    link.click()
    window.URL.revokeObjectURL(url)
  } catch { ElMessage.error('下载失败') }
}

async function handleDeleteBackup(name) {
  try {
    await ElMessageBox.confirm(`确定要删除备份 ${name} 吗？`, '删除确认', { type: 'warning' })
    const res = await deleteBackup(name)
    if (res.success) {
      ElMessage.success('备份已删除')
      loadBackups()
    } else {
      ElMessage.error(res.message || '删除失败')
    }
  } catch {}
}

function formatSize(bytes) {
  if (!bytes) return '0 B'
  const units = ['B', 'KB', 'MB', 'GB', 'TB']
  const i = Math.floor(Math.log(bytes) / Math.log(1024))
  return (bytes / Math.pow(1024, i)).toFixed(1) + ' ' + units[i]
}

async function handleReset() {
  try {
    await ElMessageBox.confirm(
      '此操作将清除所有数据并恢复默认设置，管理员密码将重置为 admin，此操作不可逆！',
      '系统初始化确认',
      { confirmButtonText: '确认初始化', cancelButtonText: '取消', type: 'warning' }
    )
    const { value } = await ElMessageBox.prompt(
      '请输入 "RESET" 确认执行初始化操作',
      '二次确认',
      { confirmButtonText: '执行', cancelButtonText: '取消', inputPattern: /^RESET$/, inputErrorMessage: '请输入正确的确认码' }
    )
    if (value === 'RESET') {
      const res = await resetSystem('RESET')
      if (res.success) {
        ElMessage.success('系统已初始化，即将跳转到登录页')
        localStorage.removeItem('winnas_token')
        setTimeout(() => { router.push('/login') }, 1500)
      } else {
        ElMessage.error(res.message || '初始化失败')
      }
    }
  } catch {}
}
</script>

<style scoped>
.el-form-item {
  margin-bottom: 12px;
}

@media (max-width: 768px) {
  .el-form-item {
    margin-bottom: 8px;
  }
  :deep(.el-form-item__label) {
    font-size: 13px;
    padding-right: 8px;
  }
  :deep(.el-col-12) {
    max-width: 100%;
    flex: 0 0 100%;
  }
}
</style>
