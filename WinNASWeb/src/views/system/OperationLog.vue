<template>
  <div class="page-container">
    <div class="page-header">
      <h2>操作日志</h2>
    </div>

    <el-form :inline="true" style="margin-bottom: 15px">
      <el-form-item label="操作类型">
        <el-select v-model="filterAction" clearable placeholder="全部" style="width: 150px">
          <el-option label="登录" value="login" />
          <el-option label="上传" value="upload" />
          <el-option label="下载" value="download" />
          <el-option label="删除" value="delete" />
          <el-option label="创建分享" value="create_share" />
        </el-select>
      </el-form-item>
      <el-form-item>
        <el-button type="primary" @click="loadLogs">查询</el-button>
      </el-form-item>
    </el-form>

    <el-table :data="logs" v-loading="loading">
      <el-table-column prop="id" label="ID" width="80" />
      <el-table-column prop="username" label="用户" width="120" />
      <el-table-column prop="action" label="操作" width="120" />
      <el-table-column prop="target" label="目标" min-width="200" show-overflow-tooltip />
      <el-table-column prop="detail" label="详情" min-width="200" show-overflow-tooltip />
      <el-table-column prop="ipAddress" label="IP地址" width="140" />
      <el-table-column prop="createdAt" label="时间" width="180" />
    </el-table>

    <el-pagination
      v-model:current-page="page"
      :page-size="pageSize"
      :total="total"
      layout="prev, pager, next"
      @current-change="loadLogs"
      style="margin-top: 15px; justify-content: center"
    />
  </div>
</template>

<script setup>
import { ref, onMounted } from 'vue'
import { getLogs } from '../../api/system'

const logs = ref([])
const loading = ref(false)
const page = ref(1)
const pageSize = ref(20)
const total = ref(0)
const filterAction = ref('')

onMounted(() => { loadLogs() })

async function loadLogs() {
  loading.value = true
  try {
    const res = await getLogs({ page: page.value, pageSize: pageSize.value, action: filterAction.value || undefined })
    if (res.success) {
      logs.value = res.data
    }
  } finally {
    loading.value = false
  }
}
</script>
