import {themes as prismThemes} from 'prism-react-renderer';
import type {Config} from '@docusaurus/types';
import type * as Preset from '@docusaurus/preset-classic';

const config: Config = {
  title: 'CrudKit',
  tagline: 'A convention-based CRUD framework for .NET 10',
  favicon: 'img/favicon.png',

  future: {
    v4: true,
  },

  url: 'https://suleymanozev.github.io',
  baseUrl: '/CrudKit/',

  organizationName: 'suleymanozev',
  projectName: 'CrudKit',

  onBrokenLinks: 'throw',
  markdown: {
    hooks: {
      onBrokenMarkdownLinks: 'warn',
    },
  },

  i18n: {
    defaultLocale: 'en',
    locales: ['en'],
  },

  presets: [
    [
      'classic',
      {
        docs: {
          sidebarPath: './sidebars.ts',
          routeBasePath: '/docs',
          editUrl: 'https://github.com/suleymanozev/CrudKit/tree/main/docs-site/',
        },
        blog: false,
        theme: {
          customCss: './src/css/custom.css',
        },
      } satisfies Preset.Options,
    ],
  ],

  themeConfig: {
    image: 'img/docusaurus-social-card.jpg',
    colorMode: {
      respectPrefersColorScheme: true,
    },
    navbar: {
      title: 'CrudKit',
      logo: {
        alt: 'CrudKit Logo',
        src: 'img/icon.png',
      },
      items: [
        {
          type: 'docSidebar',
          sidebarId: 'docs',
          position: 'left',
          label: 'Docs',
        },
        {
          to: '/docs/getting-started/quick-start',
          label: 'Getting Started',
          position: 'left',
        },
        {
          to: '/docs/reference/endpoints-table',
          label: 'Reference',
          position: 'left',
        },
        {
          href: 'https://github.com/suleymanozev/CrudKit',
          label: 'GitHub',
          position: 'right',
        },
      ],
    },
    footer: {
      style: 'dark',
      links: [
        {
          title: 'Documentation',
          items: [
            {label: 'Quick Start', to: '/docs/getting-started/quick-start'},
            {label: 'Features', to: '/docs/features/soft-delete'},
            {label: 'Reference', to: '/docs/reference/endpoints-table'},
          ],
        },
        {
          title: 'Guides',
          items: [
            {label: 'Testing', to: '/docs/guides/testing'},
            {label: 'Migrations', to: '/docs/guides/migrations'},
            {label: 'Database Dialect', to: '/docs/guides/database-dialect'},
          ],
        },
        {
          title: 'More',
          items: [
            {
              label: 'GitHub',
              href: 'https://github.com/suleymanozev/CrudKit',
            },
          ],
        },
      ],
      copyright: `Copyright © ${new Date().getFullYear()} CrudKit. Built with Docusaurus.`,
    },
    prism: {
      theme: prismThemes.github,
      darkTheme: prismThemes.dracula,
      additionalLanguages: ['csharp', 'bash', 'json'],
    },
  } satisfies Preset.ThemeConfig,
};

export default config;
