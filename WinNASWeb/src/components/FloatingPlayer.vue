<template>
  <teleport to="body">
    <div v-if="player.visible" class="floating-player" :style="playerStyle" @mousedown="startDrag">
      <div class="player-header">
        <span class="player-title">{{ player.title }}</span>
        <div class="player-controls">
          <el-button v-if="player.playlist.length > 1 && player.currentIndex > 0" link size="small" @click.stop="handlePrev" title="上一个">
            <el-icon><DArrowLeft /></el-icon>
          </el-button>
          <el-button v-if="player.playlist.length > 1 && player.currentIndex < player.playlist.length - 1" link size="small" @click.stop="handleNext" title="下一个">
            <el-icon><DArrowRight /></el-icon>
          </el-button>
          <el-button link size="small" @click.stop="toggleMinimize" :title="minimized ? '展开' : '最小化'">
            <el-icon><component :is="minimized ? 'FullScreen' : 'Minus'" /></el-icon>
          </el-button>
          <el-button link size="small" @click.stop="handleClose" title="关闭">
            <el-icon><Close /></el-icon>
          </el-button>
        </div>
      </div>
      <div v-show="!minimized" class="player-body" :class="{ 'player-body-audio': player.isAudio }">
        <video
          v-if="player.isVideo"
          ref="videoRef"
          :src="player.url"
          controls
          autoplay
          @ended="onMediaEnded"
          style="width: 100%; max-height: 320px; background: #000"
        />
        <audio
          v-if="player.isAudio"
          ref="audioRef"
          :src="player.url"
          controls
          autoplay
          @ended="onMediaEnded"
          style="width: 100%"
        />
      </div>
      <div v-if="minimized && player.isAudio" class="mini-audio-info">
        <el-icon><Headset /></el-icon>
        <span>{{ player.title }}</span>
      </div>
    </div>
  </teleport>
</template>

<script setup>
import { ref, computed, watch, nextTick } from 'vue'
import { usePlayerStore } from '../store/player'

const player = usePlayerStore()
const videoRef = ref(null)
const audioRef = ref(null)
const minimized = ref(false)

const posX = ref(window.innerWidth - 420)
const posY = ref(window.innerHeight - 380)
const dragging = ref(false)
const dragOffsetX = ref(0)
const dragOffsetY = ref(0)

const playerStyle = computed(() => {
  const w = minimized.value ? 280 : 400
  const h = minimized.value ? 'auto' : 'auto'
  return {
    left: posX.value + 'px',
    top: posY.value + 'px',
    width: w + 'px',
    minHeight: minimized.value ? '40px' : 'auto'
  }
})

function startDrag(e) {
  if (e.target.closest('.player-body') || e.target.closest('video') || e.target.closest('audio') || e.target.closest('input')) return
  dragging.value = true
  dragOffsetX.value = e.clientX - posX.value
  dragOffsetY.value = e.clientY - posY.value
  document.addEventListener('mousemove', onDrag)
  document.addEventListener('mouseup', stopDrag)
}

function onDrag(e) {
  if (!dragging.value) return
  posX.value = Math.max(0, Math.min(window.innerWidth - 100, e.clientX - dragOffsetX.value))
  posY.value = Math.max(0, Math.min(window.innerHeight - 50, e.clientY - dragOffsetY.value))
}

function stopDrag() {
  dragging.value = false
  document.removeEventListener('mousemove', onDrag)
  document.removeEventListener('mouseup', stopDrag)
}

function toggleMinimize() {
  minimized.value = !minimized.value
}

function handleClose() {
  player.close()
  player.clearPlaylist()
  minimized.value = false
}

function handleNext() {
  player.playNext()
}

function handlePrev() {
  player.playPrev()
}

function onMediaEnded() {
  if (player.playlist.length > 1) {
    player.playNext()
  }
}

watch(() => player.visible, (val) => {
  if (val) {
    minimized.value = false
    posX.value = Math.min(posX.value, window.innerWidth - 200)
    posY.value = Math.min(posY.value, window.innerHeight - 100)
  }
})
</script>

<style scoped>
.floating-player {
  position: fixed;
  z-index: 9999;
  background: #fff;
  border-radius: 8px;
  box-shadow: 0 4px 20px rgba(0, 0, 0, 0.25);
  overflow: hidden;
  user-select: none;
  transition: width 0.2s;
}

.player-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 6px 10px;
  background: #409eff;
  color: #fff;
  cursor: move;
  gap: 8px;
}

.player-title {
  flex: 1;
  font-size: 13px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.player-controls {
  display: flex;
  align-items: center;
  gap: 2px;
  flex-shrink: 0;
}

.player-controls .el-button {
  color: #fff !important;
}

.player-body {
  padding: 0;
  background: #000;
}

.player-body-audio {
  background: #fff;
}

.player-body audio {
  display: block;
  width: 100%;
  border-radius: 0 0 8px 8px;
}

.mini-audio-info {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 8px 12px;
  font-size: 12px;
  color: #606266;
  background: #fff;
}

.mini-audio-info span {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
</style>
