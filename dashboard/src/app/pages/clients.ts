import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { Api, Client, ClientBranch, ClientLogin, ClientsResponse, CurrentRestaurant, money, randomPassword, UsageReport, when } from '../core/api';
import { errorText, Toasts } from '../core/toast';

/**
 * Admin home: every client (business) with its restaurants, today's numbers and logins.
 * A client's login only ever sees that client's restaurants.
 */
@Component({
  selector: 'app-clients',
  imports: [FormsModule, RouterLink],
  template: `
    <div class="page-head">
      <h1>All clients</h1>
      <div class="row">
        <button class="btn ghost" (click)="load()">↻ Refresh</button>
        <button class="btn primary" (click)="showNew.set(!showNew())">{{ showNew() ? 'Close' : '+ New client' }}</button>
      </div>
    </div>

    @if (data(); as d) {
      <div class="stats">
        <div class="stat"><div class="stat-label">Clients</div><div class="stat-value">{{ d.totals.clientCount }}</div>
          <div class="stat-sub">{{ d.totals.activeClients }} active</div></div>
        <div class="stat"><div class="stat-label">Restaurants</div><div class="stat-value">{{ d.totals.restaurants }}</div></div>
        <div class="stat"><div class="stat-label">Orders today</div><div class="stat-value">{{ d.totals.ordersToday }}</div></div>
        <div class="stat"><div class="stat-label">Sales today</div><div class="stat-value">{{ money(d.totals.salesToday) }}</div></div>
        <div class="stat"><div class="stat-label">Chats today</div><div class="stat-value">{{ d.totals.chatsToday }}</div></div>
        @if (usage(); as u) {
          <!-- AI cost of every client together, and what the Anthropic credit still covers -->
          <a class="stat link" routerLink="/usage">
            <div class="stat-label">AI cost this month</div>
            <div class="stat-value">{{ money(u.total.costPkr) }}</div>
            <div class="stat-sub">{{ u.total.replies.toLocaleString('en-PK') }} replies · \${{ u.total.costUsd.toFixed(2) }}
              @if (u.credit; as c) { · since top-up {{ money(c.spentUsd * u.pkrPerUsd) }} }</div>
          </a>
          <a class="stat link" routerLink="/usage" [class.warn-stat]="u.credit && u.credit.percent <= 20">
            <div class="stat-label">Replies left (AI credit)</div>
            @if (u.credit; as c) {
              <div class="stat-value">{{ c.repliesLeft === null ? '—' : '~' + c.repliesLeft.toLocaleString('en-PK') }}</div>
              <div class="stat-sub">\${{ c.remainingUsd.toFixed(2) }} ({{ money(c.remainingPkr) }}) of \${{ c.balanceUsd.toFixed(2) }} left</div>
            } @else {
              <div class="stat-value">—</div>
              <div class="stat-sub">Enter your Anthropic balance on the AI usage page</div>
            }
          </a>
        }
      </div>
    }

    @if (showNew()) {
      <form class="card form-col" (ngSubmit)="createClient()">
        <h2>New client</h2>
        <div class="steps">
          <div class="step">
            <div class="step-n">1</div>
            <div class="form-col">
              <b>Business</b>
              <label class="field"><span>Business name *</span><input name="cn" [(ngModel)]="nc.name" placeholder="e.g. Butt Foods" /></label>
            </div>
          </div>
          <div class="step">
            <div class="step-n">2</div>
            <div class="form-col">
              <b>First restaurant <span class="muted small">(optional, you can add more later)</span></b>
              <label class="field"><span>Restaurant / branch name</span><input name="rn" [(ngModel)]="nc.restaurantName" [placeholder]="nc.name || 'Same as business name'" /></label>
              <label class="field"><span>WhatsApp Phone number ID</span><input name="pn" [(ngModel)]="nc.whatsAppPhoneNumberId" placeholder="From Meta > WhatsApp Manager > Phone numbers" /></label>
            </div>
          </div>
          <div class="step">
            <div class="step-n">3</div>
            <div class="form-col">
              <b>Login for the client <span class="muted small">(optional)</span></b>
              <label class="field"><span>Username</span><input name="un" [(ngModel)]="nc.username" placeholder="e.g. butt.foods" autocomplete="off" /></label>
              <label class="field"><span>Password (min 8)</span>
                <div class="row"><input name="pw" [(ngModel)]="nc.password" type="text" autocomplete="new-password" />
                  <button class="btn ghost" type="button" (click)="nc.password = randomPassword()">🎲</button></div></label>
            </div>
          </div>
        </div>
        <div class="row"><button class="btn primary" [disabled]="busy() || !nc.name.trim()">Create client</button>
          <span class="muted small">The client sees only their own restaurants. Share the username and password with them privately.</span></div>
      </form>
    }

    @if ((data()?.clients?.length ?? 0) > 3) {
      <input class="search" [ngModel]="search()" (ngModelChange)="search.set($event)" placeholder="🔍 Search clients or restaurants" />
    }

    @for (c of filtered(); track c.id) {
      <section class="card client" [class.suspended]="!c.isActive">
        <div class="client-head">
          <div>
            <h2>{{ c.name }}
              @if (!c.isActive) { <span class="pill warn">Suspended</span> }
            </h2>
            <div class="muted small">Today: {{ c.ordersToday }} orders · {{ money(c.salesToday) }} · {{ c.chatsToday }} chats</div>
          </div>
          <div class="row">
            <button class="btn ghost sm" (click)="rename(c)">Rename</button>
            <button class="btn ghost sm" (click)="toggleActive(c)">{{ c.isActive ? 'Suspend' : 'Activate' }}</button>
            @if (c.restaurants.length === 0) { <button class="btn danger-ghost sm" (click)="removeClient(c)">Delete</button> }
          </div>
        </div>

        <div class="branches">
          @for (r of c.restaurants; track r.id) {
            <div class="branch">
              <div class="branch-main">
                <b>{{ r.name }}</b>
                <span class="pill xs" [class.ok]="r.isBotEnabled" [class.warn]="!r.isBotEnabled">Bot {{ r.isBotEnabled ? 'ON' : 'OFF' }}</span>
                <div class="muted small">{{ r.ordersToday }} orders · {{ money(r.salesToday) }} · {{ r.activeOrders }} active ·
                  {{ r.chatsToday }} chats{{ r.lastMessageAt ? ' · last message ' + when(r.lastMessageAt) : '' }}</div>
              </div>
              <div class="row">
                @if (movingId() === r.id) {
                  <select class="move" [ngModel]="c.id" (ngModelChange)="move(r, +$event)">
                    @for (o of data()!.clients; track o.id) { <option [value]="o.id">{{ o.id === c.id ? 'Move to…' : o.name }}</option> }
                  </select>
                } @else if (data()!.clients.length > 1) {
                  <button class="link small" (click)="movingId.set(r.id)">Move</button>
                }
                <button class="btn primary sm" (click)="open(r)">Open →</button>
              </div>
            </div>
          } @empty {
            <div class="muted small">No restaurant yet.</div>
          }
        </div>

        @if (addBranchFor() === c.id) {
          <form class="form-row" (ngSubmit)="addBranch(c)">
            <label class="field grow"><span>Restaurant / branch name</span><input name="bn" [(ngModel)]="branchName" /></label>
            <label class="field grow"><span>WhatsApp Phone number ID</span><input name="bp" [(ngModel)]="branchPhoneId" /></label>
            <button class="btn primary">Add</button>
            <button class="btn ghost" type="button" (click)="addBranchFor.set(null)">Cancel</button>
          </form>
        }

        <div class="logins">
          <span class="muted small">Logins:</span>
          @for (u of c.users; track u.id) {
            <span class="login-chip" [title]="u.lastLoginAt ? 'Last login ' + when(u.lastLoginAt) : 'Never logged in'">
              🔑 {{ u.username }}
              <button class="link small" (click)="resetPassword(u)">reset</button>
              <button class="link small danger" (click)="removeLogin(u)">✕</button>
            </span>
          } @empty { <span class="muted small">none</span> }
        </div>

        @if (addLoginFor() === c.id) {
          <form class="form-row" (ngSubmit)="addLogin(c)">
            <label class="field grow"><span>Username</span><input name="lu" [(ngModel)]="loginName" autocomplete="off" /></label>
            <label class="field grow"><span>Password (min 8)</span><input name="lp" [(ngModel)]="loginPassword" type="text" autocomplete="new-password" /></label>
            <button class="btn primary">Create login</button>
            <button class="btn ghost" type="button" (click)="addLoginFor.set(null)">Cancel</button>
          </form>
        }

        <div class="row">
          <button class="btn ghost sm" (click)="startBranch(c)">+ Add restaurant</button>
          <button class="btn ghost sm" (click)="startLogin(c)">+ Add login</button>
        </div>
      </section>
    } @empty {
      @if (data()) { <div class="card empty"><h2>No clients yet</h2><p class="muted">Click <b>+ New client</b> to add the first one.</p></div> }
    }
  `,
})
export class ClientsPage {
  private api = inject(Api);
  private toasts = inject(Toasts);
  private router = inject(Router);
  private restaurants = inject(CurrentRestaurant);

