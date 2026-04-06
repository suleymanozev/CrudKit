import type {SidebarsConfig} from '@docusaurus/plugin-content-docs';

const sidebars: SidebarsConfig = {
  docs: [
    'intro',
    {
      type: 'category',
      label: 'Getting Started',
      items: [
        'getting-started/quick-start',
        'getting-started/entity-hierarchy',
        'getting-started/configuration',
      ],
    },
    {
      type: 'category',
      label: 'Features',
      items: [
        'features/soft-delete',
        'features/multi-tenant',
        'features/auth',
        'features/audit-trail',
        'features/hooks',
        'features/validation',
        'features/state-machine',
        'features/import-export',
        'features/bulk-operations',
        'features/idempotency',
        'features/concurrency',
        'features/document-numbering',
        'features/query-features',
      ],
    },
    {
      type: 'category',
      label: 'Advanced',
      items: [
        'advanced/modular-monolith',
        'advanced/source-generation',
        'advanced/custom-endpoints',
        'advanced/feature-flags',
        'advanced/error-handling',
      ],
    },
    {
      type: 'category',
      label: 'Reference',
      items: [
        'reference/attributes',
        'reference/interfaces',
        'reference/base-classes',
        'reference/configuration-options',
        'reference/endpoints-table',
      ],
    },
    {
      type: 'category',
      label: 'Guides',
      items: [
        'guides/testing',
        'guides/migrations',
        'guides/database-dialect',
        'guides/identity',
      ],
    },
  ],
};

export default sidebars;
