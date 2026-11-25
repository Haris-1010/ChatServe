import { Component, inject, OnInit, signal } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Api, UsageClient, UsageLine, UsageReport } from '../core/api';
import { Auth } from '../core/auth';
import { errorText, Toasts } from '../core/toast';

@Component({
  selector: 'app-usage',
  imports: [FormsModule, DecimalPipe, DatePipe],
  template: `
    <div class="page-head">
      <h1>AI usage</h1>
      <input type="month" [ngModel]="month()" (ngModelChange)="pick($event)" />
    </div>

    @if (report(); as r) {
      @if (r.showCost) {
        <!-- ===== Admin: totals, credit, all clients side by side ===== -->
        <div class="stats">
          <div class="stat"><div class="stat-label">All clients, {{ monthName(r.month) }}</div>
            <div class="stat-value">Rs {{ r.total.costPkr | number:'1.0-0' }}</div>
            <div class="stat-sub">$ {{ r.total.costUsd | number:'1.2-4' }} · {{ r.total.replies | number }} AI replies</div></div>
          <div class="stat"><div class="stat-label">Tokens used</div>
            <div class="stat-value small-value">{{ tokens(r.total) | number }}</div>
            <div class="stat-sub">{{ r.total.inputTokens | number }} in · {{ r.total.outputTokens | number }} out ·
              {{ r.total.cacheReadTokens + r.total.cacheWriteTokens | number }} cache</div></div>
          <div class="stat"><div class="stat-label">Average per reply</div>
            <div class="stat-value small-value">{{ r.total.replies ? (tokens(r.total) / r.total.replies | number:'1.0-0') : '—' }} tokens</div>
            <div class="stat-sub">Rs {{ r.total.replies ? (r.total.costPkr / r.total.replies | number:'1.2-2') : '—' }} per reply</div></div>
        </div>

        <div class="card credit" [class.warn]="r.credit && r.credit.percent <= 20" [class.over]="r.credit && r.credit.remainingUsd <= 0">
          <h2>Anthropic credit left</h2>
          @if (r.credit; as cr) {
            <div class="credit-row">
              <div>
                <div class="big">$ {{ cr.remainingUsd | number:'1.2-2' }} <span class="of">/ $ {{ cr.balanceUsd | number:'1.2-2' }}</span></div>
                <div class="small muted">Rs {{ cr.remainingPkr | number:'1.0-0' }} · {{ cr.percent }}% left</div>
              </div>
              <div>
                <div class="big">{{ cr.tokensLeft != null ? (cr.tokensLeft | number) : '—' }} <span class="of">tokens left</span></div>
                <div class="small muted">
                  @if (cr.repliesLeft != null) { about {{ cr.repliesLeft | number }} more AI replies (all clients together) } @else { no AI use yet to estimate from }
                </div>
              </div>
            </div>
            <div class="bar"><div [style.width.%]="min(cr.percent, 100)"></div></div>
            <div class="small muted">
              Balance entered {{ cr.setAt | date:'d MMM y, h:mm a' }}. Used since then: $ {{ cr.spentUsd | number:'1.2-4' }}
              ({{ cr.spentTokens | number }} tokens). Tokens and replies left are estimates at the average cost so far.
            </div>
          } @else {
            <div class="small muted">Enter your balance to see how much is left. Anthropic doesn't share the balance through the API.</div>
          }
          <form class="budget" (ngSubmit)="saveCredit()">
            <label class="field"><span>Current balance from console.anthropic.com → Billing ($)</span>
              <input type="number" min="0" step="0.01" name="credit" [(ngModel)]="creditInput" placeholder="e.g. 5.00" /></label>
            <button class="btn" [disabled]="busy()">Save balance</button>
          </form>
        </div>
        <p class="muted small">After a top-up, enter the new balance again. Prices and the dollar rate are in Settings → AI cost.</p>

        @if (r.clients.length) {
          <section class="card flush compare">
            <h2>Clients this month</h2>
            <div class="scroll">
              <table class="table">
                <thead><tr>
                  <th>Client</th><th class="num">Replies</th><th class="num">Left</th><th class="num">Per day</th>
                  <th class="num" title="At this month's pace">Month end (est.)</th><th class="num">Tokens</th>
                  <th class="num">Tokens / reply</th><th class="num">Cost</th><th class="num">Share</th>
                </tr></thead>
                <tbody>
                  @for (c of r.clients; track c.id) {
                    <tr [class.warn-row]="c.warning" [class.over-row]="c.overLimit">
                      <td><b>{{ c.name }}</b></td>
                      <td class="num">{{ c.usage.replies | number }}@if (c.monthlyReplyLimit) { <span class="muted"> / {{ c.monthlyReplyLimit | number }}</span> }</td>
                      <td class="num">{{ c.remainingReplies != null ? (c.remainingReplies | number) : '—' }}</td>
                      <td class="num">{{ c.repliesPerDay | number:'1.0-1' }}</td>
                      <td class="num" [class.danger]="c.monthlyReplyLimit && c.projectedReplies > c.monthlyReplyLimit">{{ c.projectedReplies | number }}</td>
                      <td class="num">{{ c.totalTokens | number }}</td>
                      <td class="num">{{ c.avgTokensPerReply | number }}</td>
                      <td class="num">Rs {{ c.usage.costPkr | number:'1.0-0' }}</td>
                      <td class="num">{{ c.sharePercent ?? 0 }}%</td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          </section>
        }
      }

      <!-- ===== One card per client (an owner sees only their own) ===== -->
      @for (c of r.clients; track c.id) {
        <div class="card client" [class.warn]="c.warning" [class.over]="c.overLimit">
          <div class="client-head">
            <div>
              <h2>{{ c.name }} @if (!c.isActive) { <span class="pill warn">Suspended</span> }</h2>
              @if (r.showCost) {
                <div class="muted small">AI cost Rs {{ c.usage.costPkr | number:'1.0-0' }} · $ {{ c.usage.costUsd | number:'1.2-4' }} ·
                  {{ c.totalTokens | number }} tokens ({{ c.usage.inputTokens | number }} in · {{ c.usage.outputTokens | number }} out ·
                  {{ c.usage.cacheReadTokens + c.usage.cacheWriteTokens | number }} cache) · {{ c.avgTokensPerReply | number }} tokens / reply</div>
              } @else {
                <div class="muted small">{{ monthName(r.month) }}</div>
              }
            </div>
            <div class="cost">
              @if (c.monthlyReplyLimit) {
                <div class="big">{{ c.remainingReplies | number }} <span class="of">replies left</span></div>
                <div class="small">{{ c.usage.replies | number }} of {{ c.monthlyReplyLimit | number }} used</div>
              } @else {
                <div class="big">{{ c.usage.replies | number }} <span class="of">replies</span></div>
                <div class="small muted">No monthly limit set</div>
              }
            </div>
          </div>

          @if (c.monthlyReplyLimit) {
            <div class="bar"><div [style.width.%]="min(c.percent ?? 0, 100)"></div></div>
            <div class="small">
              {{ c.percent }}% used
              @if (c.overLimit) { · <b class="danger">This month's replies are used up. The bot keeps replying.</b> }
              @else if (c.warning) { · <b class="warn-text">Close to this month's limit.</b> }
            </div>
          }
          <div class="small muted">
            About {{ c.repliesPerDay | number:'1.0-1' }} replies a day.
            @if (c.monthlyReplyLimit && c.projectedReplies > c.monthlyReplyLimit) {
              <b class="warn-text">At this pace about {{ c.projectedReplies | number }} by month end, more than the {{ c.monthlyReplyLimit | number }} in the package.</b>
            } @else if (c.usage.replies) {
              At this pace about {{ c.projectedReplies | number }} by month end.
            }
          </div>

          @if (c.usage.replies) {
            <div class="days" [attr.aria-label]="'Replies per day for ' + c.name">
              @for (d of c.daily; track d.date) {
                <div class="day" [title]="dayTitle(d.date, d.usage, r.showCost)">
                  <div class="col" [style.height.%]="dayHeight(c, d.usage.replies)"></div>
                  <span class="day-n">{{ dayNum(d.date) }}</span>
                </div>
              }
            </div>
            <button class="btn ghost sm" type="button" (click)="toggle(c.id)">{{ open() === c.id ? 'Hide' : 'Show' }} day by day</button>
            @if (open() === c.id) {
              <table class="table small">
                <thead><tr><th>Day</th><th class="num">AI replies</th>
                  @if (r.showCost) { <th class="num">Tokens</th><th class="num">Cost</th> }</tr></thead>
                <tbody>
                  @for (d of reversed(c.daily); track d.date) {
                    @if (d.usage.replies || tokens(d.usage)) {
                      <tr><td>{{ d.date | date:'EEE d MMM' }}</td><td class="num">{{ d.usage.replies | number }}</td>
                        @if (r.showCost) { <td class="num">{{ tokens(d.usage) | number }}</td><td class="num">Rs {{ d.usage.costPkr | number:'1.0-0' }}</td> }</tr>
                    }
                  }
                </tbody>
              </table>
            }
          }

          @if (c.restaurants.length > 1) {
            <table class="table small">
              <thead><tr><th>Restaurant</th><th class="num">AI replies</th>
                @if (r.showCost) { <th class="num">Tokens</th><th class="num">Cost</th> }</tr></thead>
              <tbody>
                @for (x of c.restaurants; track x.id) {
                  <tr><td>{{ x.name }}</td><td class="num">{{ x.usage.replies | number }}</td>
                    @if (r.showCost) { <td class="num">{{ tokens(x.usage) | number }}</td><td class="num">Rs {{ x.usage.costPkr | number:'1.0-0' }}</td> }</tr>
                }
              </tbody>
            </table>
          }

          @if (auth.isAdmin()) {
            <form class="budget" (ngSubmit)="saveBudget(c)">
              <label class="field"><span>AI replies per month (package)</span>
                <input type="number" min="0" step="100" name="b{{ c.id }}" [(ngModel)]="budgets[c.id]" placeholder="e.g. 3000 (empty = no limit)" /></label>
              <button class="btn" [disabled]="busy()">Save limit</button>
            </form>
          }
        </div>
      } @empty {
        <div class="card empty muted">No clients yet.</div>
      }

      <p class="muted small">
        One reply = one AI answer to a customer message. Fixed messages (order confirmed, status updates, rating questions) don't count.
        @if (r.showCost) { Costs are estimates from the tokens used, at the prices in Settings (reading training screenshots is included). }
        The limit is only a warning: the bot never stops because of it.
      </p>
    }
  `,
  styles: [`
    .page-head { display: flex; justify-content: space-between; align-items: center; gap: 12px; flex-wrap: wrap; }
    .page-head input { width: auto; }
    .stats { display: grid; grid-template-columns: repeat(auto-fit, minmax(220px, 1fr)); gap: 12px; margin-bottom: 8px; }
    .credit { margin: 6px 0 8px; display: flex; flex-direction: column; gap: 10px; }
    .credit h2, .compare h2 { margin: 0; font-size: 18px; }
    .compare h2 { padding: 14px 16px 6px; }
    .compare { margin-bottom: 14px; }
    .scroll { overflow-x: auto; }
    .compare .table { min-width: 760px; }
    .warn-row td:first-child { box-shadow: inset 3px 0 0 #f5a623; }
    .over-row td:first-child { box-shadow: inset 3px 0 0 #e5484d; }
    .credit.warn { border-color: rgba(245, 166, 35, .6); }
    .credit.warn .bar div { background: #f5a623; }
    .credit.over { border-color: rgba(229, 72, 77, .7); }
    .credit.over .bar div { background: #e5484d; }
    .credit-row { display: grid; grid-template-columns: repeat(auto-fit, minmax(220px, 1fr)); gap: 12px; }
    .client { margin-bottom: 14px; display: flex; flex-direction: column; gap: 10px; }
    .client.warn { border-color: rgba(245, 166, 35, .6); }
    .client.over { border-color: rgba(229, 72, 77, .7); }
    .client h2 { margin: 0 0 2px; font-size: 18px; }
    .client-head { display: flex; justify-content: space-between; gap: 12px; flex-wrap: wrap; }
    .cost { text-align: right; }
    .big { font-size: 22px; font-weight: 700; }
    .big .of { font-size: 14px; font-weight: 500; opacity: .7; }
    .bar { height: 8px; border-radius: 6px; background: rgba(127,127,127,.2); overflow: hidden; }
    .bar div { height: 100%; background: var(--primary); }
    .client.warn .bar div { background: #f5a623; }
    .client.over .bar div { background: #e5484d; }
    .days { display: flex; align-items: flex-end; gap: 2px; height: 90px; padding-top: 4px; }
    .day { flex: 1; min-width: 0; height: 100%; display: flex; flex-direction: column; justify-content: flex-end; align-items: center; }
    .day .col { width: 100%; max-width: 18px; min-height: 1px; border-radius: 3px 3px 0 0; background: var(--primary); opacity: .85; }
    .day:hover .col { opacity: 1; }
    .day-n { font-size: 9px; opacity: .55; margin-top: 2px; }
    .btn.sm { align-self: flex-start; }
    .danger { color: #e5484d; }
    .warn-text { color: #d48806; }
    .budget { display: flex; gap: 10px; align-items: flex-end; flex-wrap: wrap; }
    .budget .field { flex: 1; min-width: 200px; margin: 0; }
    .table { width: 100%; border-collapse: collapse; }
    .table th, .table td { text-align: left; padding: 6px 8px; border-bottom: 1px solid rgba(127,127,127,.2); }
    .table .num { text-align: right; white-space: nowrap; }
    @media (max-width: 600px) { .cost { text-align: left; } .day-n { display: none; } }
  `],
})
export class UsagePage implements OnInit {
  private api = inject(Api);
  private toasts = inject(Toasts);
  protected auth = inject(Auth);
  report = signal<UsageReport | null>(null);
  month = signal('');
  busy = signal(false);
  open = signal<number | null>(null);
  budgets: Record<number, number | null> = {};
  creditInput: number | null = null;

