import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import { VitePWA } from 'vite-plugin-pwa'

export default defineConfig({
  plugins: [
    react(),
    VitePWA({
      registerType: 'autoUpdate',
      includeAssets: ['favicon.svg', 'icons/icon.svg'],
      manifest: {
        name: 'Bagly',
        short_name: 'Bagly',
        description: 'Bagly — curated bags for everyday carry, travel, and work.',
        start_url: '/',
        display: 'standalone',
        background_color: '#1b3d2f',
        theme_color: '#1b3d2f',
        icons: [
          {
            src: '/icons/icon-192.png',
            sizes: '192x192',
            type: 'image/png',
          },
          {
            src: '/icons/icon-512.png',
            sizes: '512x512',
            type: 'image/png',
          },
          {
            src: '/icons/icon-512.png',
            sizes: '512x512',
            type: 'image/png',
            purpose: 'maskable',
          },
        ],
      },
      workbox: {
        navigateFallback: '/index.html',
        navigateFallbackDenylist: [/^\/api/, /^\/hubs/],
        runtimeCaching: [
          {
            urlPattern: /^\/api(?:\/|$)/i,
            handler: 'NetworkOnly',
          },
          {
            urlPattern: /^\/hubs(?:\/|$)/i,
            handler: 'NetworkOnly',
          },
          {
            urlPattern: /^https?:\/\/[^/]+\/api(?:\/|$)/i,
            handler: 'NetworkOnly',
          },
          {
            urlPattern: /^https?:\/\/[^/]+\/hubs(?:\/|$)/i,
            handler: 'NetworkOnly',
          },
          {
            urlPattern: /^https:\/\/checkout\.razorpay\.com\/.*/i,
            handler: 'NetworkOnly',
          },
        ],
      },
    }),
  ],
  server: {
    proxy: {
      '/api': {
        target: 'http://localhost:5032',
        changeOrigin: true,
      },
      '/hubs': {
        target: 'http://localhost:5032',
        changeOrigin: true,
        ws: true,
      },
    },
  },
})
