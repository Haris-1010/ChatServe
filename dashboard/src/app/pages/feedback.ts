import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Api, downloadCsv, FeedbackEntry, FeedbackReport, money, when } from '../core/api';
import { onRestaurant } from '../core/page';
import { errorText, Toasts } from '../core/toast';

@Component({
  selector: 'app-feedback',
  imports: [FormsModule],
  template: `
    <div class="page-head">
      <h1>Customer feedback</h1>
      <div class="row">
        <select class="inline" [ngModel]="days()" (ngModelChange)="days.set(+$event); load(rid())">
          <option [ngValue]="7">Last 7 days</option>
          <option [ngValue]="30">Last 30 days</option>
          <option [ngValue]="90">Last 3 months</option>
          <option [ngValue]="365">Last year</option>
        </select>
        <button class="btn ghost" (click)="exportCsv()" [disabled]="!report()?.feedback?.length">⬇ CSV (Excel)</button>
      </div>
    </div>

    @if (report(); as r) {
      <div class="stats">
        <div class="stat"><div class="stat-label">Average rating</div>
          <div class="stat-value">{{ r.average != null ? '⭐ ' + r.average : '—' }}</div>
          <div class="stat-sub">{{ r.count }} {{ r.count === 1 ? 'rating' : 'ratings' }} · {{ r.withComment }} with a comment</div></div>
        <div class="stat"><div class="stat-label">Customers said "received"</div>
          <div class="stat-value">{{ r.received }}</div>
          <div class="stat-sub">{{ r.deliveredOrders }} {{ r.deliveredOrders === 1 ? 'delivery' : 'deliveries' }} completed</div></div>
        <div class="stat dist">
          @for (s of r.stars; track s.stars) {
            <div class="dist-row">
              <span class="dist-label">{{ s.stars }} ⭐</span>
              <div class="bar"><div [style.width.%]="r.count ? s.count * 100 / r.count : 0" [class.low]="s.stars <= 2"></div></div>
              <span class="dist-count">{{ s.count }}</span>
            </div>
          }
        </div>
      </div>

      <section class="card flush">
        <table class="table">
          <thead><tr><th>Order</th><th>Rating</th><th>Comment</th><th class="hide-sm">Items</th><th>When</th></tr></thead>
          <tbody>
            @for (f of r.feedback; track f.id) {
              <tr [class.low-row]="f.rating <= 2">
                <td><b>#{{ f.id }}</b> {{ f.customerName }}<div class="muted small">+{{ f.phone }} · {{ money(f.total) }}</div></td>
                <td class="stars" [title]="f.rating + ' / 5'">{{ stars(f.rating) }}</td>
                <td>{{ f.feedbackComment || '—' }}</td>
                <td class="hide-sm muted small">{{ f.items.join(', ') }}</td>
                <td class="muted">{{ when(f.feedbackAt) }}</td>
              </tr>
            } @empty { <tr><td colspan="5" class="muted">No ratings yet in this period.</td></tr> }
          </tbody>
        </table>
      </section>
    }

    <p class="muted small">
      How it works: when the rider leaves or the order is marked done, the customer gets a <b>✅ Got my order</b> button
      (typing "mil gaya" or "received" works too). The order is then marked received and the bot asks for a 1 to 5 star rating,
      then for a comment. Ratings of 3 or less, comments and "⚠️ Problem" taps are also sent to the staff WhatsApp right away.
    </p>
  `,
  styles: [`
    .page-head .row select { width: auto; }
    .stats { display: grid; grid-template-columns: repeat(auto-fit, minmax(240px, 1fr)); gap: 12px; margin-bottom: 14px; }
    .dist { display: flex; flex-direction: column; gap: 4px; justify-content: center; }
    .dist-row { display: grid; grid-template-columns: 40px 1fr 32px; align-items: center; gap: 8px; font-size: 13px; }
    .dist-count { text-align: right; opacity: .8; }
    .bar { height: 8px; border-radius: 6px; background: rgba(127,127,127,.2); overflow: hidden; }
    .bar div { height: 100%; background: #f5a623; }
    .bar div.low { background: #e5484d; }
    .stars { white-space: nowrap; letter-spacing: 1px; }
    .low-row td:first-child { box-shadow: inset 3px 0 0 #e5484d; }
  `],
})
export class FeedbackPage {
  private api = inject(Api);
  private toasts = inject(Toasts);
  report = signal<FeedbackReport | null>(null);
  days = signal(30);
  money = money;
  when = when;
  private current = onRestaurant(rid => this.load(rid));
  rid = computed(() => this.current.id()!);

  async load(rid: number) {
    try { this.report.set(await this.api.feedback(rid, this.days())); } catch (e) { this.toasts.error(errorText(e)); }
  }

  stars(n: number) { return '⭐'.repeat(n); }

  exportCsv() {
    const list = this.report()?.feedback ?? [];
    downloadCsv(`feedback-${new Date().toISOString().slice(0, 10)}.csv`, [
      ['Order', 'Customer', 'Phone', 'Total', 'Rating', 'Comment', 'Items', 'When'],
      ...list.map((f: FeedbackEntry) => [f.id, f.customerName, f.phone, f.total, f.rating, f.feedbackComment ?? '',
        f.items.join(', '), new Date(f.feedbackAt).toLocaleString()]),
    ]);
  }
}
