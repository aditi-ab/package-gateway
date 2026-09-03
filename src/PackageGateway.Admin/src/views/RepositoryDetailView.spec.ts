// @vitest-environment jsdom

import { flushPromises, mount } from '@vue/test-utils';
import { describe, expect, it, vi } from 'vitest';
import { createI18n } from 'vue-i18n';
import { createMemoryHistory, createRouter } from 'vue-router';
import RepositoryDetailView from './RepositoryDetailView.vue';

const { graphql } = vi.hoisted(() => ({ graphql: vi.fn(async (query: string) => {
  if (query.includes('repository(id: $id)')) {
    return {
      repository: { id: 'repo-1', name: 'Development', slug: 'dev', packageTypes: ['NU_GET', 'NPM'], enabled: true, description: 'Development packages', updatedAt: '2026-08-29T00:00:00Z' },
      upstreams: [],
      policies: { nodes: [] },
    };
  }

  return { policies: { nodes: [] } };
}) }));

vi.mock('@/api/graphql', () => ({ graphql, mutationError: vi.fn() }));

describe('repository detail tabs', () => {
  it('switches child content without navigating the repository route', async () => {
    const router = createRouter({
      history: createMemoryHistory('/admin/'),
      routes: [
        { path: '/repositories/:id/:tab?', component: RepositoryDetailView },
        { path: '/policies', component: { template: '<div />' } },
        { path: '/repositories', component: { template: '<div />' } },
      ],
    });

    await router.push('/repositories/repo-1/settings');
    await router.isReady();

    const wrapper = mount(RepositoryDetailView, {
      global: {
        plugins: [router, createI18n({ legacy: false, locale: 'en' })],
        stubs: { PackagesView: true, ReviewQueueView: true },
      },
    });

    await flushPromises();

    expect(wrapper.text()).toContain('General settings');

    const routeBeforeSwitch = router.currentRoute.value.fullPath;

    const upstreamsTab = wrapper.findAll('[role="tab"]').find(candidate => candidate.text() === 'Upstreams');

    expect(upstreamsTab).toBeDefined();
    await upstreamsTab!.trigger('mousedown', { button: 0, ctrlKey: false });
    await flushPromises();

    expect(router.currentRoute.value.fullPath).toBe(routeBeforeSwitch);
    expect(wrapper.text()).toContain('Upstream proxies');
  });
});
