<template>
  <div class="page-container">
    <div class="page-header">
      <h2>系统信息</h2>
      <el-button @click="loadInfo" icon="Refresh">刷新</el-button>
    </div>

    <el-row :gutter="20">
      <el-col :xs="24" :sm="24" :md="12">
        <el-card shadow="hover">
          <template #header><span>系统状态</span></template>
          <el-descriptions :column="1" border>
            <el-descriptions-item label="CPU使用率">
              <div class="progress-row">
                <el-progress :percentage="info.cpuUsage" :color="getCpuColor(info.cpuUsage)" :stroke-width="18" :text-inside="true" style="flex:1" />
              </div>
            </el-descriptions-item>
            <el-descriptions-item label="内存使用率">
              <div class="progress-row">
                <el-progress :percentage="memoryUsagePercent" :color="memoryUsagePercent > 80 ? '#f56c6c' : '#67c23a'" :stroke-width="18" :text-inside="true" style="flex:1" />
                <span class="progress-detail">{{ formatSize(info.usedMemory) }} / {{ formatSize(info.totalMemory) }}</span>
              </div>
            </el-descriptions-item>
            <el-descriptions-item label="磁盘使用率">
              <div class="progress-row">
                <el-progress :percentage="info.diskUsage" :color="info.diskUsage > 80 ? '#f56c6c' : '#409eff'" :stroke-width="18" :text-inside="true" style="flex:1" />
                <span class="progress-detail">{{ formatSize(info.diskFree) }} 可用</span>
              </div>
            </el-descriptions-item>
            <el-descriptions-item label="磁盘总量">{{ formatSize(info.diskTotal) }}</el-descriptions-item>
          </el-descriptions>
        </el-card>
      </el-col>

      <el-col :xs="24" :sm="24" :md="12">
        <el-card shadow="hover">
          <template #header><span>网络信息</span></template>
          <el-descriptions :column="1" border>
            <el-descriptions-item label="HTTP端口">{{ info.httpPort }}</el-descriptions-item>
            <el-descriptions-item label="允许注册">
              <el-tag :type="info.allowRegister ? 'success' : 'danger'" size="small">
                {{ info.allowRegister ? '是' : '否' }}
              </el-tag>
            </el-descriptions-item>
            <el-descriptions-item label="访客模式">
              <el-tag :type="info.allowGuest ? 'success' : 'danger'" size="small">
                {{ info.allowGuest ? '是' : '否' }}
              </el-tag>
            </el-descriptions-item>
          </el-descriptions>

          <div style="margin-top: 20px">
            <h4 style="margin-bottom: 10px">网卡列表</h4>
            <div v-for="net in info.networks" :key="net.id" class="network-item">
              <span class="net-name">{{ net.name }}</span>
              <span class="net-ip">{{ net.ipAddress }}</span>
            </div>
          </div>
        </el-card>
      </el-col>
    </el-row>
  </div>
</template>

<script setup>
import { ref, reactive, computed, onMounted, onUnmounted } from 'vue'
import { getSystemInfo } from '../../api/system'

const info = reactive({
  cpuUsage: 0,
  diskUsage: 0,
  diskTotal: 0,
  diskFree: 0,
  totalMemory: 0,
  usedMemory: 0,
  httpPort: 8080,
  allowRegister: false,
  allowGuest: false,
  networks: []
})

let timer = null

const memoryUsagePercent = computed(() => {
  if (!info.totalMemory) return 0
  return Math.round(info.usedMemory / info.totalMemory * 100)
})

onMounted(() => {
  loadInfo()
  timer = setInterval(loadInfo, 5000)
})

onUnmounted(() => {
  if (timer) clearInterval(timer)
})

async function loadInfo() {
  const res = await getSystemInfo()
  if (res.success) Object.assign(info, res.data)
}

function getCpuColor(val) {
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
</script>

<style scoped>
.progress-row {
  display: flex;
  align-items: center;
  gap: 12px;
}

.progress-detail {
  font-size: 12px;
  color: #909399;
  white-space: nowrap;
}

.network-item {
  display: flex;
  justify-content: space-between;
  padding: 8px 0;
  border-bottom: 1px solid #f0f0f0;
}

.net-name {
  color: #606266;
}

.net-ip {
  color: #409eff;
  font-family: monospace;
}
</style>
