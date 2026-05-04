<template>
  <div class="page-container">
    <div class="page-header">
      <h2>团队协作</h2>
      <el-button type="primary" @click="showCreateDialog">创建团队</el-button>
    </div>

    <el-row :gutter="20">
      <el-col :xs="24" :sm="12" :md="8" v-for="team in teams" :key="team.id">
        <el-card shadow="hover" class="team-card" :class="{ 'my-team': team.creatorId === currentUserId }">
          <div class="team-avatar">
            <el-avatar :size="48" icon="UserFilled" />
          </div>
          <div class="team-info" @click="$router.push(`/teams/${team.id}`)">
            <h3>
              {{ team.name }}
              <el-tag v-if="team.creatorId === currentUserId" type="warning" size="small" style="margin-left:6px">我创建的</el-tag>
              <el-tag v-else type="info" size="small" style="margin-left:6px">参与的</el-tag>
            </h3>
            <p>{{ team.description || '暂无描述' }}</p>
            <p class="creator-text">创建者: {{ team.creatorName || '未知' }}</p>
          </div>
          <el-dropdown v-if="team.creatorId === currentUserId" trigger="click" @command="(cmd) => handleTeamCommand(cmd, team)">
            <el-icon class="team-more"><MoreFilled /></el-icon>
            <template #dropdown>
              <el-dropdown-menu>
                <el-dropdown-item command="edit"><el-icon><Edit /></el-icon>编辑</el-dropdown-item>
                <el-dropdown-item command="delete" style="color:#f56c6c"><el-icon><Delete /></el-icon>删除</el-dropdown-item>
              </el-dropdown-menu>
            </template>
          </el-dropdown>
        </el-card>
      </el-col>
    </el-row>

    <el-dialog v-model="dialogVisible" :title="isEditing ? '编辑团队' : '创建团队'" width="560px">
      <el-form :model="teamForm" label-width="100px">
        <el-form-item label="团队名称">
          <el-input v-model="teamForm.name" placeholder="请输入团队名称" />
        </el-form-item>
        <el-form-item label="团队描述">
          <el-input v-model="teamForm.description" type="textarea" :rows="3" placeholder="请输入团队描述" />
        </el-form-item>
        <el-form-item label="存储路径">
          <el-input v-model="teamForm.storagePath" placeholder="留空使用默认路径" :disabled="isEditing" />
        </el-form-item>
        <el-form-item label="团队成员">
          <el-select v-model="teamForm.memberIds" multiple placeholder="选择成员" style="width: 100%" filterable>
            <el-option v-for="u in allUsers" :key="u.id" :label="`${u.nickname || u.username} (${u.username})`" :value="u.id" />
          </el-select>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible = false">取消</el-button>
        <el-button type="primary" @click="handleSubmit" :loading="submitting">{{ isEditing ? '保存' : '创建' }}</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup>
import { ref, reactive, computed, onMounted } from 'vue'
import { getTeams, createTeam, updateTeam, deleteTeam, getTeamMembers, addMember, removeMember } from '../../api/team'
import { getUserList } from '../../api/auth'
import { useUserStore } from '../../store/user'
import { ElMessage, ElMessageBox } from 'element-plus'

const userStore = useUserStore()
const currentUserId = computed(() => userStore.userInfo?.id)
const teams = ref([])
const allUsers = ref([])
const dialogVisible = ref(false)
const isEditing = ref(false)
const editingId = ref(null)
const submitting = ref(false)
const teamForm = reactive({ name: '', description: '', storagePath: '', memberIds: [] })
const originalMemberIds = ref([])

onMounted(() => { loadTeams(); loadUsers() })

async function loadTeams() {
  const res = await getTeams()
  if (res.success) teams.value = res.data
}

async function loadUsers() {
  try {
    const res = await getUserList()
    if (res.success) allUsers.value = res.data
  } catch {}
}

function showCreateDialog() {
  isEditing.value = false
  editingId.value = null
  Object.assign(teamForm, { name: '', description: '', storagePath: '', memberIds: [] })
  originalMemberIds.value = []
  dialogVisible.value = true
}

async function handleTeamCommand(cmd, team) {
  if (cmd === 'edit') {
    isEditing.value = true
    editingId.value = team.id
    Object.assign(teamForm, { name: team.name, description: team.description || '', storagePath: team.storagePath || '', memberIds: [] })
    try {
      const res = await getTeamMembers(team.id)
      if (res.success) {
        const memberIds = res.data.map(m => m.userId)
        teamForm.memberIds = memberIds
        originalMemberIds.value = [...memberIds]
      }
    } catch {}
    dialogVisible.value = true
  } else if (cmd === 'delete') {
    handleDelete(team)
  }
}

async function handleSubmit() {
  if (!teamForm.name) { ElMessage.warning('请输入团队名称'); return }
  submitting.value = true
  try {
    if (isEditing.value) {
      const res = await updateTeam(editingId.value, { name: teamForm.name, description: teamForm.description })
      if (res.success) {
        const toAdd = teamForm.memberIds.filter(id => !originalMemberIds.value.includes(id))
        const toRemove = originalMemberIds.value.filter(id => !teamForm.memberIds.includes(id))
        for (const uid of toAdd) {
          if (uid !== currentUserId.value) {
            try { await addMember(editingId.value, { userId: uid, role: 'member' }) } catch {}
          }
        }
        for (const uid of toRemove) {
          if (uid !== currentUserId.value) {
            try { await removeMember(editingId.value, uid) } catch {}
          }
        }
        ElMessage.success('更新成功')
        dialogVisible.value = false
        loadTeams()
      } else {
        ElMessage.error(res.message || '更新失败')
      }
    } else {
      const res = await createTeam({
        name: teamForm.name,
        description: teamForm.description,
        storagePath: teamForm.storagePath || undefined,
        memberIds: teamForm.memberIds.length > 0 ? teamForm.memberIds : undefined
      })
      if (res.success) {
        ElMessage.success('团队创建成功')
        dialogVisible.value = false
        loadTeams()
      } else {
        ElMessage.error(res.message || '创建失败')
      }
    }
  } finally {
    submitting.value = false
  }
}

async function handleDelete(team) {
  await ElMessageBox.confirm(`确定删除团队「${team.name}」吗？删除后不可恢复。`, '删除确认', { type: 'warning' })
  const res = await deleteTeam(team.id)
  if (res.success) {
    ElMessage.success('删除成功')
    loadTeams()
  } else {
    ElMessage.error(res.message || '删除失败')
  }
}
</script>

<style scoped>
.team-card {
  transition: transform 0.3s;
  margin-bottom: 20px;
}
.team-card:hover {
  transform: translateY(-4px);
}
.team-card.my-team {
  border-left: 3px solid #e6a23c;
}
.team-card :deep(.el-card__body) {
  display: flex;
  align-items: center;
  gap: 15px;
}
.team-info {
  flex: 1;
  min-width: 0;
  cursor: pointer;
}
.team-info h3 {
  font-size: 16px;
  color: #303133;
  margin-bottom: 4px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.team-info p {
  font-size: 13px;
  color: #909399;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.creator-text {
  font-size: 12px;
  color: #b0b4bb;
  margin-top: 2px;
}
.team-more {
  cursor: pointer;
  color: #909399;
  flex-shrink: 0;
}
.team-more:hover {
  color: #409eff;
}
</style>
