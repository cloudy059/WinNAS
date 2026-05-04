<template>
  <el-container class="layout-container">
    <el-aside v-if="!isMobile" :width="isCollapse ? '64px' : '220px'" class="layout-aside">
      <div class="logo-container">
        <img src="/WinNAS.ico" style="width:28px;height:28px;flex-shrink:0" />
        <h1 v-show="!isCollapse">WinNAS</h1>
        <h1 v-show="isCollapse">W</h1>
      </div>
      <el-menu
        :default-active="currentRoute"
        :collapse="isCollapse"
        router
        :background-color="sidebarBg"
        :text-color="sidebarText"
        :active-text-color="sidebarActive"
      >
        <el-menu-item index="/">
          <el-icon><HomeFilled /></el-icon>
          <template #title>首页</template>
        </el-menu-item>
        <el-menu-item index="/files">
          <el-icon><Folder /></el-icon>
          <template #title>文件管理</template>
        </el-menu-item>
        <el-menu-item index="/shares">
          <el-icon><Share /></el-icon>
          <template #title>分享管理</template>
        </el-menu-item>
        <el-menu-item index="/teams">
          <el-icon><UserFilled /></el-icon>
          <template #title>团队协作</template>
        </el-menu-item>
        <el-menu-item index="/chat">
          <el-icon><ChatDotRound /></el-icon>
          <template #title>在线聊天</template>
        </el-menu-item>
        <el-menu-item v-if="userStore.isAdmin" index="/network">
          <el-icon><Link /></el-icon>
          <template #title>网络共享</template>
        </el-menu-item>
        <el-menu-item index="/neighborhood">
          <el-icon><Connection /></el-icon>
          <template #title>网上邻居</template>
        </el-menu-item>
        <el-menu-item index="/recycle">
          <el-icon><Delete /></el-icon>
          <template #title>回收站</template>
        </el-menu-item>
        <el-sub-menu v-if="userStore.isAdmin" index="system">
          <template #title>
            <el-icon><component :is="OperationIcon" /></el-icon>
            <span>系统管理</span>
          </template>
          <el-menu-item index="/system"><el-icon><Monitor /></el-icon>系统信息</el-menu-item>
          <el-menu-item index="/users"><el-icon><User /></el-icon>用户管理</el-menu-item>
          <el-menu-item index="/logs"><el-icon><Document /></el-icon>操作日志</el-menu-item>
          <el-menu-item index="/announcements"><el-icon><Bell /></el-icon>公告管理</el-menu-item>
          <el-menu-item index="/settings"><el-icon><Tools /></el-icon>系统设置</el-menu-item>
        </el-sub-menu>
      </el-menu>
    </el-aside>

    <el-drawer v-if="isMobile" v-model="drawerVisible" direction="ltr" size="220px" :show-close="false" :with-header="false" class="mobile-drawer">
      <div class="logo-container">
        <img src="/WinNAS.ico" style="width:28px;height:28px;flex-shrink:0" />
        <h1>WinNAS</h1>
      </div>
      <el-menu
        :default-active="currentRoute"
        router
        :background-color="sidebarBg"
        :text-color="sidebarText"
        :active-text-color="sidebarActive"
        @select="handleMobileMenuSelect"
      >
        <el-menu-item index="/">
          <el-icon><HomeFilled /></el-icon>
          <template #title>首页</template>
        </el-menu-item>
        <el-menu-item index="/files">
          <el-icon><Folder /></el-icon>
          <template #title>文件管理</template>
        </el-menu-item>
        <el-menu-item index="/shares">
          <el-icon><Share /></el-icon>
          <template #title>分享管理</template>
        </el-menu-item>
        <el-menu-item index="/teams">
          <el-icon><UserFilled /></el-icon>
          <template #title>团队协作</template>
        </el-menu-item>
        <el-menu-item index="/chat">
          <el-icon><ChatDotRound /></el-icon>
          <template #title>在线聊天</template>
        </el-menu-item>
        <el-menu-item v-if="userStore.isAdmin" index="/network">
          <el-icon><Link /></el-icon>
          <template #title>网络共享</template>
        </el-menu-item>
        <el-menu-item index="/neighborhood">
          <el-icon><Connection /></el-icon>
          <template #title>网上邻居</template>
        </el-menu-item>
        <el-menu-item index="/recycle">
          <el-icon><Delete /></el-icon>
          <template #title>回收站</template>
        </el-menu-item>
        <el-sub-menu v-if="userStore.isAdmin" index="system">
          <template #title>
            <el-icon><component :is="OperationIcon" /></el-icon>
            <span>系统管理</span>
          </template>
          <el-menu-item index="/system"><el-icon><Monitor /></el-icon>系统信息</el-menu-item>
          <el-menu-item index="/users"><el-icon><User /></el-icon>用户管理</el-menu-item>
          <el-menu-item index="/logs"><el-icon><Document /></el-icon>操作日志</el-menu-item>
          <el-menu-item index="/announcements"><el-icon><Bell /></el-icon>公告管理</el-menu-item>
          <el-menu-item index="/settings"><el-icon><Tools /></el-icon>系统设置</el-menu-item>
        </el-sub-menu>
      </el-menu>
    </el-drawer>

    <el-container>
      <el-header class="layout-header">
        <div class="header-left">
          <el-icon v-if="isMobile" class="collapse-btn" @click="drawerVisible = true">
            <Expand />
          </el-icon>
          <el-icon v-else class="collapse-btn" @click="isCollapse = !isCollapse">
            <Fold v-if="!isCollapse" />
            <Expand v-else />
          </el-icon>
          <el-breadcrumb separator="/">
            <el-breadcrumb-item>{{ currentTitle }}</el-breadcrumb-item>
          </el-breadcrumb>
        </div>
        <div v-if="pinnedAnnouncements.length > 0" class="header-announcement">
          <el-icon :size="14" color="#e6a23c"><Bell /></el-icon>
          <div class="header-announcement-scroll">
            <div class="header-announcement-inner" :class="{ 'scroll-animate': pinnedAnnouncements.length > 1 }">
              <span v-for="(item, idx) in pinnedAnnouncements" :key="item.id" class="header-announcement-item">
                <el-tag type="danger" size="small" style="margin-right:2px">置顶</el-tag>
                {{ item.title }}：{{ item.content }}
                <span v-if="idx < pinnedAnnouncements.length - 1" class="header-announcement-sep">|</span>
              </span>
            </div>
          </div>
        </div>
        <div class="header-right">
          <el-popover placement="bottom-end" :width="280" trigger="click">
            <template #reference>
              <el-icon class="header-icon-btn" title="侧边栏颜色">
                <Brush />
              </el-icon>
            </template>
            <div class="sidebar-color-picker">
              <p style="margin:0 0 10px;font-size:13px;color:#606266;font-weight:500">侧边栏配色</p>
              <div class="color-options">
                <div
                  v-for="(color, index) in themeStore.sidebarColors"
                  :key="index"
                  class="color-dot"
                  :class="{ active: themeStore.sidebarColorIndex === index }"
                  :style="{ background: color.bg }"
                  :title="color.name"
                  @click="themeStore.setSidebarColor(index)"
                >
                  <el-icon v-if="themeStore.sidebarColorIndex === index" :size="12" color="#fff"><Check /></el-icon>
                </div>
              </div>
              <p style="margin:8px 0 0;font-size:11px;color:#909399">{{ themeStore.sidebarColors[themeStore.sidebarColorIndex]?.name }}</p>
            </div>
          </el-popover>
          <el-tooltip :content="themeStore.isDark ? '切换亮色模式' : '切换暗黑模式'" placement="bottom">
            <el-icon class="header-icon-btn" @click="themeStore.toggleTheme()">
              <Sunny v-if="themeStore.isDark" />
              <Moon v-else />
            </el-icon>
          </el-tooltip>
          <el-tooltip content="通知" placement="bottom">
            <el-badge :value="unreadCount" :hidden="unreadCount === 0" :max="99">
              <el-icon class="header-icon-btn" @click="notificationVisible = true">
                <Bell />
              </el-icon>
            </el-badge>
          </el-tooltip>
          <el-tooltip content="关于 WinNAS" placement="bottom">
            <el-icon class="header-icon-btn" @click="aboutVisible = true">
              <InfoFilled />
            </el-icon>
          </el-tooltip>
          <el-dropdown trigger="click" @command="handleCommand">
            <span class="user-info">
              <el-avatar :size="28" icon="UserFilled" />
              <span class="username">{{ userStore.username }}</span>
              <el-icon><ArrowDown /></el-icon>
            </span>
            <template #dropdown>
              <el-dropdown-menu>
                <el-dropdown-item command="profile">个人信息</el-dropdown-item>
                <el-dropdown-item command="logout" divided>退出登录</el-dropdown-item>
              </el-dropdown-menu>
            </template>
          </el-dropdown>
        </div>
      </el-header>

      <el-main class="layout-main">
        <router-view />
      </el-main>
    </el-container>

    <el-dialog v-model="profileVisible" title="个人信息" width="460px">
      <el-form :model="profileForm" label-width="80px">
        <el-form-item label="用户名">
          <el-input :model-value="userStore.username" disabled />
        </el-form-item>
        <el-form-item label="昵称">
          <el-input v-model="profileForm.nickname" placeholder="请输入昵称" />
        </el-form-item>
        <el-form-item label="角色">
          <el-input :model-value="userStore.isAdmin ? '管理员' : '普通用户'" disabled />
        </el-form-item>
        <el-form-item label="修改密码">
          <el-input v-model="profileForm.newPassword" type="password" show-password placeholder="留空则不修改" />
        </el-form-item>
        <el-form-item label="确认密码">
          <el-input v-model="profileForm.confirmPassword" type="password" show-password placeholder="再次输入新密码" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="profileVisible = false">取消</el-button>
        <el-button type="primary" @click="handleSaveProfile" :loading="savingProfile">保存</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="aboutVisible" title="关于 WinNAS" width="640px" top="5vh">
      <div class="about-content">
        <div class="about-logo">
          <img src="/WinNAS.ico" style="width:48px;height:48px" />
          <h2>WinNAS</h2>
        </div>
        <p class="about-desc">轻量级局域网文件共享与协作系统</p>

        <el-divider />

        <h3>操作说明</h3>
        <div class="about-section">
          <h4>📁 文件管理</h4>
          <ul>
            <li>点击「添加共享目录」将本地文件夹添加到系统中</li>
            <li>选择左侧目录后，右侧显示目录内的文件和子文件夹</li>
            <li>点击「上传文件」选择文件上传，点击「上传文件夹」选择整个文件夹上传</li>
            <li>点击「新建文件夹」在当前目录下创建子文件夹</li>
            <li>双击文件夹进入子目录，双击图片/文本文件直接预览，双击音视频文件直接播放</li>
            <li>右键文件可弹出快捷菜单，支持预览、播放、下载、分享、移动、重命名、删除</li>
            <li>点击目录右侧的 ⋯ 菜单可编辑目录属性、分享整个目录或删除目录</li>
            <li>支持按图片/视频/文档/音频等类型筛选当前目录文件</li>
            <li>图片目录可切换画廊模式浏览，支持幻灯片播放</li>
          </ul>

          <h4>🔗 分享管理</h4>
          <ul>
            <li>在文件管理中对文件或目录点击「分享」，可创建分享链接</li>
            <li>支持设置访问密码、过期时间和最大下载次数</li>
            <li>创建成功后链接自动复制到剪贴板，可发送给局域网内其他用户</li>
            <li>分享链接支持生成二维码，手机扫码直接访问</li>
            <li>在分享管理页面可查看所有分享，支持禁用和重新启用</li>
            <li>文件夹分享支持「下载全部」打包为 ZIP 下载</li>
          </ul>

          <h4>👥 团队协作</h4>
          <ul>
            <li>点击「创建团队」新建协作空间，可选择已有用户作为成员</li>
            <li>团队卡片右侧 ⋯ 菜单支持编辑团队属性和删除团队（仅创建者）</li>
            <li>进入团队后切换标签页：团队文件、团队成员、团队信息</li>
            <li>团队文件支持上传、新建文件夹、预览、播放、下载和删除</li>
            <li>团队成员页面可添加新成员或移除已有成员</li>
          </ul>

          <h4>💬 在线聊天</h4>
          <ul>
            <li>左侧联系人分为「私聊」和「群聊」两个区域</li>
            <li>私聊：选择用户发起一对一聊天</li>
            <li>群聊：选择团队进入团队群聊</li>
            <li>自己发送的消息显示在右侧（蓝色气泡），对方消息显示在左侧</li>
          </ul>

          <h4>⚙️ 系统管理（仅管理员）</h4>
          <ul>
            <li>系统信息：查看 CPU、内存、磁盘等运行状态</li>
            <li>用户管理：查看、启用/禁用用户，修改用户角色和存储配额</li>
            <li>操作日志：查看系统操作记录</li>
            <li>公告管理：发布、编辑和删除系统公告，支持置顶</li>
            <li>系统设置：修改端口、上传限制等配置参数</li>
          </ul>

          <h4>☁️ 云盘挂载（仅管理员）</h4>
          <ul>
            <li>集成 OpenList 网盘管理，支持挂载阿里云盘、百度网盘、OneDrive 等 30+ 种存储</li>
            <li>需要将 openlist.exe 放置到程序目录下的 openlist 文件夹中</li>
            <li>启动 OpenList 后可在页面内直接管理，也可打开独立管理面板</li>
            <li>支持获取管理员密码，首次使用建议修改默认密码</li>
          </ul>

          <h4>🌐 网上邻居</h4>
          <ul>
            <li>自动发现局域网内运行 WinNAS 的其他设备</li>
            <li>以卡片形式展示发现的设备，显示设备名称和地址</li>
            <li>点击设备卡片查看其公开目录</li>
            <li>进入公开目录后可浏览文件、预览图片/文本、播放音视频、下载文件</li>
            <li>双击文件夹进入子目录，双击文件预览或播放</li>
          </ul>

          <h4>🌐 网络共享（仅管理员）</h4>
          <ul>
            <li>WebDAV：在文件资源管理器地址栏输入 WebDAV 地址，使用 WinNAS 账号密码登录</li>
            <li>SMB：右键"此电脑"→"映射网络驱动器"，输入共享地址连接</li>
            <li>支持全局开关控制 SMB 和 WebDAV 服务的启用与禁用</li>
            <li>可对每个共享目录单独设置是否允许 SMB 或 WebDAV 共享</li>
            <li>SMB 共享需要以管理员身份运行 WinNAS</li>
          </ul>

          <h4>🎵 媒体播放</h4>
          <ul>
            <li>支持 MP4、WebM 等视频格式和 MP3、WAV、FLAC 等音频格式</li>
            <li>点击「播放」按钮后，页面底部出现浮动播放器</li>
            <li>播放器支持播放列表、上一曲/下一曲、进度拖动、音量调节</li>
            <li>播放器可最小化为小窗，不影响其他操作</li>
          </ul>

          <h4>🖼️ 文件预览</h4>
          <ul>
            <li>图片：支持 JPG、PNG、GIF、BMP、WebP、SVG 格式，全屏预览</li>
            <li>Markdown：预览 .md 文件时自动渲染为格式化内容</li>
            <li>文本：支持 TXT、LOG、JSON、XML、CSV、YAML、HTML、CSS、JS、PY、JAVA、C、CS 等 26 种格式，代码风格展示</li>
          </ul>

          <h4>🌙 暗黑模式</h4>
          <ul>
            <li>点击顶部栏右侧月亮/太阳图标可切换暗黑/亮色主题</li>
            <li>主题设置会自动保存，下次打开时恢复上次选择</li>
            <li>夜间使用暗黑模式更护眼</li>
          </ul>

          <h4>🎨 侧边栏配色</h4>
          <ul>
            <li>点击顶部栏画笔图标可选择侧边栏配色方案</li>
            <li>提供午夜、石墨、紫罗兰、玫瑰金、日落、翡翠、深空、靛蓝八种渐变配色</li>
            <li>配色设置自动保存，下次打开时恢复</li>
          </ul>
        </div>

        <el-divider />

        <div class="about-footer">
          <p>开发者：<strong>数白云</strong></p>
          <p>版本：{{ MAJOR }}.{{ MINOR }}.{{ VERSION }}</p>
        </div>
      </div>
    </el-dialog>

    <el-dialog v-model="notificationVisible" title="通知中心" width="500px">
      <div v-if="notifications.length === 0" style="text-align:center;padding:20px;color:var(--el-text-color-secondary)">暂无通知</div>
      <div v-else>
        <div style="text-align:right;margin-bottom:12px">
          <el-button size="small" @click="readAllNotifications">全部已读</el-button>
          <el-button size="small" type="danger" @click="clearAllNotifications">清空</el-button>
        </div>
        <div v-for="n in notifications" :key="n.id" style="padding:10px;border-bottom:1px solid var(--el-border-color-lighter);display:flex;gap:8px;align-items:flex-start" :style="{ opacity: n.isRead ? 0.6 : 1 }">
          <el-tag :type="n.type === 'download' ? 'success' : n.type === 'share' ? 'warning' : 'info'" size="small">{{ n.type }}</el-tag>
          <div style="flex:1">
            <div style="font-size:13px;font-weight:500">{{ n.title }}</div>
            <div v-if="n.content" style="font-size:12px;color:var(--el-text-color-secondary);margin-top:2px">{{ n.content }}</div>
            <div style="font-size:11px;color:var(--el-text-color-placeholder);margin-top:4px">{{ new Date(n.createdAt).toLocaleString('zh-CN') }}</div>
          </div>
          <el-button v-if="!n.isRead" link size="small" @click="readNotification(n.id)">已读</el-button>
        </div>
      </div>
    </el-dialog>
  </el-container>
