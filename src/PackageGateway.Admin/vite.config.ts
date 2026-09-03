import { fileURLToPath, URL } from 'node:url';
import VueI18nPlugin from '@intlify/unplugin-vue-i18n/vite';
import tailwindcss from '@tailwindcss/vite';
import vue from '@vitejs/plugin-vue';
import { defineConfig } from 'vite';

export default defineConfig({
  base: '/admin/',
  plugins: [tailwindcss(), vue(), VueI18nPlugin()],
  resolve: { alias: { '@': fileURLToPath(new URL('./src', import.meta.url)) }, dedupe: ['vue', 'reka-ui', '@lucide/vue'] },
  server: { proxy: { '/graphql': 'http://localhost:54227', '/admin/config.json': 'http://localhost:54227', '/admin/auth': 'http://localhost:54227', '/admin/identity': 'http://localhost:54227' } },
});
