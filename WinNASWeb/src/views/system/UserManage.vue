<template>
  <div class="page-container">
    <div class="page-header">
      <h2>用户管理</h2>
    </div>

    <el-table :data="users" v-loading="loading">
      <el-table-column prop="id" label="ID" :width="isMobile ? '50' : '80'" />
      <el-table-column prop="username" label="用户名" :width="isMobile ? '100' : '150'" />
      <el-table-column v-if="!isMobile" prop="nickname" label="昵称" width="150" />
      <el-table-column v-if="!isMobile" prop="role" label="角色" width="100">
        <template #default="{ row }">
          <el-tag :type="row.role === 'admin' ? 'danger' : 'info'" size="small">
            {{ row.role === 'admin' ? '管理员' : '用户' }}
          </el-tag>
        </template>
      </el-table-column>
      <el-table-column v-if="!isMobile" prop="isEnabled" label="状态" width="80">
        <template #default="{ row }">
          <el-tag :type="row.isEnabled ? 'success' : 'danger'" size="small">
            {{ row.isEnabled ? '启用' : '禁用' }}
          </el-tag>
        </template>
      </el-table-column>
      <el-table-column v-if="!isMobile" prop="storageLimit" label="存储限制" width="120">
        <template #default="{ row }">
          {{ row.storageLimit === -1 ? '不限' : formatSize(row.storageLimit) }}
        </template>
      </el-table-column>
      <el-table-column v-if="!isMobile" prop="createdAt" label="创建时间" width="180" />
      <el-table-column label="操作" :width="isMobile ? '60' : '250'" :fixed="isMobile ? false : 'right'">
        <template #default="{ row }">
          <template v-if="!isMobile">
            <el-button link type="primary" @click="handleEdit(row)">编辑</el-button>
            <el-button link :type="row.isEnabled ? 'warning' : 'success'" @click="toggleEnabled(row)">
              {{ row.isEnabled ? '禁用' : '启用' }}
            </el-button>
            <el-button link type="danger" @click="handleDelete(row)" :disabled="row.role === 'admin'">删除</el-button>
          </template>
          <el-dropdown v-else trigger="click">
            <el-icon class="mobile-more"><MoreFilled /></el-icon>
            <template #dropdown>
              <el-dropdown-menu>
                <el-dropdown-item @click="handleEdit(row)"><el-icon><Edit /></el-icon>编辑</el-dropdown-item>
                <el-dropdown-item v-if="row.isEnabled" @click="toggleEnabled(row)"><el-icon><Lock /></el-icon>禁用</el-dropdown-item>
                <el-dropdown-item v-if="!row.isEnabled" @click="toggleEnabled(row)"><el-icon><Unlock /></el-icon>启用</el-dropdown-item>
                <el-dropdown-item v-if="row.role !== 'admin'" @click="handleDelete(row)" style="color:#f56c6c"><el-icon><Delete /></el-icon>删除</el-dropdown-item>
              </el-dropdown-menu>
            </template>
          </el-dropdown>
        </template>
      </el-table-column>
    </el-table>

    <el-dialog v-model="editVisible" title="编辑用户" width="500px">
      <el-form :model="editForm" label-width="100px">
        <el-form-item label="昵称">
          <el-input v-model="editForm.nickname" />
        </el-form-item>
        <el-form-item label="角色">
          <el-select v-model="editForm.role">
            <el-option label="管理员" value="admin" />
            <el-option label="用户" value="user" />
          </el-select>
        </el-form-item>
        <el-form-item label="存储限制(MB)">
          <el-input-number v-model="editForm.storageLimitMB" :min="-1" />
          <span style="margin-left: 10px; color: #909399">-1 表示不限</span>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="editVisible = false">取消</el-button>
        <el-button type="primary" @click="handleSave">保存</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup>
import { ref, reactive, onMounted, onUnmounted } from 'vue'
import { getUsers, updateUser, deleteUser } from '../../api/auth'
import { ElMessage, ElMessageBox } from 'element-plus'
import { MoreFilled, Edit, Lock, Unlock, Delete } from '@element-plus/icons-vue'

const users = ref([])
const loading = ref(false)
const editVisible = ref(false)
const editForm = reactive({ id: 0, nickname: '', role: 'user', storageLimitMB: -1 })
const isMobile = ref(window.innerWidth < 768)

onMounted(() => { loadUsers(); window.addEventListener('resize', checkMobile) })
onUnmounted(() => { window.removeEventListener('resize', checkMobile) })

function checkMobile() { isMobile.value = window.innerWidth < 768 }

async function loadUsers() {
  loading.value = true
  try {
    const res = await getUsers()
    if (res.success) users.value = res.data
  } finally {
    loading.value = false
  }
}

function handleEdit(row) {
  editForm.id = row.id
  editForm.nickname = row.nickname
  editForm.role = row.role
  editForm.storageLimitMB = row.storageLimit === -1 ? -1 : Math.round(row.storageLimit / 1024 / 1024)
  editVisible.value = true
}

async function handleSave() {
  const data = {
    role: editForm.role,
    storageLimit: editForm.storageLimitMB === -1 ? -1 : editForm.storageLimitMB * 1024 * 1024
  }
  await updateUser(editForm.id, data)
  ElMessage.success('更新成功')
  editVisible.value = false
  loadUsers()
}

async function toggleEnabled(row) {
  await updateUser(row.id, { isEnabled: !row.isEnabled })
  ElMessage.success('操作成功')
  loadUsers()
}

async function handleDelete(row) {
  await ElMessageBox.confirm(`确定删除用户 ${row.username} 吗？`, '确认', { type: 'warning' })
  await deleteUser(row.id)
  ElMessage.success('删除成功')
  loadUsers()
}

function formatSize(bytes) {
  if (!bytes) return '0 B'
  const units = ['B', 'KB', 'MB', 'GB', 'TB']
  const i = Math.floor(Math.log(bytes) / Math.log(1024))
  return (bytes / Math.pow(1024, i)).toFixed(1) + ' ' + units[i]
}
</script>

<style scoped>
.mobile-more { cursor: pointer; font-size: 18px; color: #606266; }
</style>
