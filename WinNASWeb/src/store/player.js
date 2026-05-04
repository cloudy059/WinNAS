import { defineStore } from 'pinia'
import { ref } from 'vue'

export const usePlayerStore = defineStore('player', () => {
  const visible = ref(false)
  const url = ref('')
  const title = ref('')
  const isVideo = ref(false)
  const isAudio = ref(false)
  const playlist = ref([])
  const currentIndex = ref(-1)

  function play(item) {
    url.value = item.url
    title.value = item.title
    isVideo.value = item.isVideo || false
    isAudio.value = item.isAudio || false
    visible.value = true
  }

  function playList(items, index = 0) {
    playlist.value = items
    currentIndex.value = index
    if (items.length > 0 && index >= 0 && index < items.length) {
      play(items[index])
    }
  }

  function playNext() {
    if (playlist.value.length > 0 && currentIndex.value < playlist.value.length - 1) {
      currentIndex.value++
      play(playlist.value[currentIndex.value])
      return true
    }
    return false
  }

  function playPrev() {
    if (playlist.value.length > 0 && currentIndex.value > 0) {
      currentIndex.value--
      play(playlist.value[currentIndex.value])
      return true
    }
    return false
  }

  function close() {
    visible.value = false
    url.value = ''
    title.value = ''
    isVideo.value = false
    isAudio.value = false
  }

  function clearPlaylist() {
    playlist.value = []
    currentIndex.value = -1
  }

  return {
    visible, url, title, isVideo, isAudio,
    playlist, currentIndex,
    play, playList, playNext, playPrev, close, clearPlaylist
  }
})
