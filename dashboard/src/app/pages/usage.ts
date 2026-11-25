import { Component, inject, OnInit, signal } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Api, ReplyRequest, ReplyRequestKind, UsageClient, UsageLine, UsageReport } from '../core/api';
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
          <div class="stat"><div class="stat-top"><span class="stat-icon">💰</span><div class="stat-label">AI cost, {{ monthName(r.month) }}</div></div>
            <div class="stat-value">Rs {{ r.total.costPkr | number:'1.0-0' }}</div>
            <div class="stat-sub">$ {{ r.total.costUsd | number:'1.2-4' }} · all clients</div></div>
          <div class="stat"><div class="stat-top"><span class="stat-icon blue">💬</span><div class="stat-label">AI replies</div></div>
            <div class="stat-value">{{ r.total.replies | number }}</div>
            <div class="stat-sub">{{ r.clients.length }} {{ r.clients.length === 1 ? 'client' : 'clients' }}</div></div>
          <div class="stat"><div class="stat-top"><span class="stat-icon violet">🔢</span><div class="stat-label">Tokens used</div></div>
            <div class="stat-value">{{ tokens(r.total) | number }}</div>
            <div class="stat-sub">{{ r.total.inputTokens | number }} in · {{ r.total.outputTokens | number }} out ·
              {{ r.total.cacheReadTokens + r.total.cacheWriteTokens | number }} cache</div></div>
          <div class="stat"><div class="stat-top"><span class="stat-icon amber">⚖️</span><div class="stat-label">Average per reply</div></div>
            <div class="stat-value">{{ r.total.replies ? (tokens(r.total) / r.total.replies | number:'1.0-0') : '—' }}</div>
            <div class="stat-sub">tokens · Rs {{ r.total.replies ? (r.total.costPkr / r.total.replies | number:'1.2-2') : '—' }} per reply</div></div>
        </div>

        @if (pending().length) {
          <section class="card requests">
            <div class="card-head"><h2>📩 Reply requests</h2><span class="pill warn">{{ pending().length }} waiting</span></div>
            @for (q of pending(); track q.id) {
              <div class="request">
                <div class="request-main">
                  <div><b>{{ q.clientName }}</b> asks for <b>{{ q.replies | number }}</b> more replies
                    <span class="pill" [class.ok]="q.kind === 'Package'">{{ q.kind === 'TopUp' ? 'This month only' : 'Every month' }}</span></div>
                  <div class="muted small">{{ q.createdAt | date:'d MMM, h:mm a' }} · by {{ q.requestedBy }}@if (q.note) { · "{{ q.note }}" }</div>
                </div>
                <div class="request-actions">
                  <input type="number" min="1" class="w-num" [(ngModel)]="approveAmount[q.id]" [name]="'a' + q.id" title="Replies to give" />
                  <input class="w-note" [(ngModel)]="adminNote[q.id]" [name]="'n' + q.id" placeholder="Note to client (optional)" />
                  <button class="btn primary sm" (click)="approve(q)" [disabled]="busy()">Approve</button>
                  <button class="btn danger-ghost sm" (click)="reject(q)" [disabled]="busy()">Reject</button>
                </div>
              </div>
            }
          </section>
        }

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
            @if (c.extraReplies) {
              <div class="small muted">Package {{ c.packageReplies | number }} + {{ c.extraReplies | number }} extra this month</div>
            }
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
            <div class="limits">
              <div class="limit-box">
                <div class="limit-title">Package (every month)</div>
                <div class="stepper">
                  <button class="btn ghost sm" type="button" (click)="step(c, -500)">−500</button>
                  <button class="btn ghost sm" type="button" (click)="step(c, -100)">−100</button>
                  <input type="number" min="0" step="100" [name]="'b' + c.id" [(ngModel)]="budgets[c.id]" placeholder="No limit" />
                  <button class="btn ghost sm" type="button" (click)="step(c, 100)">+100</button>
                  <button class="btn ghost sm" type="button" (click)="step(c, 500)">+500</button>
                </div>
                <button class="btn primary sm" type="button" (click)="saveBudget(c)" [disabled]="busy()">Save package</button>
                <div class="muted small">Empty = no limit. Changes every month from now on.</div>
              </div>
              <div class="limit-box">
                <div class="limit-title">Extra for this month only</div>
                <div class="stepper">
                  @for (n of [100, 500, 1000]; track n) {
                    <button class="btn ghost sm" type="button" (click)="topUp(c, n)" [disabled]="busy() || !c.packageReplies">+{{ n | number }}</button>
                  }
                  <button class="btn ghost sm" type="button" (click)="topUp(c, -100)" [disabled]="busy() || !c.extraReplies">−100</button>
                </div>
                <div class="muted small">{{ c.extraReplies ? (c.extraReplies | number) + ' extra this month.' : 'None this month.' }} Resets next month.</div>
              </div>
            </div>
          } @else {
            <!-- Client: ask for more replies -->
            @if (myPending(); as q) {
              <div class="ask waiting">
                ⏳ You asked for <b>{{ q.replies | number }}</b> more replies ({{ q.kind === 'TopUp' ? 'this month' : 'every month' }}) on {{ q.createdAt | date:'d MMM' }}.
                Waiting for approval.
                <button class="link small" type="button" (click)="withdraw(q)">Withdraw</button>
              </div>
            } @else if (asking()) {
              <form class="ask" (ngSubmit)="sendRequest()">
                <div class="limit-title">How many more replies do you need?</div>
                <div class="chips">
                  @for (n of [250, 500, 1000, 2000]; track n) {
                    <button type="button" class="chip" [class.on]="askReplies === n" (click)="askReplies = n">{{ n | number }}</button>
                  }
                  <input type="number" min="1" class="w-num" name="askN" [(ngModel)]="askReplies" />
                </div>
                <div class="chips">
                  <button type="button" class="chip" [class.on]="askKind === 'TopUp'" (click)="askKind = 'TopUp'">Only this month</button>
                  <button type="button" class="chip" [class.on]="askKind === 'Package'" (click)="askKind = 'Package'">Every month (bigger package)</button>
                </div>
                <input name="askNote" [(ngModel)]="askNote" placeholder="Note (optional), e.g. Eid rush" maxlength="500" />
                <div class="row">
                  <button class="btn primary" [disabled]="busy() || !askReplies">Send request</button>
                  <button class="btn ghost" type="button" (click)="asking.set(false)">Cancel</button>
                </div>
              </form>
            } @else {
              <div class="row">
                <button class="btn primary" type="button" (click)="asking.set(true)">＋ Request more replies</button>
                <span class="muted small">We'll review it and you'll see the answer here.</span>
              </div>
            }
            @if (myHistory().length) {
              <div class="history">
                @for (q of myHistory(); track q.id) {
                  <div class="small">
                    <span class="pill" [class.ok]="q.status === 'Approved'" [class.warn]="q.status === 'Rejected'">{{ q.status }}</span>
                    {{ q.replies | number }} replies ({{ q.kind === 'TopUp' ? 'this month' : 'every month' }}), {{ q.createdAt | date:'d MMM' }}
                    @if (q.status === 'Approved' && q.approvedReplies !== q.replies) { · {{ q.approvedReplies | number }} given }
                    @if (q.adminNote) { · <span class="muted">"{{ q.adminNote }}"</span> }
                  </div>
                }
              </div>
            }
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
    .client { margin-bottom: 14px; display: flex; flex-direction: column; gap: 12px; }
    .requests { display: flex; flex-direction: column; gap: 4px; }
    .request { display: flex; justify-content: space-between; gap: 12px; flex-wrap: wrap; align-items: center; padding: 12px 0; border-top: 1px solid var(--border); }
    .request-main { flex: 1 1 280px; display: flex; flex-direction: column; gap: 2px; }
    .request-main .pill { margin-left: 6px; }
    .request-actions { display: flex; gap: 6px; align-items: center; flex-wrap: wrap; }
    .w-num { width: 100px; }
    .w-note { width: 200px; }
    .limits { display: grid; grid-template-columns: repeat(auto-fit, minmax(280px, 1fr)); gap: 12px; }
    .limit-box { display: flex; flex-direction: column; gap: 8px; align-items: flex-start; padding: 14px; border-radius: 12px;
      background: var(--surface-2); border: 1px solid var(--border); }
    .limit-title { font-weight: 600; font-size: 13px; }
    .stepper { display: flex; gap: 6px; align-items: center; flex-wrap: wrap; }
    .stepper input { width: 110px; text-align: center; }
    .ask { display: flex; flex-direction: column; gap: 10px; padding: 14px; border-radius: 12px; background: var(--surface-2); border: 1px solid var(--border); }
    .ask.waiting { display: block; background: var(--info-soft); border-color: transparent; }
    .chips { display: flex; gap: 6px; flex-wrap: wrap; align-items: center; }
    .chip { font: inherit; font-size: 13px; padding: 6px 12px; border-radius: 999px; border: 1px solid var(--border); background: var(--surface); color: var(--text); cursor: pointer; transition: .15s; }
    .chip:hover { border-color: var(--primary); }
    .chip.on { background: var(--primary-soft); border-color: var(--primary); color: var(--primary-strong); font-weight: 600; }
    .history { display: flex; flex-direction: column; gap: 6px; }
    .client.warn { border-color: rgba(245, 166, 35, .6); }
    .client.over { border-color: rgba(229, 72, 77, .7); }
    .client h2 { margin: 0 0 2px; font-size: 18px; }
    .client-head { display: flex; justify-content: space-between; gap: 12px; flex-wrap: wrap; }
    .cost { text-align: right; }
    .big { font-size: 22px; font-weight: 700; }
    .big .of { font-size: 14px; font-weight: 500; opacity: .7; }
    .bar { height: 10px; border-radius: 6px; background: var(--surface-2); border: 1px solid var(--border); overflow: hidden; }
    .bar div { transition: width .4s ease; border-radius: 6px; }
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
  requests = signal<ReplyRequest[]>([]);
  pending = signal<ReplyRequest[]>([]);
  approveAmount: Record<number, number> = {};
  adminNote: Record<number, string> = {};
  asking = signal(false);
  askReplies = 500;
  askKind: ReplyRequestKind = 'TopUp';
  askNote = '';

  myPending() { return this.requests().find(q => q.status === 'Pending') ?? null; }
  myHistory() { return this.requests().filter(q => q.status !== 'Pending').slice(0, 5); }

  async loadRequests() {
    try {
      const list = await this.api.replyRequests();
      this.requests.set(list);
      const p = list.filter(q => q.status === 'Pending');
      this.pending.set(this.auth.isAdmin() ? p : []);
      for (const q of p) { this.approveAmount[q.id] ??= q.replies; }
    } catch (e) { this.toasts.error(errorText(e)); }
  }

  private async run(action: () => Promise<unknown>, ok: string) {
    this.busy.set(true);
    try {
      await action();
      this.toasts.ok(ok);
      await Promise.all([this.load(this.month()), this.loadRequests()]);
    } catch (e) {
      this.toasts.error(errorText(e));
    } finally {
      this.busy.set(false);
    }
  }

  sendRequest() {
    return this.run(() => this.api.requestReplies(Math.round(+this.askReplies), this.askKind, this.askNote), 'Request sent.')
      .then(() => { this.asking.set(false); this.askNote = ''; });
  }
  withdraw(q: ReplyRequest) { return this.run(() => this.api.withdrawRequest(q.id), 'Request withdrawn.'); }
  approve(q: ReplyRequest) {
    const n = Math.round(+this.approveAmount[q.id] || q.replies);
    return this.run(() => this.api.approveRequest(q.id, n, this.adminNote[q.id] ?? ''), `Approved ${n.toLocaleString()} replies for ${q.clientName}.`);
  }
  reject(q: ReplyRequest) { return this.run(() => this.api.rejectRequest(q.id, this.adminNote[q.id] ?? ''), 'Request rejected.'); }
  topUp(c: UsageClient, n: number) {
    return this.run(() => this.api.topUp(c.id, n), n > 0 ? `Added ${n.toLocaleString()} replies for this month.` : `Removed ${(-n).toLocaleString()} extra replies.`);
  }
  step(c: UsageClient, n: number) { this.budgets[c.id] = Math.max(0, (+(this.budgets[c.id] ?? 0) || 0) + n); }

  ngOnInit() { this.load(); this.loadRequests(); }

  async load(month?: string) {
    try {
      const r = await this.api.usage(month);
      this.report.set(r);
      this.month.set(r.month);
      this.budgets = Object.fromEntries(r.clients.map(c => [c.id, c.packageReplies]));
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
