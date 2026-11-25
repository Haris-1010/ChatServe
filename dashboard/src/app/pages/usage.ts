import { Component, inject, OnInit, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Api, UsageClient, UsageLine, UsageReport } from '../core/api';
import { Auth } from '../core/auth';
import { errorText, Toasts } from '../core/toast';

@Component({
  selector: 'app-usage',
  imports: [FormsModule, DecimalPipe],
  template: `
    <div class="page-head">
      <h1>AI usage</h1>
      <input type="month" [ngModel]="month()" (ngModelChange)="pick($event)" />
    </div>

    @if (report(); as r) {
      @if (auth.isAdmin()) {
        <div class="stats">
          <div class="stat"><div class="stat-label">All clients, {{ monthName(r.month) }}</div>
            <div class="stat-value">Rs {{ r.total.costPkr | number:'1.0-0' }}</div>
            <div class="stat-sub">$ {{ r.total.costUsd | number:'1.2-2' }} · {{ r.total.replies | number }} AI replies</div></div>
          <div class="stat"><div class="stat-label">Tokens</div>
            <div class="stat-value small-value">{{ tokens(r.total) | number }}</div>
            <div class="stat-sub">{{ r.total.inputTokens | number }} in · {{ r.total.outputTokens | number }} out</div></div>
        </div>
        <p class="muted small">Your Anthropic credit balance is at <b>console.anthropic.com → Billing</b>. Prices and the dollar rate are in Settings → AI cost.</p>
      }

      @for (c of r.clients; track c.id) {
        <div class="card client" [class.warn]="c.warning" [class.over]="c.overLimit">
          <div class="client-head">
            <div>
              <h2>{{ c.name }} @if (!c.isActive) { <span class="pill warn">Suspended</span> }</h2>
              <div class="muted small">{{ c.usage.replies | number }} AI replies · {{ tokens(c.usage) | number }} tokens · $ {{ c.usage.costUsd | number:'1.2-2' }}</div>
            </div>
            <div class="cost">
              <div class="big">Rs {{ c.usage.costPkr | number:'1.0-0' }}</div>
              @if (c.monthlyAiBudget) {
                <div class="small">of Rs {{ c.monthlyAiBudget | number:'1.0-0' }} · <b>Rs {{ c.remaining | number:'1.0-0' }} left</b></div>
              } @else {
                <div class="small muted">No monthly limit set</div>
              }
            </div>
          </div>

          @if (c.monthlyAiBudget) {
            <div class="bar"><div [style.width.%]="min(c.percent ?? 0, 100)"></div></div>
            <div class="small">
              {{ c.percent }}% used
              @if (c.overLimit) { · <b class="danger">Limit reached. The bot keeps replying; extra usage is charged as agreed.</b> }
              @else if (c.warning) { · <b class="warn-text">Close to the monthly limit.</b> }
            </div>
          }

          @if (c.restaurants.length > 1) {
            <table class="table small">
              <thead><tr><th>Restaurant</th><th>AI replies</th><th>Tokens</th><th>Cost</th></tr></thead>
              <tbody>
                @for (x of c.restaurants; track x.id) {
                  <tr><td>{{ x.name }}</td><td>{{ x.usage.replies | number }}</td><td>{{ tokens(x.usage) | number }}</td>
                    <td>Rs {{ x.usage.costPkr | number:'1.0-0' }}</td></tr>
                }
              </tbody>
            </table>
          }

          @if (auth.isAdmin()) {
            <form class="budget" (ngSubmit)="saveBudget(c)">
              <label class="field"><span>Monthly AI limit (Rs)</span>
                <input type="number" min="0" step="100" name="b{{ c.id }}" [(ngModel)]="budgets[c.id]" placeholder="e.g. 3000 (empty = no limit)" /></label>
              <button class="btn" [disabled]="busy()">Save limit</button>
            </form>
          }
        </div>
      } @empty {
        <div class="card empty muted">No clients yet.</div>
      }

      <p class="muted small">
        Costs are estimates from the tokens each AI reply used, at the prices in Settings. Reading training screenshots is included.
        The limit is only a warning: the bot never stops because of it.
      </p>
    }
  `,
  styles: [`
    .page-head { display: flex; justify-content: space-between; align-items: center; gap: 12px; flex-wrap: wrap; }
    .page-head input { width: auto; }
    .stats { display: grid; grid-template-columns: repeat(auto-fit, minmax(220px, 1fr)); gap: 12px; margin-bottom: 8px; }
    .client { margin-bottom: 14px; display: flex; flex-direction: column; gap: 10px; }
    .client.warn { border-color: rgba(245, 166, 35, .6); }
    .client.over { border-color: rgba(229, 72, 77, .7); }
    .client h2 { margin: 0 0 2px; font-size: 18px; }
    .client-head { display: flex; justify-content: space-between; gap: 12px; flex-wrap: wrap; }
    .cost { text-align: right; }
    .big { font-size: 22px; font-weight: 700; }
    .bar { height: 8px; border-radius: 6px; background: rgba(127,127,127,.2); overflow: hidden; }
    .bar div { height: 100%; background: var(--primary); }
    .client.warn .bar div { background: #f5a623; }
    .client.over .bar div { background: #e5484d; }
    .danger { color: #e5484d; }
    .warn-text { color: #d48806; }
    .budget { display: flex; gap: 10px; align-items: flex-end; flex-wrap: wrap; }
    .budget .field { flex: 1; min-width: 200px; margin: 0; }
    .table { width: 100%; border-collapse: collapse; }
    .table th, .table td { text-align: left; padding: 6px 8px; border-bottom: 1px solid rgba(127,127,127,.2); }
    @media (max-width: 600px) { .cost { text-align: left; } }
  `],
})
export class UsagePage implements OnInit {
  private api = inject(Api);
  private toasts = inject(Toasts);
  protected auth = inject(Auth);
  report = signal<UsageReport | null>(null);
  month = signal('');
  busy = signal(false);
  budgets: Record<number, number | null> = {};

  ngOnInit() { this.load(); }

  async load(month?: string) {
    try {
      const r = await this.api.usage(month);
      this.report.set(r);
      this.month.set(r.month);
      this.budgets = Object.fromEntries(r.clients.map(c => [c.id, c.monthlyAiBudget]));
    } catch (e) { this.toasts.error(errorText(e)); }
  }

  pick(month: string) { if (month) this.load(month); }

  async saveBudget(c: UsageClient) {
    this.busy.set(true);
    try {
      const v = this.budgets[c.id];
      await this.api.setBudget(c.id, v ? +v : null);
      this.toasts.ok(`Limit saved for ${c.name}.`);
      await this.load(this.month());
    } catch (e) {
      this.toasts.error(errorText(e));
    } finally {
      this.busy.set(false);
    }
  }

  tokens(u: UsageLine) { return u.inputTokens + u.outputTokens + u.cacheReadTokens + u.cacheWriteTokens; }
  min(a: number, b: number) { return Math.min(a, b); }
  monthName(m: string) {
    const [y, mo] = m.split('-').map(Number);
    return new Date(y, mo - 1, 1).toLocaleString('en', { month: 'long', year: 'numeric' });
  }
}