</template>

<script setup>
import { ref, reactive, computed, markRaw, onMounted, onUnmounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useUserStore } from '../store/user'
import { useThemeStore } from '../store/theme'
import { updateProfile } from '../api/auth'
import { VERSION, MAJOR, MINOR } from '../version'
import { ElMessage } from 'element-plus'
import {
  HomeFilled, Folder, Share, UserFilled, ChatDotRound,
  Setting, Operation, Monitor, User, Document, Bell, Tools, InfoFilled,
  Link, Fold, Expand, ArrowDown, Sunny, Moon, Brush, Check, Connection, Delete
} from '@element-plus/icons-vue'
import { getNotifications, readNotification as readNotificationApi, clearNotifications } from '../api/enhanced'
import { getAnnouncements } from '../api/system'

const OperationIcon = markRaw(Operation)

const route = useRoute()
const router = useRouter()
const userStore = useUserStore()
const themeStore = useThemeStore()
const isCollapse = ref(false)
const profileVisible = ref(false)
const aboutVisible = ref(false)
const notificationVisible = ref(false)
const notifications = ref([])
const unreadCount = ref(0)
const savingProfile = ref(false)
const profileForm = reactive({ nickname: '', newPassword: '', confirmPassword: '' })
const isMobile = ref(false)
const drawerVisible = ref(false)
const announcements = ref([])