  data = signal<ClientsResponse | null>(null);
  usage = signal<UsageReport | null>(null);
  busy = signal(false);
  showNew = signal(false);
  addBranchFor = signal<number | null>(null);
  addLoginFor = signal<number | null>(null);
  movingId = signal<number | null>(null);
  search = signal('');

  nc = { name: '', restaurantName: '', whatsAppPhoneNumberId: '', username: '', password: '' };
  branchName = '';
  branchPhoneId = '';
  loginName = '';
  loginPassword = '';

  money = money;
  when = when;
  randomPassword = randomPassword;

  filtered = computed(() => {
    const list = this.data()?.clients ?? [];
    const q = this.search().trim().toLowerCase();
    return q ? list.filter(c => c.name.toLowerCase().includes(q) || c.restaurants.some(r => r.name.toLowerCase().includes(q))) : list;
  });

  constructor() {
    this.load();
  }

  async load() {
    try { this.data.set(await this.api.clients()); } catch (e) { this.toasts.error(errorText(e)); }
    // The AI cost cards are extra: if they fail, the page still works.
    try { this.usage.set(await this.api.usage()); } catch { this.usage.set(null); }
  }

  private async run(action: () => Promise<unknown>, ok: string) {
    this.busy.set(true);
    try {
      await action();
      this.toasts.ok(ok);
      await Promise.all([this.load(), this.restaurants.load()]);
      return true;
    } catch (e) {
      this.toasts.error(errorText(e));
      return false;
    } finally {
      this.busy.set(false);
    }
  }

