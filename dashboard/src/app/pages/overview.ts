import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Api, money, ORDER_STATUS_LABEL, ORDER_TYPE_LABEL, Overview, Status, UsageCredit, when } from '../core/api';
import { Auth } from '../core/auth';
import { onRestaurant } from '../core/page';
import { errorText, Toasts } from '../core/toast';

@Component({
  selector: 'app-overview',
  imports: [RouterLink],
  template: `
    <div class="page-head">
      <h1>Home</h1>
      <button class="btn ghost" (click)="refresh()">↻ Refresh</button>
    </div>

    @if (stats(); as s) {
      <div class="stats">
        <div class="stat"><div class="stat-top"><span class="stat-icon">🧾</span><div class="stat-label">Orders today</div></div>
          <div class="stat-value">{{ s.ordersToday }}</div><div class="stat-sub">confirmed orders</div></div>
        <div class="stat"><div class="stat-top"><span class="stat-icon">💵</span><div class="stat-label">Sales today</div></div>
          <div class="stat-value">{{ money(s.revenueToday) }}</div><div class="stat-sub">from today's orders</div></div>
        <a class="stat link" routerLink="/orders"><div class="stat-top"><span class="stat-icon blue">🔥</span><div class="stat-label">Active orders</div></div>
          <div class="stat-value">{{ s.activeOrders }}</div><div class="stat-sub">{{ s.awaitingConfirmation }} awaiting confirmation</div></a>
        <a class="stat link" routerLink="/chats"><div class="stat-top"><span class="stat-icon blue">💬</span><div class="stat-label">Chats today</div></div>
          <div class="stat-value">{{ s.chatsToday }}</div><div class="stat-sub">{{ s.botRepliesToday }} bot replies · {{ s.pausedChats }} with staff</div></a>
        <a class="stat link" routerLink="/bookings"><div class="stat-top"><span class="stat-icon violet">📅</span><div class="stat-label">Upcoming bookings</div></div>
          <div class="stat-value">{{ s.upcomingBookings }}</div><div class="stat-sub">table bookings</div></a>
        <a class="stat link" routerLink="/customers"><div class="stat-top"><span class="stat-icon violet">👥</span><div class="stat-label">Customers</div></div>
          <div class="stat-value">{{ s.customers }}</div><div class="stat-sub">all time</div></a>
        <a class="stat link" routerLink="/feedback"><div class="stat-top"><span class="stat-icon amber">⭐</span><div class="stat-label">Rating (30 days)</div></div>
          <div class="stat-value">{{ s.averageRating30Days != null ? s.averageRating30Days + ' / 5' : '—' }}</div>
          <div class="stat-sub">{{ s.ratingCount30Days }} {{ s.ratingCount30Days === 1 ? 'rating' : 'ratings' }}</div></a>
        <a class="stat link" routerLink="/usage" [class.warn-stat]="s.monthlyReplyLimit && s.repliesLeft! <= s.monthlyReplyLimit * 0.2">
          <div class="stat-top"><span class="stat-icon">🤖</span><div class="stat-label">AI replies left this month</div></div>
          @if (s.monthlyReplyLimit) {
            <div class="stat-value">{{ s.repliesLeft!.toLocaleString() }}</div>
            <div class="stat-sub">{{ s.repliesThisMonth.toLocaleString() }} of {{ s.monthlyReplyLimit.toLocaleString() }} used</div>
            <div class="mini-bar"><div [style.width.%]="pct(s.repliesThisMonth, s.monthlyReplyLimit)"></div></div>
          } @else {
            <div class="stat-value">{{ s.repliesThisMonth.toLocaleString() }}</div>
            <div class="stat-sub">replies used · no monthly limit</div>
          }
        </a>
        @if (auth.isAdmin() && credit(); as cr) {
        <a class="stat link" routerLink="/usage" [class.warn-stat]="cr.percent <= 20">
          <div class="stat-top"><span class="stat-icon amber">🪙</span><div class="stat-label">Anthropic credit left</div></div>
          <div class="stat-value">{{ cr.tokensLeft != null ? compact(cr.tokensLeft) : '$ ' + cr.remainingUsd.toFixed(2) }}</div>
          <div class="stat-sub">tokens · $ {{ cr.remainingUsd.toFixed(2) }} of $ {{ cr.balanceUsd.toFixed(2) }}@if (cr.repliesLeft != null) { · ~{{ cr.repliesLeft.toLocaleString() }} replies }</div>
          <div class="mini-bar"><div [style.width.%]="cr.percent"></div></div>
        </a>
        }
        @if (auth.isAdmin()) {
        <div class="stat"><div class="stat-top"><span class="stat-icon violet">🔢</span><div class="stat-label">AI tokens today</div></div>
          <div class="stat-value">{{ compact(s.inputTokensToday + s.outputTokensToday) }}</div>
          <div class="stat-sub">{{ s.inputTokensToday.toLocaleString() }} in · {{ s.outputTokensToday.toLocaleString() }} out</div></div>
        }
      </div>
    }

    <div class="grid-2">
      @if (auth.isAdmin()) {
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
      }

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
          <p class="muted">No orders yet. Try placing a test order with the bot in <a routerLink="/simulator">Test the bot</a>.</p>
        }
      </section>
    </div>
  `,
})
export class OverviewPage {
  private api = inject(Api);
  private toasts = inject(Toasts);
  stats = signal<Overview | null>(null);
  credit = signal<UsageCredit | null>(null);
  status = signal<Status | null>(null);
  money = money;
  when = when;
  statusLabel = ORDER_STATUS_LABEL;
  typeLabel = ORDER_TYPE_LABEL;
  private current = onRestaurant(rid => this.load(rid), 30);

  protected auth = inject(Auth);

  constructor() {
    this.loadStatus();
  }

  /** The setup checklist (keys, webhook) is only for the admin. */
  private loadStatus() {
    if (!this.auth.isAdmin()) return;
    this.api.usage().then(u => this.credit.set(u.credit)).catch(() => { /* optional card */ });
    this.api.status().then(s => this.status.set(s)).catch(e => this.toasts.error(errorText(e)));
  }

  async load(rid: number) {
    try { this.stats.set(await this.api.overview(rid)); } catch (e) { this.toasts.error(errorText(e)); }
  }

  refresh() {
    const id = this.current.id();
    if (id) this.load(id);
    this.loadStatus();
  }

  /** 80,387,276 → "80.4M" */
  compact(n: number) { return new Intl.NumberFormat('en', { notation: 'compact', maximumFractionDigits: 1 }).format(n); }

  pct(a: number, b: number) { return Math.min(100, b ? a * 100 / b : 0); }

  copy(text: string) {
    navigator.clipboard?.writeText(text).then(() => this.toasts.ok('Copied'), () => this.toasts.error('Could not copy. Please select the text and copy it yourself'));
  }
}