const pinnedAnnouncements = computed(() => announcements.value.filter(a => a.isPinned))

const sidebarBg = computed(() => themeStore.sidebarColors[themeStore.sidebarColorIndex]?.menuBg || '#1c2541')
const sidebarText = computed(() => themeStore.sidebarColors[themeStore.sidebarColorIndex]?.text || '#c5d0e0')
const sidebarActive = computed(() => themeStore.sidebarColors[themeStore.sidebarColorIndex]?.active || '#5fa8d3')

function checkMobile() {
  isMobile.value = window.innerWidth < 768
}

onMounted(() => {
  checkMobile()
  window.addEventListener('resize', checkMobile)
  loadNotifications()
  setInterval(loadNotifications, 30000)
  loadAnnouncements()
  setInterval(loadAnnouncements, 60000)
})

onUnmounted(() => {
  window.removeEventListener('resize', checkMobile)
})

async function loadNotifications() {
  try {
    const res = await getNotifications()
    if (res.success) {
      notifications.value = res.data || []
      unreadCount.value = res.unread || 0
    }
  } catch {}
}

async function readNotification(id) {
  try {
    await readNotificationApi(id)
    loadNotifications()
  } catch {}
}

async function readAllNotifications() {
  try {
    await readNotificationApi(0)
    loadNotifications()
  } catch {}
}

