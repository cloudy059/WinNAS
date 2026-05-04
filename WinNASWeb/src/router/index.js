import { createRouter, createWebHashHistory } from 'vue-router'

const routes = [
  {
    path: '/login',
    name: 'Login',
    component: () => import('../views/Login.vue'),
    meta: { requiresAuth: false }
  },
  {
    path: '/share/:code',
    name: 'ShareAccess',
    component: () => import('../views/share/ShareAccess.vue'),
    meta: { requiresAuth: false }
  },
  {
    path: '/guest',
    name: 'Guest',
    component: () => import('../views/file/GuestBrowser.vue'),
    meta: { requiresAuth: false }
  },
  {
    path: '/',
    component: () => import('../views/Layout.vue'),
    meta: { requiresAuth: true },
    children: [
      { path: '', name: 'Home', component: () => import('../views/Home.vue'), meta: { title: '首页' } },
      { path: 'files', name: 'Files', component: () => import('../views/file/FileManager.vue'), meta: { title: '文件管理' } },
      { path: 'shares', name: 'Shares', component: () => import('../views/share/ShareList.vue'), meta: { title: '分享管理' } },
      { path: 'teams', name: 'Teams', component: () => import('../views/team/TeamList.vue'), meta: { title: '团队协作' } },
      { path: 'teams/:id', name: 'TeamDetail', component: () => import('../views/team/TeamDetail.vue'), meta: { title: '团队详情' } },
      { path: 'chat', name: 'Chat', component: () => import('../views/team/Chat.vue'), meta: { title: '在线聊天' } },
      { path: 'network', name: 'Network', component: () => import('../views/network/NetworkShare.vue'), meta: { title: '网络共享' } },
      { path: 'neighborhood', name: 'Neighborhood', component: () => import('../views/network/NetworkNeighborhood.vue'), meta: { title: '网上邻居' } },
      { path: 'recycle', name: 'RecycleBin', component: () => import('../views/recycle/RecycleBin.vue'), meta: { title: '回收站' } },
      { path: 'system', name: 'System', component: () => import('../views/system/SystemInfo.vue'), meta: { title: '系统信息' } },
      { path: 'users', name: 'Users', component: () => import('../views/system/UserManage.vue'), meta: { title: '用户管理' } },
      { path: 'logs', name: 'Logs', component: () => import('../views/system/OperationLog.vue'), meta: { title: '操作日志' } },
      { path: 'announcements', name: 'Announcements', component: () => import('../views/system/Announcement.vue'), meta: { title: '公告管理' } },
      { path: 'settings', name: 'Settings', component: () => import('../views/system/Settings.vue'), meta: { title: '系统设置' } }
    ]
  }
]

const router = createRouter({
  history: createWebHashHistory(),
  routes
})

router.beforeEach((to, from, next) => {
  const token = localStorage.getItem('winnas_token')
  if (to.meta.requiresAuth && !token) {
    next('/login')
  } else if (to.path === '/login' && token) {
    next('/')
  } else {
    next()
  }
})

export default router
