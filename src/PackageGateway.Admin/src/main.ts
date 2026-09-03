import { createApp } from 'vue';
import App from './App.vue';
import { initializeAuth } from './auth/auth';
import { i18n } from './plugins/i18n';
import router from './router';
import '@fontsource-variable/inter';
import '@aditify/identity/styles.css';
import './styles.css';

await initializeAuth();
createApp(App).use(router).use(i18n).mount('#app');
