import { defineConfig } from 'vitepress'

const base = process.env.DOCS_BASE ?? '/docs/'

export default defineConfig({
  title: 'Secure Package Gateway',
  description: 'Operations, protocol, security, UI, and management API documentation.',
  base,
  outDir: process.env.DOCS_OUT_DIR,
  cleanUrls: true,
  lastUpdated: false,
  head: [['link', { rel: 'icon', type: 'image/svg+xml', href: `${base}aditi-logo.svg` }]],
  themeConfig: {
    logo: '/shield.svg',
    nav: [{ text: 'Guide', link: '/guide/introduction' }, { text: 'Management API', link: '/management-api/overview' }, { text: 'Operations', link: '/operations/deployment' }],
    sidebar: [
      { text: 'Guide', items: [{ text: 'Introduction', link: '/guide/introduction' }, { text: 'How delivery works', link: '/guide/architecture' }, { text: 'Security decisions', link: '/guide/security-decisions' }, { text: 'Administration UI', link: '/guide/admin-ui' }] },
      { text: 'Package clients', items: [{ text: 'NuGet', link: '/protocols/nuget' }, { text: 'npm', link: '/protocols/npm' }] },
      { text: 'Management API', items: [{ text: 'GraphQL overview', link: '/management-api/overview' }, { text: 'Queries', link: '/management-api/queries' }, { text: 'Mutations', link: '/management-api/mutations' }, { text: 'Authentication', link: '/management-api/authentication' }] },
      { text: 'Operations', items: [{ text: 'Deployment', link: '/operations/deployment' }, { text: 'Configuration', link: '/operations/configuration' }, { text: 'Database lifecycle', link: '/operations/database' }, { text: 'Observability', link: '/operations/observability' }, { text: 'Runbooks', link: '/operations/runbooks' }] },
    ],
    search: { provider: 'local' },
    footer: { message: 'Secure Package Gateway', copyright: 'Internal documentation' },
  },
})
