import { Routes } from '@angular/router';
import { adminGuard, authGuard } from './core/auth';
import { Login } from './pages/login';
import { Shell } from './pages/shell';

export const routes: Routes = [
  { path: 'login', component: Login },
  {
    path: '',
    component: Shell,
    canActivate: [authGuard],
    children: [
      { path: '', pathMatch: 'full', loadComponent: () => import('./pages/overview').then(m => m.OverviewPage) },
      { path: 'orders', loadComponent: () => import('./pages/orders').then(m => m.OrdersPage) },
      { path: 'chats', loadComponent: () => import('./pages/chats').then(m => m.ChatsPage) },
      { path: 'simulator', loadComponent: () => import('./pages/simulator').then(m => m.SimulatorPage) },
      { path: 'menu', loadComponent: () => import('./pages/menu').then(m => m.MenuPage) },
      { path: 'bookings', loadComponent: () => import('./pages/bookings').then(m => m.BookingsPage) },
      { path: 'customers', loadComponent: () => import('./pages/customers').then(m => m.CustomersPage) },
      { path: 'training', loadComponent: () => import('./pages/training').then(m => m.TrainingPage) },
      { path: 'restaurant', loadComponent: () => import('./pages/restaurant').then(m => m.RestaurantPage) },
      { path: 'settings', canActivate: [adminGuard], loadComponent: () => import('./pages/settings').then(m => m.SettingsPage) },
      { path: 'clients', canActivate: [adminGuard], loadComponent: () => import('./pages/clients').then(m => m.ClientsPage) },
      { path: 'users', redirectTo: 'clients' },
      { path: 'account', loadComponent: () => import('./pages/account').then(m => m.AccountPage) },
    ],
  },
  { path: '**', redirectTo: '' },
];
