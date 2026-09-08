import react from '@vitejs/plugin-react';
import { defineConfig } from 'vite';

const apiProxy = {
  target: process.env.YOWTHI_ERP_API_PROXY_TARGET ?? 'http://127.0.0.1:5180',
  changeOrigin: false,
};

export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      '/api': apiProxy,
      '/auth': apiProxy,
      '/health': apiProxy,
    },
  },
  preview: {
    allowedHosts: ['desktop-9jmcsvj.tail1abebb.ts.net'],
    proxy: {
      '/api': apiProxy,
      '/auth': apiProxy,
      '/health': apiProxy,
    },
  },
});
