import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Api, money, ORDER_STATUS_LABEL, ORDER_TYPE_LABEL, Overview, Status, when } from '../core/api';
import { onRestaurant } from '../core/page';
import { errorText, Toasts } from '../core/toast';

@Component({
  selector: 'app-overview',
  imports: [RouterLink],
  template: `
    <div class="page-head">
      <h1>Overview</h1>
      <button class="btn ghost" (click)="refresh()">↻ Refresh</button>
    </div>

    @if (stats(); as s) {
      <div class="stats">
        <div class="stat"><div class="stat-label">Orders today</div><div class="stat-value">{{ s.ordersToday }}</div></div>
        <div class="stat"><div class="stat-label">Sales today</div><div class="stat-value">{{ money(s.revenueToday) }}</div></div>
        <a class="stat link" routerLink="/orders"><div class="stat-label">Active orders</div><div class="stat-value">{{ s.activeOrders }}</div>
          <div class="stat-sub">{{ s.awaitingConfirmation }} awaiting confirmation</div></a>
        <a class="stat link" routerLink="/chats"><div class="stat-label">Chats today</div><div class="stat-value">{{ s.chatsToday }}</div>
          <div class="stat-sub">{{ s.botRepliesToday }} bot replies · {{ s.pausedChats }} with staff</div></a>
        <a class="stat link" routerLink="/bookings"><div class="stat-label">Upcoming bookings</div><div class="stat-value">{{ s.upcomingBookings }}</div></a>
        <a class="stat link" routerLink="/customers"><div class="stat-label">Customers</div><div class="stat-value">{{ s.customers }}</div></a>
        <div class="stat"><div class="stat-label">AI tokens today</div><div class="stat-value small-value">{{ s.inputTokensToday.toLocaleString() }} in</div>
          <div class="stat-sub">{{ s.outputTokensToday.toLocaleString() }} out</div></div>
      </div>
    }

    <div class="grid-2">
      <section class="card">
        <h2>Setup checklist</h2>
        @if (status(); as st) {
          <ul class="checks">
            @for (c of st.checks; track c.name) {
              <li [class]="c.state">
                <span class="dot"></span>
                <div><b>{{ c.name }}</b><div class="muted small">{{ c.message }}</div></div>
              </li>
            }
          </ul>
          <div class="webhook">
            <div class="muted small">Callback URL for Meta (must be public HTTPS, e.g. ngrok):</div>
            <code class="copy" (click)="copy(st.webhookUrl)" title="Copy">{{ st.webhookUrl }}</code>
          </div>
          <a class="btn" routerLink="/settings">Open Settings →</a>
        }
      </section>

      <section class="card">
        <h2>Recent orders</h2>
        @if (stats()?.recentOrders?.length) {
          <table class="table">
            <thead><tr><th>#</th><th>Customer</th><th>Status</th><th class="num">Total</th><th>Time</th></tr></thead>
            <tbody>
              @for (o of stats()!.recentOrders; track o.id) {
                <tr>
                  <td>#{{ o.id }}</td>
                  <td>{{ o.customerName }} <span class="muted small">{{ typeLabel[o.type] }}</span></td>
                  <td><span class="badge s-{{ o.status }}">{{ statusLabel[o.status] }}</span></td>
                  <td class="num">{{ money(o.total) }}</td>
                  <td class="muted">{{ when(o.createdAt) }}</td>
                </tr>
              }
            </tbody>
          </table>
        } @else {
          <p class="muted">No orders yet. Try placing a test order with the bot in the <a routerLink="/simulator">Simulator</a>.</p>
        }
      </section>
    </div>
  `,
})
export class OverviewPage {
  private api = inject(Api);
  private toasts = inject(Toasts);
  stats = signal<Overview | null>(null);
  status = signal<Status | null>(null);
  money = money;
  when = when;
  statusLabel = ORDER_STATUS_LABEL;
  typeLabel = ORDER_TYPE_LABEL;
  private current = onRestaurant(rid => this.load(rid), 30);

  constructor() {
    this.api.status().then(s => this.status.set(s)).catch(e => this.toasts.error(errorText(e)));
  }

  async load(rid: number) {
    try { this.stats.set(await this.api.overview(rid)); } catch (e) { this.toasts.error(errorText(e)); }
  }

  refresh() {
    const id = this.current.id();
    if (id) this.load(id);
    this.api.status().then(s => this.status.set(s)).catch(e => this.toasts.error(errorText(e)));
  }

  copy(text: string) {
    navigator.clipboard?.writeText(text).then(() => this.toasts.ok('Copied'), () => this.toasts.error('Could not copy. Please select the text and copy it yourself'));
  }
}
