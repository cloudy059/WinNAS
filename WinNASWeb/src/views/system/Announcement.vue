<template>
  <div class="page-container">
    <div class="page-header">
      <h2>公告管理</h2>
      <el-button type="primary" @click="showCreateDialog">发布公告</el-button>
    </div>

    <el-table :data="announcements" v-loading="loading">
      <el-table-column prop="id" label="ID" :width="isMobile ? '50' : '80'" />
      <el-table-column prop="title" label="标题" min-width="200" />
      <el-table-column v-if="!isMobile" prop="content" label="内容" min-width="300" show-overflow-tooltip />
      <el-table-column v-if="!isMobile" label="置顶" width="80">
        <template #default="{ row }">
          <el-tag :type="row.isPinned ? 'danger' : 'info'" size="small">
            {{ row.isPinned ? '是' : '否' }}
          </el-tag>
        </template>
      </el-table-column>
      <el-table-column v-if="!isMobile" prop="createdAt" label="发布时间" width="180" />
      <el-table-column label="操作" :width="isMobile ? '60' : '140'" :fixed="isMobile ? false : 'right'">
        <template #default="{ row }">
          <template v-if="!isMobile">
            <el-button link type="primary" @click="showEditDialog(row)">编辑</el-button>
            <el-button link type="danger" @click="handleDelete(row)">删除</el-button>
          </template>
          <el-dropdown v-else trigger="click">
            <el-icon class="mobile-more"><MoreFilled /></el-icon>
            <template #dropdown>
              <el-dropdown-menu>
                <el-dropdown-item @click="showEditDialog(row)"><el-icon><Edit /></el-icon>编辑</el-dropdown-item>
                <el-dropdown-item @click="handleDelete(row)" style="color:#f56c6c"><el-icon><Delete /></el-icon>删除</el-dropdown-item>
              </el-dropdown-menu>
            </template>
          </el-dropdown>
        </template>
      </el-table-column>
    </el-table>

    <el-dialog v-model="dialogVisible" :title="isEditing ? '编辑公告' : '发布公告'" width="600px">
      <el-form :model="form" label-width="80px">
        <el-form-item label="标题">
          <el-input v-model="form.title" placeholder="请输入公告标题" />
        </el-form-item>
        <el-form-item label="内容">
          <el-input v-model="form.content" type="textarea" :rows="6" placeholder="请输入公告内容" />
        </el-form-item>
        <el-form-item label="置顶">
          <el-switch v-model="form.isPinned" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible = false">取消</el-button>
        <el-button type="primary" @click="handleSubmit">{{ isEditing ? '保存' : '发布' }}</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup>
import { ref, reactive, onMounted, onUnmounted } from 'vue'
import { getAnnouncements, createAnnouncement, updateAnnouncement, deleteAnnouncement } from '../../api/system'
import { ElMessage, ElMessageBox } from 'element-plus'
import { MoreFilled, Edit, Delete } from '@element-plus/icons-vue'

const announcements = ref([])
const loading = ref(false)
const dialogVisible = ref(false)
const isEditing = ref(false)
const editingId = ref(null)
const form = reactive({ title: '', content: '', isPinned: false })
const isMobile = ref(window.innerWidth < 768)

onMounted(() => { loadAnnouncements(); window.addEventListener('resize', checkMobile) })
onUnmounted(() => { window.removeEventListener('resize', checkMobile) })

function checkMobile() { isMobile.value = window.innerWidth < 768 }

async function loadAnnouncements() {
  loading.value = true
  try {
    const res = await getAnnouncements()
    if (res.success) announcements.value = res.data
  } finally {
    loading.value = false
  }
}

function showCreateDialog() {
  isEditing.value = false
  editingId.value = null
  Object.assign(form, { title: '', content: '', isPinned: false })
  dialogVisible.value = true
}

function showEditDialog(row) {
  isEditing.value = true
  editingId.value = row.id
  Object.assign(form, { title: row.title, content: row.content, isPinned: row.isPinned })
  dialogVisible.value = true
}

async function handleSubmit() {
  if (isEditing.value) {
    const res = await updateAnnouncement(editingId.value, form)
    if (res.success) {
      ElMessage.success('更新成功')
      dialogVisible.value = false
      loadAnnouncements()
    } else {
      ElMessage.error(res.message || '更新失败')
    }
  } else {
    const res = await createAnnouncement(form)
    if (res.success) {
      ElMessage.success('发布成功')
      dialogVisible.value = false
      loadAnnouncements()
    }
  }
}

async function handleDelete(row) {
  await ElMessageBox.confirm('确定删除此公告吗？', '确认', { type: 'warning' })
  await deleteAnnouncement(row.id)
  ElMessage.success('删除成功')
  loadAnnouncements()
}
</script>

<style scoped>
.mobile-more { cursor: pointer; font-size: 18px; color: #606266; }
</style>
