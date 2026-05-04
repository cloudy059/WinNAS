<template>
  <div class="page-container">
    <div class="page-header">
      <h2>在线聊天</h2>
    </div>

    <el-row :gutter="20" style="height: calc(100vh - 180px)">
      <el-col :xs="24" :sm="6">
        <el-card shadow="hover" class="chat-contacts" style="height: 100%">
          <template #header><span>联系人</span></template>
          <div class="contact-section">
            <div class="contact-section-title"><el-icon><UserFilled /></el-icon> 私聊</div>
            <div v-if="users.length === 0" class="empty-text" style="padding:10px">暂无用户</div>
            <div
              v-for="u in users"
              :key="'user-' + u.id"
              class="contact-item"
              :class="{ active: chatType === 'private' && currentId === u.id }"
              @click="selectChat('private', u.id, u.nickname || u.username)"
            >
              <el-avatar :size="28" icon="UserFilled" />
              <span>{{ u.nickname || u.username }}</span>
            </div>
          </div>
          <div class="contact-section" style="margin-top:12px">
            <div class="contact-section-title"><el-icon><ChatDotRound /></el-icon> 群聊</div>
            <div v-if="teams.length === 0" class="empty-text" style="padding:10px">暂无团队</div>
            <div
              v-for="t in teams"
              :key="'team-' + t.id"
              class="contact-item"
              :class="{ active: chatType === 'team' && currentId === t.id }"
              @click="selectChat('team', t.id, t.name)"
            >
              <el-avatar :size="28" icon="UserFilled" style="background:#e6a23c" />
              <span>{{ t.name }}</span>
              <el-tag size="small" type="info">群聊</el-tag>
            </div>
          </div>
        </el-card>
      </el-col>

      <el-col :xs="24" :sm="18">
        <el-card shadow="hover" class="chat-main" style="height: 100%">
          <template #header>
            <div class="chat-header">
              <span>{{ chatName || '请选择联系人' }}</span>
              <el-tag v-if="chatType === 'team'" size="small" type="info">群聊</el-tag>
              <el-tag v-else-if="chatType === 'private'" size="small" type="success">私聊</el-tag>
            </div>
          </template>
          <div class="chat-messages" ref="messagesRef">
            <div v-for="msg in messages" :key="msg.id" class="message-item" :class="{ 'message-self': msg.senderId === myId }">
              <div class="message-header">
                <span class="message-sender">{{ getSenderName(msg) }}</span>
                <span class="message-time">{{ formatTime(msg.createdAt) }}</span>
              </div>
              <div class="message-content">{{ msg.content }}</div>
            </div>
            <div v-if="messages.length === 0" class="empty-text">暂无消息</div>
          </div>
          <div class="chat-input">
            <el-input
              v-model="inputMessage"
              placeholder="输入消息..."
              @keyup.enter="sendMessage"
              :disabled="!currentId"
            >
              <template #append>
                <el-button @click="sendMessage" :disabled="!currentId">发送</el-button>
              </template>
            </el-input>
          </div>
        </el-card>
      </el-col>
    </el-row>
  </div>
</template>

<script setup>
import { ref, onMounted, nextTick } from 'vue'
import { getTeams, getChatHistory, sendMessage as sendMsg } from '../../api/team'
import { getUserList } from '../../api/auth'
import { useUserStore } from '../../store/user'

const userStore = useUserStore()
const myId = userStore.userInfo?.id || 0

const teams = ref([])
const users = ref([])
const messages = ref([])
const chatType = ref('')
const currentId = ref(null)
const chatName = ref('')
const inputMessage = ref('')
const messagesRef = ref(null)

onMounted(async () => {
  const [teamRes, userRes] = await Promise.all([getTeams(), getUserList()])
  if (teamRes.success) teams.value = teamRes.data
  if (userRes.success) users.value = userRes.data.filter(u => u.id !== myId)
})

async function selectChat(type, id, name) {
  chatType.value = type
  currentId.value = id
  chatName.value = name
  await loadMessages()
}

async function loadMessages() {
  const params = { limit: 50 }
  if (chatType.value === 'team') params.teamId = currentId.value
  else params.receiverId = currentId.value

  const res = await getChatHistory(params)
  if (res.success) {
    messages.value = res.data.reverse()
    await nextTick()
    scrollToBottom()
  }
}

async function sendMessage() {
  if (!inputMessage.value.trim() || !currentId.value) return

  const data = { content: inputMessage.value, messageType: 'text' }
  if (chatType.value === 'team') data.teamId = currentId.value
  else data.receiverId = currentId.value

  const res = await sendMsg(data)
  if (res.success) {
    inputMessage.value = ''
    loadMessages()
  }
}

function getSenderName(msg) {
  if (msg.senderName) return msg.senderName
  if (msg.senderId === myId) return userStore.userInfo?.nickname || userStore.username || '我'
  const user = users.value.find(u => u.id === msg.senderId)
  return user ? (user.nickname || user.username) : `用户${msg.senderId}`
}

function scrollToBottom() {
  if (messagesRef.value) {
    messagesRef.value.scrollTop = messagesRef.value.scrollHeight
  }
}

function formatTime(dateStr) {
  if (!dateStr) return ''
  return new Date(dateStr).toLocaleTimeString('zh-CN')
}
</script>

<style scoped>
.chat-contacts :deep(.el-card__body) {
  padding: 0;
  overflow-y: auto;
  height: calc(100% - 50px);
}

.contact-section {
  padding: 0;
}

.contact-section-title {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 10px 16px 6px;
  font-size: 12px;
  color: #909399;
  font-weight: 600;
}

.contact-item {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 10px 16px;
  cursor: pointer;
  transition: background 0.3s;
}

.contact-item:hover {
  background: #f5f7fa;
}

.contact-item.active {
  background: #ecf5ff;
}

.chat-main :deep(.el-card__body) {
  display: flex;
  flex-direction: column;
  height: calc(100% - 50px);
  padding: 0;
}

.chat-header {
  display: flex;
  align-items: center;
  gap: 8px;
}

.chat-messages {
  flex: 1;
  overflow-y: auto;
  padding: 15px;
}

.message-item {
  margin-bottom: 15px;
}

.message-item.message-self {
  text-align: right;
}

.message-item.message-self .message-content {
  background: #409eff;
  color: #fff;
}

.message-item.message-self .message-header {
  justify-content: flex-end;
}

.message-header {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 4px;
}

.message-sender {
  font-size: 12px;
  color: #409eff;
}

.message-time {
  font-size: 11px;
  color: #c0c4cc;
}

.message-content {
  padding: 8px 12px;
  background: #f5f7fa;
  border-radius: 8px;
  display: inline-block;
  max-width: 70%;
  word-break: break-word;
  text-align: left;
}

.chat-input {
  padding: 15px;
  border-top: 1px solid #e6e6e6;
}

.empty-text {
  text-align: center;
  color: #c0c4cc;
  padding: 40px;
}

@media (max-width: 768px) {
  .chat-contacts { height: auto !important; max-height: 200px; margin-bottom: 12px; }
  .chat-main { height: 500px !important; }
  .message-content { max-width: 85%; }
  .el-row { height: auto !important; }
}
</style>
