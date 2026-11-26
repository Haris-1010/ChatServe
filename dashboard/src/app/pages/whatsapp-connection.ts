import { Component, effect, inject, input, signal, untracked } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Api, ConnectStep, SignupConfig, TestResult, WhatsAppConnection, WhatsAppMode } from '../core/api';
import { runEmbeddedSignup } from '../core/embedded-signup';
import { errorText, Toasts } from '../core/toast';

/**
 * Admin only: how this restaurant's WhatsApp number is connected.
 * Classic (API only) keeps working as before; coexistence keeps the number in the owner's WhatsApp Business app too.
 */
@Component({
  selector: 'app-whatsapp-connection',
  imports: [FormsModule, DatePipe],
  template: `
    @if (c(); as c) {
      <section class="card form-col">
        <div class="card-head">
          <h2>WhatsApp connection</h2>
          <span class="pill" [class.ok]="c.mode === 'Coexistence'">{{ c.mode === 'Coexistence' ? '📱 + 🤖 Coexistence' : '🤖 API only' }}</span>
        </div>

        <p class="muted small">
          @if (c.mode === 'Coexistence') {
            The number works in the owner's <b>WhatsApp Business app and</b> in ChatServe. When the owner replies from the phone,
            the reply shows in Chats (📱) and the bot can pause for that customer.
          } @else {
            The number lives only on the API (it can't be used in the WhatsApp app). This is the classic setup.
          }
        </p>

        <div class="kv"><span>Phone number ID</span><code>{{ c.phoneNumberId }}</code></div>
        <div class="kv"><span>WhatsApp Business account</span><code>{{ c.whatsAppBusinessAccountId ?? '-' }}</code></div>
        <div class="kv"><span>Token</span><span>{{ c.hasOwnToken ? 'Own token (from Embedded Signup)' : 'App-wide token from Settings' }}</span></div>
        @if (c.connectedAt) { <div class="kv"><span>Connected</span><span>{{ c.connectedAt | date: 'd MMM y, h:mm a' }}</span></div> }
        @if (c.mode === 'Coexistence') {
          <div class="kv"><span>History from phone</span><span>
            {{ c.historyMessagesImported }} messages imported
            @if (c.historySyncRequestedAt) { · requested {{ c.historySyncRequestedAt | date: 'd MMM, h:mm a' }} }
          </span></div>
          <div class="kv"><span>Last reply from phone</span><span>{{ c.lastPhoneReplyAt ? (c.lastPhoneReplyAt | date: 'd MMM, h:mm a') : '-' }}</span></div>
          <label class="row">
            <label class="switch"><input type="checkbox" [ngModel]="c.pauseBotOnPhoneReply" (ngModelChange)="setPause($event)" /><span></span></label>
            <span>Pause the bot for a customer when the owner replies from the phone</span>
          </label>
        }

        <div class="row wrap">
          <button class="btn sm" (click)="test()" [disabled]="busy()">🔌 Test this number</button>
          @if (c.mode === 'Coexistence') {
            <button class="btn sm" (click)="sync()" [disabled]="busy()">⟳ Ask for phone history again</button>
          }
        </div>
        @if (testResult(); as t) { <div class="alert" [class.error]="!t.ok" [class.ok]="t.ok">{{ t.message }}</div> }

        <h3>Connect with Facebook (Embedded Signup)</h3>
        @if (cfg(); as g) {
          @if (!g.ready) {
            <div class="alert">Needs Meta Tech Provider setup. Missing in Settings > WhatsApp: {{ g.missing.join(', ') }}.</div>
          }
          <div class="row wrap">
            <button class="btn primary" (click)="connect('Coexistence')" [disabled]="busy() || !g.ready">📱 Connect WhatsApp Business app number</button>
            <button class="btn" (click)="connect('CloudApi')" [disabled]="busy() || !g.ready">🤖 Connect a new number (API only)</button>
          </div>
          <small class="muted">The client logs in with Facebook (or you do it together with them). For the app number, they scan a QR code
            in WhatsApp Business (version 2.24.17 or newer) and choose to share chat history. Their app must be opened at least every 14 days.</small>
        }
        @if (steps().length) {
          <ul class="steps">
            @for (s of steps(); track s.name) { <li>{{ s.ok ? '✅' : '❌' }} <b>{{ s.name }}</b>: {{ s.message }}</li> }
          </ul>
        }

        <details>
          <summary>Manual settings (e.g. a number connected through a partner)</summary>
          <div class="grid-2">
            <label class="field"><span>Mode</span>
              <select [(ngModel)]="manualMode"><option value="CloudApi">API only</option><option value="Coexistence">Coexistence</option></select></label>
            <label class="field"><span>WhatsApp Business account ID</span><input [(ngModel)]="manualWaba" /></label>
            <label class="field"><span>Own access token</span>
              <input type="password" autocomplete="new-password" [(ngModel)]="manualToken"
                [placeholder]="c.hasOwnToken ? 'Set. Type a new one to change it' : 'Empty = use the app-wide token'" />
              @if (c.hasOwnToken) { <small><a href="" (click)="$event.preventDefault(); clearToken()">Remove the own token</a> (use the app-wide one again)</small> }
            </label>
          </div>
          <button class="btn sm" (click)="saveManual()" [disabled]="busy()">Save</button>
        </details>
      </section>
    }
  `,
  styles: [`
    .row.wrap { flex-wrap: wrap; gap: 8px; }
    h3 { margin: 12px 0 0; font-size: 15px; }
    .steps { margin: 0; padding-left: 4px; list-style: none; display: grid; gap: 4px; font-size: 13.5px; }
    details summary { cursor: pointer; color: var(--muted); font-size: 13px; margin-bottom: 8px; }
  `],
})
export class WhatsAppConnectionCard {
  private api = inject(Api);
  private toasts = inject(Toasts);
  rid = input.required<number>();