async function clearAllNotifications() {
  try {
    await clearNotifications()
    loadNotifications()
  } catch {}
}

async function loadAnnouncements() {
  try {
    const res = await getAnnouncements()
    if (res.success) {
      announcements.value = (res.data || []).sort((a, b) => {
        if (a.isPinned && !b.isPinned) return -1
        if (!a.isPinned && b.isPinned) return 1
        return new Date(b.createdAt) - new Date(a.createdAt)
      })
    }
  } catch {}
}

function handleMobileMenuSelect() {
  drawerVisible.value = false
}

const currentRoute = computed(() => route.path)
const currentTitle = computed(() => route.meta?.title || route.name || '')

async function handleCommand(command) {
  if (command === 'logout') {
    await userStore.logout()
    ElMessage.success('已退出登录')
    router.push('/login')
  } else if (command === 'profile') {
    profileForm.nickname = userStore.userInfo?.nickname || ''
    profileForm.newPassword = ''
    profileForm.confirmPassword = ''
    profileVisible.value = true
  }
}

async function handleSaveProfile() {
  if (profileForm.newPassword && profileForm.newPassword !== profileForm.confirmPassword) {
    ElMessage.warning('两次密码输入不一致')
    return
  }
  savingProfile.value = true
  try {
    const data = { nickname: profileForm.nickname }
    if (profileForm.newPassword) data.password = profileForm.newPassword
    const res = await updateProfile(data)
    if (res.success) {
      ElMessage.success('保存成功')
      profileVisible.value = false
      if (res.data) userStore.userInfo = { ...userStore.userInfo, ...res.data }
    } else {
      ElMessage.error(res.message || '保存失败')
    }
  } finally {
    savingProfile.value = false
  }
}
</script>