  async createClient() {
    const n = this.nc;
    const done = await this.run(() => this.api.createClient({
      name: n.name,
      restaurantName: n.restaurantName || undefined,
      whatsAppPhoneNumberId: n.whatsAppPhoneNumberId || undefined,
      username: n.username || undefined,
      password: n.password || undefined,
    }), n.username ? `Client created. Login: ${n.username.trim().toLowerCase()}. Share the password now.` : 'Client created.');
    if (done) {
      this.nc = { name: '', restaurantName: '', whatsAppPhoneNumberId: '', username: '', password: '' };
      this.showNew.set(false);
    }
  }

  open(r: ClientBranch) {
    this.restaurants.select(r.id);
    this.router.navigate(['/']);
  }

  rename(c: Client) {
    const name = prompt('New name for this client:', c.name);
    if (name && name.trim() !== c.name) this.run(() => this.api.updateClient(c.id, name, c.isActive), 'Renamed');
  }

  toggleActive(c: Client) {
    if (c.isActive && !confirm(`Suspend "${c.name}"? Their logins stop working right away. The bot keeps running (turn it OFF per restaurant if needed).`)) return;
    this.run(() => this.api.updateClient(c.id, c.name, !c.isActive), c.isActive ? 'Client suspended' : 'Client activated');
  }

  removeClient(c: Client) {
    if (confirm(`Delete the client "${c.name}" and its logins?`)) this.run(() => this.api.deleteClient(c.id), 'Client deleted');
  }

  move(r: ClientBranch, organizationId: number) {
    this.movingId.set(null);
    const target = this.data()?.clients.find(c => c.id === organizationId);
    if (!target || target.restaurants.some(x => x.id === r.id)) return;
    if (!confirm(`Move "${r.name}" to ${target.name}? ${target.name}'s logins will see it; the old client's logins won't.`)) return;
    this.run(() => this.api.moveRestaurant(r.id, organizationId), 'Restaurant moved');
  }

  startBranch(c: Client) {
    this.branchName = '';
    this.branchPhoneId = '';
    this.addLoginFor.set(null);
    this.addBranchFor.set(c.id);
  }

  async addBranch(c: Client) {
    if (await this.run(() => this.api.createRestaurant(this.branchName, this.branchPhoneId, c.id), 'Restaurant added. Open it to fill in the menu.'))
      this.addBranchFor.set(null);
  }

  startLogin(c: Client) {
    this.loginName = '';
    this.loginPassword = randomPassword();
    this.addBranchFor.set(null);
    this.addLoginFor.set(c.id);
  }

  async addLogin(c: Client) {
    const name = this.loginName.trim().toLowerCase();
    if (await this.run(() => this.api.createUser(this.loginName, this.loginPassword, c.id), `Login "${name}" created. Share the password now.`))
      this.addLoginFor.set(null);
  }

  resetPassword(u: ClientLogin) {
    const password = prompt(`New password for "${u.username}" (min 8 characters). They will be logged out everywhere.`, randomPassword());
    if (password) this.run(() => this.api.resetUserPassword(u.id, password), `Password for "${u.username}" changed. Share the new one.`);
  }

  removeLogin(u: ClientLogin) {
    if (confirm(`Delete the login "${u.username}"? They will be logged out immediately.`))
      this.run(() => this.api.deleteUser(u.id), 'Login deleted');
  }
}
