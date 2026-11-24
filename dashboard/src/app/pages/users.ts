import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Api, CurrentRestaurant, DashboardUserRow, when } from '../core/api';
import { errorText, Toasts } from '../core/toast';

/** Admin only: dashboard logins for restaurant owners. Each owner sees only their own restaurant. */
@Component({
  selector: 'app-users',
  imports: [FormsModule],
  template: `
    <div class="page-head"><h1>Users (owner logins)</h1></div>
    <p class="muted">Give each restaurant owner their own login. An owner only sees their own restaurant: orders, chats,
      bookings, customers, menu and restaurant info. They can't see other restaurants, app Settings or this page.</p>

    <section class="card">
      <h2>New owner login</h2>
      <form class="form-row" (ngSubmit)="create()">
        <label class="field"><span>Restaurant</span>
          <select name="rid" [(ngModel)]="newRestaurantId">
            @for (r of restaurants.list(); track r.id) { <option [ngValue]="r.id">{{ r.name }}</option> }
          </select></label>
        <label class="field grow"><span>Username</span>
          <input name="u" [(ngModel)]="newUsername" placeholder="e.g. ali.karahi" autocomplete="off" /></label>
        <label class="field grow"><span>Password (min 8)</span>
          <input name="p" [(ngModel)]="newPassword" type="text" autocomplete="new-password" /></label>
        <button class="btn ghost" type="button" (click)="newPassword = generate()">🎲 Generate</button>
        <button class="btn primary" [disabled]="busy()">+ Create</button>
      </form>
      <p class="muted small">Share the username and password with the owner privately. They can change the password later under "My account".</p>
    </section>

    <section class="card flush">
      <table class="table">
        <thead><tr><th>Username</th><th>Restaurant</th><th class="hide-sm">Created</th><th>Last login</th><th></th></tr></thead>
        <tbody>
          @for (u of users(); track u.id) {
            <tr>
              <td><b>{{ u.username }}</b></td>
              <td>{{ u.restaurantName }}</td>
              <td class="muted hide-sm">{{ when(u.createdAt) }}</td>
              <td class="muted">{{ u.lastLoginAt ? when(u.lastLoginAt) : 'Never' }}</td>
              <td class="actions">
                <button class="btn ghost sm" (click)="reset(u)">Reset password</button>
                <button class="btn danger-ghost sm" (click)="remove(u)">🗑</button>
              </td>
            </tr>
          } @empty { <tr><td colspan="5" class="muted">No owner logins yet. Only the admin can log in.</td></tr> }
        </tbody>
      </table>
    </section>
  `,
})
export class UsersPage {
  private api = inject(Api);
  private toasts = inject(Toasts);
  protected restaurants = inject(CurrentRestaurant);
  users = signal<DashboardUserRow[]>([]);
  busy = signal(false);
  newRestaurantId: number | null = this.restaurants.id();
  newUsername = '';
  newPassword = '';
  when = when;

  constructor() { this.load(); }

  async load() {
    try { this.users.set(await this.api.users()); } catch (e) { this.toasts.error(errorText(e)); }
  }

  generate() {
    const chars = 'abcdefghjkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789';
    const bytes = crypto.getRandomValues(new Uint8Array(12));
    return Array.from(bytes, b => chars[b % chars.length]).join('');
  }

  async create() {
    if (!this.newRestaurantId) { this.toasts.error('Choose a restaurant.'); return; }
    this.busy.set(true);
    try {
      await this.api.createUser(this.newUsername, this.newPassword, this.newRestaurantId);
      this.toasts.ok(`Login "${this.newUsername.trim().toLowerCase()}" created. Share the password with the owner now.`);
      this.newUsername = '';
      this.newPassword = '';
      await this.load();
    } catch (e) {
      this.toasts.error(errorText(e));
    } finally {
      this.busy.set(false);
    }
  }

  async reset(u: DashboardUserRow) {
    const password = prompt(`New password for "${u.username}" (min 8 characters). They will be logged out everywhere.`, this.generate());
    if (!password) return;
    try {
      await this.api.resetUserPassword(u.id, password);
      this.toasts.ok(`Password for "${u.username}" changed. Share the new one with the owner.`);
    } catch (e) {
      this.toasts.error(errorText(e));
    }
  }

  async remove(u: DashboardUserRow) {
    if (!confirm(`Delete the login "${u.username}"? They will be logged out immediately.`)) return;
    try {
      await this.api.deleteUser(u.id);
      this.toasts.ok('Login deleted');
      await this.load();
    } catch (e) {
      this.toasts.error(errorText(e));
    }
  }
}
