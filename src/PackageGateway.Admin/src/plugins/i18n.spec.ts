// @vitest-environment jsdom
import { describe, expect, it } from 'vitest';
import { detectLanguage, i18n, language, languagePreference, setLanguage, setLanguagePreference } from './i18n';

describe('language preference', () => {
  it('selects Swedish from Swedish regional locales and otherwise uses English', () => {
    expect(detectLanguage(['sv-SE', 'en-US'])).toBe('sv');
    expect(detectLanguage(['en-US', 'sv-SE'])).toBe('en');
    expect(detectLanguage(['en-GB', 'de-DE'])).toBe('en');
  });

  it('updates the active locale and persists the selection', () => {
    setLanguage('sv');
    expect(language.value).toBe('sv');
    expect(languagePreference.value).toBe('sv');
    expect(i18n.global.locale.value).toBe('sv');
    expect(localStorage.getItem('package-gateway-language')).toBe('sv');
    expect(document.documentElement.lang).toBe('sv');
  });

  it('can return to automatic system language selection', () => {
    setLanguagePreference('system');
    expect(languagePreference.value).toBe('system');
    expect(localStorage.getItem('package-gateway-language')).toBeNull();
  });
});
