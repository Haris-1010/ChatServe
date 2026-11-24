import { Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { Api, CurrentRestaurant } from '../core/api';
import { Auth } from '../core/auth';
import { errorText, Toasts } from '../core/toast';

@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, FormsModule],
  template: `
    <div class="layout" [class.nav-open]="navOpen()">
      <aside class="sidebar">
        <div class="brand"><span class="logo">💬</span> ChatServe</div>

        @if (restaurants.list().length > 1) {
          <label class="field compact">
            <span>Restaurant</span>
            <select [ngModel]="restaurants.id()" (ngModelChange)="restaurants.select(+$event)">
              @for (r of restaurants.list(); track r.id) {
                <option [value]="r.id">{{ r.name }}{{ r.isBotEnabled ? '' : ' (bot OFF)' }}</option>
              }
            </select>
          </label>
        }

        <nav (click)="navOpen.set(false)">
          <div class="nav-label">Daily work</div>
          <a routerLink="/" routerLinkActive="active" [routerLinkActiveOptions]="{ exact: true }"><span>📊</span> Overview</a>
          <a routerLink="/orders" routerLinkActive="active"><span>🧾</span> Orders</a>
          <a routerLink="/chats" routerLinkActive="active"><span>💬</span> Chats</a>
          <a routerLink="/bookings" routerLinkActive="active"><span>📅</span> Table bookings</a>
          <a routerLink="/customers" routerLinkActive="active"><span>👥</span> Customers</a>
          <div class="nav-label">Bot training</div>
          <a routerLink="/menu" routerLinkActive="active"><span>🍛</span> Menu, deals, FAQs</a>
          <a routerLink="/restaurant" routerLinkActive="active"><span>🏪</span> Restaurant info</a>
          <a routerLink="/simulator" routerLinkActive="active"><span>🧪</span> Bot test (simulator)</a>
          @if (auth.isAdmin()) {
            <div class="nav-label">Setup</div>
            <a routerLink="/users" routerLinkActive="active"><span>🔑</span> Users (owner logins)</a>
            <a routerLink="/settings" routerLinkActive="active"><span>⚙️</span> Settings</a>
          } @else {
            <div class="nav-label">Account</div>
            <a routerLink="/account" routerLinkActive="active"><span>🔒</span> My account</a>
          }
        </nav>

        @if (auth.me(); as m) { <div class="muted small signed-in">Signed in as <b>{{ m.username }}</b>{{ m.role === 'admin' ? ' (admin)' : '' }}</div> }
        <button class="btn ghost block logout" (click)="logout()">Logout</button>
      </aside>

      <div class="main">
        <header class="topbar">
          <button class="btn ghost menu-btn" (click)="navOpen.set(!navOpen())" aria-label="Menu">☰</button>
          <div class="topbar-title">{{ restaurants.current?.name ?? 'ChatServe' }}</div>
          @if (restaurants.current; as r) {
            <span class="pill" [class.ok]="r.isBotEnabled" [class.warn]="!r.isBotEnabled">Bot {{ r.isBotEnabled ? 'ON' : 'OFF' }}</span>
          }
        </header>
        <main class="content">
          @if (loaded()) {
            @if (restaurants.list().length === 0 && auth.isAdmin()) {
              <div class="card empty">
                <h2>No restaurant yet</h2>
                <p class="muted">Add your WhatsApp <b>Phone number ID</b> in Settings and restart the app (a demo restaurant is created automatically), or create a new restaurant on the <a routerLink="/restaurant">Restaurant info</a> page.</p>
              </div>
            }
            <router-outlet />
          }
        </main>
      </div>
    </div>
  `,
})
export class Shell implements OnInit {
  protected restaurants = inject(CurrentRestaurant);
  protected auth = inject(Auth);
  private api = inject(Api);
  private router = inject(Router);
  private toasts = inject(Toasts);
  navOpen = signal(false);
  loaded = signal(false);

  async ngOnInit() {
    try {
      if (!this.auth.me()) this.auth.me.set(await this.api.me());
      await this.restaurants.load();
    } catch (e) {
      this.toasts.error(errorText(e));
    } finally {
      this.loaded.set(true);
    }
  }

  logout() {
    this.auth.clear();
    this.router.navigate(['/login']);
  }
}
