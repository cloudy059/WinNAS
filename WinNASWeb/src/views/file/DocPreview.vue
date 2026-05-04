<template>
  <el-dialog v-model="visible" :title="title" width="90%" fullscreen destroy-on-close @close="handleClose">
    <div class="doc-preview-container">
      <div v-if="loading" class="doc-preview-loading">
        <el-icon class="is-loading" :size="32"><Loading /></el-icon>
        <p>加载中...</p>
      </div>
      <div v-if="fileType === 'pptx'" ref="pptxWrapper" class="pptx-wrapper">
        <component :is="currentComponent" v-if="currentComponent" :src="fileSrc" @rendered="onPptxRendered" @error="onError" />
      </div>
      <component v-else :is="currentComponent" v-if="currentComponent" :src="fileSrc" :options="fileOptions" @rendered="onRendered" @error="onError" />
    </div>
  </el-dialog>
</template>

<script setup>
import { ref, computed, watch, shallowRef, onBeforeUnmount } from 'vue'
import { Loading } from '@element-plus/icons-vue'
import { ElMessage } from 'element-plus'

const props = defineProps({
  modelValue: { type: Boolean, default: false },
  title: { type: String, default: '' },
  src: { type: String, default: '' },
  fileName: { type: String, default: '' }
})

const emit = defineEmits(['update:modelValue'])

const visible = computed({
  get: () => props.modelValue,
  set: (val) => emit('update:modelValue', val)
})

const loading = ref(true)
const currentComponent = shallowRef(null)
const pptxWrapper = ref(null)
let resizeObserver = null

const fileType = computed(() => {
  const ext = props.fileName.split('.').pop()?.toLowerCase() || ''
  if (ext === 'pdf') return 'pdf'
  if (ext === 'doc' || ext === 'docx') return 'docx'
  if (ext === 'xls' || ext === 'xlsx') return 'excel'
  if (ext === 'ppt' || ext === 'pptx') return 'pptx'
  return ''
})

const fileSrc = computed(() => props.src)
const fileOptions = computed(() => {
  if (fileType.value === 'excel') return { xls: true }
  return {}
})

watch(() => props.modelValue, async (val) => {
  if (val && fileType.value) {
    loading.value = true
    currentComponent.value = null
    try {
      const modules = {
        pdf: () => import('@vue-office/pdf'),
        docx: () => import('@vue-office/docx'),
        excel: () => import('@vue-office/excel'),
        pptx: () => import('@vue-office/pptx')
      }
      const mod = await modules[fileType.value]()
      currentComponent.value = mod.default
    } catch {
      ElMessage.error('文档预览组件加载失败')
      loading.value = false
    }
  }
})

function onRendered() {
  loading.value = false
}

function onPptxRendered() {
  loading.value = false
  setTimeout(() => {
    adjustPptxScale()
    setupResizeObserver()
  }, 300)
}

function adjustPptxScale() {
  const wrapper = pptxWrapper.value
  if (!wrapper) return
  const containerWidth = wrapper.clientWidth
  if (containerWidth <= 0) return
  const root = wrapper.firstElementChild
  if (!root) return
  const rootWidth = root.scrollWidth || root.offsetWidth
  if (rootWidth <= 0 || rootWidth <= containerWidth) return
  const scale = containerWidth / rootWidth
  root.style.zoom = String(scale)
}

function setupResizeObserver() {
  if (resizeObserver) return
  const wrapper = pptxWrapper.value
  if (!wrapper) return
  resizeObserver = new ResizeObserver(() => {
    adjustPptxScale()
  })
  resizeObserver.observe(wrapper)
}

function onError() {
  loading.value = false
  ElMessage.error('文档预览失败')
}

function handleClose() {
  loading.value = true
  currentComponent.value = null
  if (resizeObserver) {
    resizeObserver.disconnect()
    resizeObserver = null
  }
}

onBeforeUnmount(() => {
  if (resizeObserver) {
    resizeObserver.disconnect()
    resizeObserver = null
  }
})
</script>

<style scoped>
.doc-preview-container {
  width: 100%;
  height: calc(100vh - 120px);
  overflow: auto;
}

.doc-preview-loading {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  height: 300px;
  color: #909399;
  gap: 12px;
}

.pptx-wrapper {
  width: 100%;
}
</style>
