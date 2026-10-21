import { Routes } from '@angular/router';
import { adminGuard, memberGuard } from './core/api';
export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'news' },
  { path: 'news', loadComponent: () => import('./features/news').then((m) => m.NewsPage) },
  {
    path: 'news/:id',
    loadComponent: () => import('./features/news').then((m) => m.NewsDetailPage),
  },
  ...['login', 'register', 'verify-email', 'forgot-password', 'reset-password', 'unsubscribe'].map(
    (path) => ({ path, loadComponent: () => import('./features/auth').then((m) => m.AuthPage) }),
  ),
  {
    path: 'subscriptions',
    canActivate: [memberGuard],
    loadComponent: () => import('./features/subscriptions').then((m) => m.SubscriptionsPage),
  },
  {
    path: 'settings',
    canActivate: [memberGuard],
    loadComponent: () => import('./features/settings').then((m) => m.SettingsPage),
  },
  {
    path: 'notifications',
    canActivate: [memberGuard],
    loadComponent: () => import('./features/notifications').then((m) => m.NotificationsPage),
  },
  {
    path: 'integrations/slack',
    canActivate: [memberGuard],
    loadComponent: () => import('./features/slack').then((m) => m.SlackPage),
  },
  {
    path: 'admin',
    canActivate: [adminGuard],
    loadComponent: () => import('./features/admin').then((m) => m.AdminPage),
  },
  {
    path: 'admin/:section',
    canActivate: [adminGuard],
    loadComponent: () => import('./features/admin').then((m) => m.AdminPage),
  },
  { path: '**', redirectTo: 'news' },
];