  c = signal<WhatsAppConnection | null>(null);
  cfg = signal<SignupConfig | null>(null);
  busy = signal(false);
  steps = signal<ConnectStep[]>([]);
  testResult = signal<TestResult | null>(null);
  manualMode: WhatsAppMode = 'CloudApi';
  manualWaba = '';
  manualToken = '';

  constructor() {
    effect(() => {
      const rid = this.rid();
      untracked(() => this.load(rid));
    });
  }

  async load(rid: number) {
    this.steps.set([]);
    this.testResult.set(null);
    try {
      const [c, cfg] = await Promise.all([this.api.whatsAppConnection(rid), this.api.signupConfig()]);
      this.show(c);
      this.cfg.set(cfg);
    } catch (e) { this.toasts.error(errorText(e)); }
  }

  private show(c: WhatsAppConnection) {
    this.c.set(c);
    this.manualMode = c.mode;
    this.manualWaba = c.whatsAppBusinessAccountId ?? '';
    this.manualToken = '';
  }

  async connect(mode: WhatsAppMode) {
    const g = this.cfg();
    if (!g) return;
    this.busy.set(true);
    this.steps.set([]);
    try {
      const out = await runEmbeddedSignup(g.appId, g.configId, g.graphApiVersion, mode === 'Coexistence');
      const wabaId = out.wabaId ?? (this.manualWaba.trim() || null);
      if (!wabaId) throw new Error('Meta did not send the WhatsApp Business account ID. Enter it under "Manual settings" and try again.');
      const res = await this.api.connectWhatsApp(this.rid(), { code: out.code, wabaId, phoneNumberId: out.phoneNumberId, mode });
      this.steps.set(res.steps);
      this.show(res.connection);
      res.ok ? this.toasts.ok('WhatsApp connected.') : this.toasts.error('Connected, but some steps failed (see below).');
    } catch (e) {
      this.toasts.error(errorText(e));
    } finally {
      this.busy.set(false);
    }
  }

  async sync() {
    this.busy.set(true);
    try {
      const res = await this.api.syncWhatsAppHistory(this.rid());
      this.steps.set(res.steps);
      this.show(res.connection);
    } catch (e) { this.toasts.error(errorText(e)); } finally { this.busy.set(false); }
  }

  async test() {
    this.busy.set(true);
    try { this.testResult.set(await this.api.testRestaurantWhatsApp(this.rid())); }
    catch (e) { this.toasts.error(errorText(e)); } finally { this.busy.set(false); }
  }

  setPause(on: boolean) {
    const c = this.c();
    if (c) this.update({ mode: c.mode, whatsAppBusinessAccountId: c.whatsAppBusinessAccountId, accessToken: null, pauseBotOnPhoneReply: on });
  }

  saveManual() {
    const c = this.c();
    if (c) this.update({
      mode: this.manualMode, whatsAppBusinessAccountId: this.manualWaba.trim() || null,
      accessToken: this.manualToken.trim() ? this.manualToken.trim() : null, pauseBotOnPhoneReply: c.pauseBotOnPhoneReply,
    });
  }

  clearToken() {
    const c = this.c();
    if (c) this.update({ mode: c.mode, whatsAppBusinessAccountId: c.whatsAppBusinessAccountId, accessToken: '', pauseBotOnPhoneReply: c.pauseBotOnPhoneReply });
  }

  private async update(u: Parameters<Api['updateWhatsAppConnection']>[1]) {
    this.busy.set(true);
    try {
      this.show(await this.api.updateWhatsAppConnection(this.rid(), u));
      this.toasts.ok('Saved.');
    } catch (e) { this.toasts.error(errorText(e)); } finally { this.busy.set(false); }
  }
}
