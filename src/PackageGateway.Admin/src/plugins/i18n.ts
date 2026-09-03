import { ref } from 'vue';
import { createI18n } from 'vue-i18n';

export type AdminLanguage = 'en' | 'sv';
export type AdminLanguagePreference = 'system' | AdminLanguage;

const storageKey = 'package-gateway-language';
const stored = localStorage.getItem(storageKey);
const storedLanguage: AdminLanguage | undefined = stored === 'en' || stored === 'sv' ? stored : undefined;

export const languagePreference = ref<AdminLanguagePreference>(storedLanguage ?? 'system');

export function detectLanguage(languages: readonly string[] = navigator.languages): AdminLanguage {
  const candidates = languages.length ? languages : [navigator.language];

  for (const value of candidates) {
    const locale = value.toLowerCase().split('-')[0];

    if (locale === 'sv' || locale === 'en')
      return locale;
  }

  return 'en';
}

export const language = ref<AdminLanguage>(storedLanguage ?? detectLanguage());

export const i18n = createI18n({
  legacy: false,
  locale: language.value,
  fallbackLocale: 'en',
  messages: { en: {}, sv: {} },
});

function applyLanguage(value: AdminLanguage) {
  language.value = value;
  i18n.global.locale.value = value;
  document.documentElement.lang = value;
}

export function setLanguage(value: AdminLanguage) {
  setLanguagePreference(value);
}

export function setLanguagePreference(value: AdminLanguagePreference) {
  languagePreference.value = value;

  if (value === 'system') {
    localStorage.removeItem(storageKey);
    applyLanguage(detectLanguage());
  }
  else {
    localStorage.setItem(storageKey, value);
    applyLanguage(value);
  }
}

applyLanguage(language.value);
window.addEventListener('languagechange', () => {
  if (languagePreference.value === 'system')
    applyLanguage(detectLanguage());
});
