import { IconName } from '../../shared/ui/icon/icon-paths';

export interface NavItem {
  readonly label: string;
  readonly link: string;
  readonly icon: IconName;
  /** Match the URL exactly (for index routes such as /admin). */
  readonly exact?: boolean;
}

export interface NavGroup {
  readonly label: string;
  readonly items: readonly NavItem[];
}

/**
 * Customer navigation. Routes are the approved proposals from the plan; they resolve once
 * module A registers them (step 8 onward). "Saved Ideas" from the mockups is left out until the
 * Team Captain decides its content source.
 */
export const CUSTOMER_NAV: readonly NavItem[] = [
  { label: 'Dashboard', link: '/app/dashboard', icon: 'home' },
  { label: 'My Projects', link: '/app/projects', icon: 'layers' },
];

export const CUSTOMER_PRIMARY_ACTION: NavItem = {
  label: 'New Project',
  link: '/app/start',
  icon: 'plus',
};

export const CUSTOMER_ACCOUNT_NAV: readonly NavItem[] = [
  { label: 'Profile', link: '/app/profile', icon: 'user' },
  { label: 'Notifications', link: '/app/notifications', icon: 'bell' },
];

/** Admin navigation: list screens A02–A18 (detail screens A04, A06, A11, A13 are reached from lists). */
export const ADMIN_NAV: readonly NavGroup[] = [
  { label: 'Overview', items: [{ label: 'Dashboard', link: '/admin', icon: 'grid', exact: true }] },
  { label: 'People', items: [{ label: 'Users', link: '/admin/users', icon: 'users' }] },
  {
    label: 'Projects',
    items: [
      { label: 'Projects', link: '/admin/projects', icon: 'folder' },
      { label: 'Survey uploads', link: '/admin/surveys', icon: 'file' },
      { label: 'Land & map', link: '/admin/land', icon: 'map-pin' },
      { label: 'Requirements', link: '/admin/requirements', icon: 'ruler' },
    ],
  },
  {
    label: 'AI & 2D plans',
    items: [
      { label: 'Generation jobs', link: '/admin/jobs', icon: 'sparkles' },
      { label: '2D plans', link: '/admin/plans', icon: 'layers' },
      { label: 'Validation', link: '/admin/validation', icon: 'shield' },
      { label: 'Confirmations', link: '/admin/confirmations', icon: 'check-circle' },
      { label: 'Reports & downloads', link: '/admin/exports', icon: 'download' },
    ],
  },
  {
    label: 'System',
    items: [
      { label: 'Health & audit', link: '/admin/system', icon: 'server' },
      { label: 'Roles & settings', link: '/admin/settings', icon: 'settings' },
    ],
  },
];

/** Public navigation. Section links scroll the Home Page; pages are routed. */
export interface PublicNavItem {
  readonly label: string;
  readonly link: string;
  readonly fragment?: string;
}

export const PUBLIC_NAV: readonly PublicNavItem[] = [
  { label: 'Home', link: '/' },
  { label: 'Features', link: '/', fragment: 'features' },
  { label: 'How It Works', link: '/', fragment: 'how-it-works' },
  { label: 'About', link: '/about' },
  { label: 'Contact', link: '/contact' },
];
