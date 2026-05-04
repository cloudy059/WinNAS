<template>
  <div class="page-container">
    <div class="page-header">
      <h2>回收站</h2>
      <el-button type="danger" @click="emptyRecycle" :disabled="items.length === 0">清空回收站</el-button>
    </div>

    <el-card shadow="hover">
      <el-empty v-if="items.length === 0" description="回收站为空" />
      <el-table v-else :data="items" v-loading="loading">
        <el-table-column label="文件名" min-width="200">
          <template #default="{ row }">
            <div style="display:flex;align-items:center;gap:8px">
              <el-icon :color="row.isDirectory ? '#e6a23c' : '#409eff'"><component :is="row.isDirectory ? 'Folder' : 'Document'" /></el-icon>
              <span>{{ row.fileName }}</span>
            </div>
          </template>
        </el-table-column>
        <el-table-column label="原始路径" min-width="200">
          <template #default="{ row }">
            <span style="font-size:12px;color:var(--el-text-color-secondary)">{{ row.originalPath }}</span>
          </template>
        </el-table-column>
        <el-table-column label="大小" width="100">
          <template #default="{ row }">{{ row.isDirectory ? '-' : formatSize(row.size) }}</template>
        </el-table-column>
        <el-table-column label="删除时间" width="160">
          <template #default="{ row }">{{ formatDate(row.createdAt) }}</template>
        </el-table-column>
        <el-table-column label="操作" width="160" fixed="right">
          <template #default="{ row }">
            <el-button link type="primary" @click="restoreItem(row)">恢复</el-button>
            <el-button link type="danger" @click="purgeItem(row)">彻底删除</el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>
  </div>
</template>

<script setup>
import { ref, onMounted } from 'vue'
import { getRecycleList, restoreFromRecycle, purgeFromRecycle, emptyRecycle as emptyRecycleApi } from '../../api/enhanced'
import { ElMessage, ElMessageBox } from 'element-plus'

const items = ref([])
const loading = ref(false)

onMounted(loadItems)

async function loadItems() {
  loading.value = true
  try {
    const res = await getRecycleList()
    if (res.success) items.value = res.data || []
  } finally { loading.value = false }
}

async function restoreItem(row) {
  try {
    const res = await restoreFromRecycle(row.id)
    if (res.success) {
      ElMessage.success(res.message)
      loadItems()
    } else {
      ElMessage.error(res.message)
    }
  } catch { ElMessage.error('恢复失败') }
}

async function purgeItem(row) {
  try {
    await ElMessageBox.confirm(`确定要彻底删除 "${row.fileName}" 吗？此操作不可恢复！`, '彻底删除', { type: 'warning' })
    const res = await purgeFromRecycle(row.id)
    if (res.success) {
      ElMessage.success(res.message)
      loadItems()
    } else {
      ElMessage.error(res.message)
    }
  } catch {}
}

async function emptyRecycle() {
  try {
    await ElMessageBox.confirm('确定要清空回收站吗？所有文件将被彻底删除，不可恢复！', '清空回收站', { type: 'warning' })
    const res = await emptyRecycleApi()
    if (res.success) {
      ElMessage.success(res.message)
      loadItems()
    }
  } catch {}
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
.page-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 20px;
}
</style>
