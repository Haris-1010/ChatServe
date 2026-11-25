import { Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { Api, CurrentRestaurant } from '../core/api';
import { Auth } from '../core/auth';
import { errorText, Toasts } from '../core/toast';

@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, FormsModule],
  styles: [`
    .usage-banner { display: block; margin-bottom: 14px; padding: 10px 14px; border-radius: 10px; text-decoration: none;
      background: rgba(245, 166, 35, .14); border: 1px solid rgba(245, 166, 35, .45); color: inherit; font-size: 14px; }
  `],
  template: `
    <div class="layout" [class.nav-open]="navOpen()">
      <aside class="sidebar">
        <div class="brand"><span class="logo">💬</span> ChatServe</div>

        <nav (click)="navOpen.set(false)">
          @if (auth.isAdmin()) {
            <div class="nav-label">Admin</div>
            <a routerLink="/clients" routerLinkActive="active"><span>🏢</span> All clients</a>
            <a routerLink="/usage" routerLinkActive="active"><span>📊</span> AI usage</a>
            <a routerLink="/settings" routerLinkActive="active"><span>⚙️</span> Settings</a>
          }
        </nav>

        @if (restaurants.list().length > 1) {
          <label class="field compact">
            <span>{{ auth.isAdmin() ? 'Restaurant you are viewing' : 'Branch' }}</span>
            <select [ngModel]="restaurants.id()" (ngModelChange)="restaurants.select(+$event)">
              @if (auth.isAdmin()) {
                @for (g of restaurants.groups(); track g.name) {
                  <optgroup [label]="g.name">
                    @for (r of g.items; track r.id) {
                      <option [value]="r.id">{{ r.name }}{{ r.isBotEnabled ? '' : ' (bot OFF)' }}</option>
                    }
                  </optgroup>
                }
              } @else {
                @for (r of restaurants.list(); track r.id) {
                  <option [value]="r.id">{{ r.name }}{{ r.isBotEnabled ? '' : ' (bot OFF)' }}</option>
                }
              }
            </select>
          </label>
        }

        @if (restaurants.current) {
        <nav (click)="navOpen.set(false)">
          <div class="nav-label">Every day</div>
          <a routerLink="/" routerLinkActive="active" [routerLinkActiveOptions]="{ exact: true }"><span>📊</span> Home</a>
          <a routerLink="/orders" routerLinkActive="active"><span>🧾</span> Orders</a>
          <a routerLink="/chats" routerLinkActive="active"><span>💬</span> Chats</a>
          <a routerLink="/bookings" routerLinkActive="active"><span>📅</span> Bookings</a>
          <a routerLink="/customers" routerLinkActive="active"><span>👥</span> Customers</a>
          <a routerLink="/feedback" routerLinkActive="active"><span>⭐</span> Feedback</a>
          <div class="nav-label">Bot setup</div>
          <a routerLink="/menu" routerLinkActive="active"><span>🍛</span> Menu &amp; FAQs</a>
          <a routerLink="/restaurant" routerLinkActive="active"><span>🏪</span> Restaurant info</a>
          <a routerLink="/training" routerLinkActive="active"><span>🎓</span> Bot training</a>
          <a routerLink="/simulator" routerLinkActive="active"><span>🧪</span> Test the bot</a>
          @if (!auth.isAdmin()) {
            <div class="nav-label">Account</div>
            <a routerLink="/usage" routerLinkActive="active"><span>📊</span> AI usage</a>
            <a routerLink="/account" routerLinkActive="active"><span>🔒</span> My account</a>
          }
        </nav>
        }

        @if (auth.me(); as m) {
          <div class="muted small signed-in">Signed in as <b>{{ m.username }}</b>
            {{ m.role === 'admin' ? '(admin: sees all clients)' : m.organizationName ? '· ' + m.organizationName : '' }}</div>
        }
        <button class="btn ghost block logout" (click)="logout()">Logout</button>
      </aside>

      <div class="main">
        <header class="topbar">
          <button class="btn ghost menu-btn" (click)="navOpen.set(!navOpen())" aria-label="Menu">☰</button>
          <div class="topbar-title">
            @if (onAdminPage()) { Admin }
            @else {
              @if (auth.isAdmin() && restaurants.current; as r) { <span class="muted">{{ r.organizationName }} ›</span> }
              {{ restaurants.current?.name ?? 'ChatServe' }}
            }
          </div>
          @if (!onAdminPage() && restaurants.current; as r) {
            <span class="pill" [class.ok]="r.isBotEnabled" [class.warn]="!r.isBotEnabled">Bot {{ r.isBotEnabled ? 'ON' : 'OFF' }}</span>
          }
        </header>
        <main class="content">
          @if (usageWarning(); as w) {
            <a class="usage-banner" routerLink="/usage">⚠️ {{ w }}</a>
          }
          @if (loaded()) {
            @if (restaurants.list().length === 0 && auth.isAdmin() && !onAdminPage()) {
              <div class="card empty">
                <h2>No restaurant yet</h2>
                <p class="muted">Go to <a routerLink="/clients">All clients</a> and click <b>+ New client</b> to add the first restaurant.</p>
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
  usageWarning = signal('');

  async ngOnInit() {
    try {
      if (!this.auth.me()) this.auth.me.set(await this.api.me());
      await this.restaurants.load();
      this.checkUsage();
    } catch (e) {
      this.toasts.error(errorText(e));
    } finally {
      this.loaded.set(true);
    }
  }

  onAdminPage() {
    const u = this.router.url;
    return u.startsWith('/clients') || u.startsWith('/settings') || (this.auth.isAdmin() && u.startsWith('/usage'));
  }

  /** A banner when a client is near or over its monthly AI limit (only a warning; the bot keeps working). */
  private async checkUsage() {
    try {
      const u = await this.api.usage();
      const near = u.clients.filter(c => c.warning);
      if (near.length === 0) return;
      this.usageWarning.set(this.auth.isAdmin()
        ? `${near.length} client(s) near or over their monthly AI replies: ${near.map(c => `${c.name} ${c.percent}%`).join(', ')}`
        : `You have used ${near[0].percent}% of this month's AI replies (${near[0].usage.replies} of ${near[0].monthlyReplyLimit}).`);
    } catch { /* the banner is optional */ }
  }

  logout() {
    this.auth.clear();
    this.router.navigate(['/login']);
  }
}