  ngOnInit() { this.load(); }

  async load(month?: string) {
    try {
      const r = await this.api.usage(month);
      this.report.set(r);
      this.month.set(r.month);
      this.budgets = Object.fromEntries(r.clients.map(c => [c.id, c.monthlyReplyLimit]));
    } catch (e) { this.toasts.error(errorText(e)); }
  }

  pick(month: string) { if (month) this.load(month); }

  async saveCredit() {
    this.busy.set(true);
    try {
      const v = this.creditInput;
      await this.api.setCredit(v == null || (v as unknown) === '' ? null : +v);
      this.toasts.ok('Balance saved.');
      this.creditInput = null;
      await this.load(this.month());
    } catch (e) {
      this.toasts.error(errorText(e));
    } finally {
      this.busy.set(false);
    }
  }

  async saveBudget(c: UsageClient) {
    this.busy.set(true);
    try {
      const v = this.budgets[c.id];
      await this.api.setReplyLimit(c.id, v ? Math.round(+v) : null);
      this.toasts.ok(`Limit saved for ${c.name}.`);
      await this.load(this.month());
    } catch (e) {
      this.toasts.error(errorText(e));
    } finally {
      this.busy.set(false);
    }
  }

  toggle(id: number) { this.open.set(this.open() === id ? null : id); }
  reversed<T>(a: T[]) { return [...a].reverse(); }
  dayNum(d: string) { return +d.slice(8); }
  dayHeight(c: UsageClient, n: number) {
    const max = Math.max(1, ...c.daily.map(d => d.usage.replies));
    return n ? Math.max(4, n * 100 / max) : 0;
  }
  dayTitle(date: string, u: UsageLine, cost: boolean) {
    const day = new Date(date + 'T00:00:00').toLocaleDateString('en', { weekday: 'short', day: 'numeric', month: 'short' });
    return `${day}: ${u.replies} replies` + (cost ? ` · ${this.tokens(u).toLocaleString()} tokens · Rs ${Math.round(u.costPkr)}` : '');
  }
  tokens(u: UsageLine) { return u.inputTokens + u.outputTokens + u.cacheReadTokens + u.cacheWriteTokens; }
  min(a: number, b: number) { return Math.min(a, b); }
  monthName(m: string) {
    const [y, mo] = m.split('-').map(Number);
    return new Date(y, mo - 1, 1).toLocaleString('en', { month: 'long', year: 'numeric' });
  }
}