<style scoped>
.layout-container {
  height: 100vh;
}

.layout-aside {
  background: var(--sidebar-bg-gradient, linear-gradient(180deg, #1c2541 0%, #0b132b 100%));
  transition: width 0.3s;
  overflow: hidden;
  display: flex;
  flex-direction: column;
}

.logo-container {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
  height: 60px;
  border-bottom: 1px solid rgba(255, 255, 255, 0.1);
}

.logo-container h1 {
  font-size: 22px;
  font-weight: 600;
  white-space: nowrap;
  color: #fff;
}

.layout-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  border-bottom: 1px solid #e6e6e6;
  background: #fff;
  padding: 0 20px;
  gap: 16px;
}

.header-left {
  display: flex;
  align-items: center;
  gap: 15px;
  flex-shrink: 0;
}

.collapse-btn {
  font-size: 20px;
  cursor: pointer;
  color: #606266;
}

.collapse-btn:hover {
  color: #409eff;
}

.header-right {
  display: flex;
  align-items: center;
  gap: 12px;
  flex-shrink: 0;
}

.header-announcement {
  display: flex;
  align-items: center;
  gap: 6px;
  max-width: 1200px;
  height: 32px;
  padding: 0 14px;
  background: linear-gradient(135deg, #fdf6ec, #fef9f0);
  border: 1px solid #faecd8;
  border-radius: 16px;
  overflow: hidden;
  flex: 1;
  min-width: 0;
  justify-content: center;
}

.header-announcement-scroll {
  flex: 1;
  overflow: hidden;
  height: 24px;
  min-width: 0;
}

.header-announcement-inner {
  display: flex;
  align-items: center;
  white-space: nowrap;
  font-size: 14px;
  color: #e6a23c;
  gap: 8px;
}

.header-announcement-inner.scroll-animate {
  animation: headerAnnouncementScroll 20s linear infinite;
}

@keyframes headerAnnouncementScroll {
  0% { transform: translateX(100%); }
  100% { transform: translateX(-100%); }
}

.header-announcement-item {
  display: inline-flex;
  align-items: center;
  gap: 2px;
}

.header-announcement-sep {
  margin: 0 6px;
  color: #dcdfe6;
}

.header-icon-btn {
  font-size: 20px;
  cursor: pointer;
  color: #606266;
  transition: color 0.3s;
}

.header-icon-btn:hover {
  color: #409eff;
}

.user-info {
  display: flex;
  align-items: center;
  gap: 8px;
  cursor: pointer;
  color: #606266;
}

.username {
  font-size: 14px;
}

.layout-main {
  padding: 20px;
  background: #f0f2f5;
  overflow-y: auto;
}

.sidebar-color-picker {
  padding: 4px 0;
}

.color-options {
  display: flex;
  gap: 8px;
  flex-wrap: wrap;
}

.color-dot {
  width: 40px;
  height: 56px;
  border-radius: 6px;
  cursor: pointer;
  border: 2px solid transparent;
  display: flex;
  align-items: center;
  justify-content: center;
  transition: border-color 0.2s, transform 0.2s;
  box-shadow: 0 2px 6px rgba(0, 0, 0, 0.2);
}

.color-dot:hover {
  transform: scale(1.08);
}

.color-dot.active {
  border-color: #409eff;
  box-shadow: 0 2px 8px rgba(64, 158, 255, 0.4);
}

.about-content {
  text-align: center;
}

.about-logo {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 12px;
  margin-bottom: 8px;
}

.about-logo h2 {
  font-size: 28px;
  color: #303133;
  margin: 0;
}

.about-desc {
  color: #909399;
  font-size: 14px;
  margin: 0;
}

.about-section {
  text-align: left;
  max-height: calc(100vh - 320px);
  overflow-y: auto;
}

.about-section h4 {
  font-size: 14px;
  color: #303133;
  margin: 16px 0 8px;
}

.about-section h4:first-child {
  margin-top: 0;
}

.about-section ul {
  margin: 0 0 4px;
  padding-left: 20px;
}

.about-section li {
  font-size: 13px;
  line-height: 2;
  color: #606266;
}

.about-footer {
  text-align: center;
}

.about-footer p {
  font-size: 13px;
  color: #909399;
  margin: 4px 0;
}

.el-menu {
  border-right: none;
}

.layout-aside :deep(.el-sub-menu__title) {
  padding-left: 20px !important;
}

.layout-aside :deep(.el-sub-menu .el-menu-item) {
  padding-left: 50px !important;
}

.mobile-drawer :deep(.el-drawer__body) {
  padding: 0;
  background: var(--sidebar-bg-gradient, linear-gradient(180deg, #1c2541 0%, #0b132b 100%));
}

.mobile-drawer .logo-container {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
  height: 60px;
  border-bottom: 1px solid rgba(255, 255, 255, 0.1);
}

.mobile-drawer .logo-container h1 {
  font-size: 22px;
  font-weight: 600;
  white-space: nowrap;
  color: #fff;
}

.mobile-drawer :deep(.el-menu) {
  border-right: none;
}

@media (max-width: 768px) {
  .layout-header {
    padding: 0 12px !important;
  }

  .username {
    display: none;
  }

  .layout-main {
    padding: 12px !important;
  }

  .header-announcement {
    display: none;
  }
}
</style>
