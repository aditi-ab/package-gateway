import { createRouter, createWebHistory } from 'vue-router';

export default createRouter({
  history: createWebHistory('/admin/'),
  routes: [
    {
      path: '/',
      component: () => import('@/views/DashboardView.vue'),
      meta: { title: 'Overview' },
    },
    {
      path: '/repositories',
      component: () => import('@/views/RepositoriesView.vue'),
      meta: { title: 'Repositories' },
    },
    {
      path: '/packages',
      component: () => import('@/views/PackagesView.vue'),
      meta: { title: 'Packages' },
    },
    {
      path: '/repositories/:id/:tab(packages|review|upstreams|policies|settings)?',
      component: () => import('@/views/RepositoryDetailView.vue'),
      meta: { title: 'Repository' },
    },
    {
      path: '/review',
      component: () => import('@/views/ReviewQueueView.vue'),
      meta: { title: 'Review queue' },
    },
    {
      path: '/policies',
      component: () => import('@/views/PoliciesView.vue'),
      meta: { title: 'Policies' },
    },
    {
      path: '/tokens',
      component: () => import('@/views/TokensView.vue'),
      meta: { title: 'Access tokens' },
    },
    { path: '/users', component: () => import('@/views/UsersView.vue'), meta: { title: 'Users and identity' } },
    {
      path: '/audit',
      component: () => import('@/views/AuditView.vue'),
      meta: { title: 'Audit history' },
    },
  ],
});
