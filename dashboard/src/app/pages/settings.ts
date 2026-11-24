import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Api, Setting, SettingsResponse, TestResult } from '../core/api';
import { errorText, Toasts } from '../core/toast';

@Component({
  selector: 'app-settings',
  imports: [FormsModule],
  template: `
    <div class="page-head">
      <h1>Settings</h1>
      <button class="btn primary" [disabled]="!dirty() || busy()" (click)="save()">{{ busy() ? 'Saving…' : 'Save' }}</button>
    </div>
    <p class="muted">Settings saved here go to the database, apply immediately (no restart), and take priority over appsettings / env vars.
      "Reset to default" removes the dashboard value.</p>

    @if (data(); as d) {
      <section class="card">
        <h2>Webhook (enter this in Meta)</h2>
        <div class="kv"><span>Callback URL</span><code class="copy" (click)="copy(d.webhookUrl)">{{ d.webhookUrl }}</code></div>
        <div class="muted small">Locally, Meta needs ngrok to reach you: <code>ngrok http 5040</code>, then the ngrok https URL + /webhook.
          In Meta > WhatsApp > Configuration > Webhook > Edit, use the verify token below and subscribe to the "messages" field.</div>
        <div class="kv"><span>Database</span><code>{{ d.database }}</code></div>
        <div class="muted small">The database setting can't be changed here: use appsettings / user-secrets / DATABASE_URL.</div>
      </section>

      @for (g of groups(); track g) {
        <section class="card">
          <div class="card-head">
            <h2>{{ g }}</h2>
            @if (g === 'WhatsApp') { <button class="btn sm" (click)="test('whatsapp')" [disabled]="testing()">🔌 Connection test</button> }
            @if (g === 'AI (Claude)') { <button class="btn sm" (click)="test('ai')" [disabled]="testing()">🔌 Claude test</button> }
          </div>
          @if (results()[g]; as r) { <div class="alert" [class.error]="!r.ok" [class.ok]="r.ok">{{ r.message }}</div> }

          <div class="settings-grid">
            @for (s of inGroup(g); track s.key) {
              <div class="setting" [class.changed]="edits[s.key] !== undefined">
                <div class="setting-label">
                  <b>{{ s.label }}</b>
                  <span class="src src-{{ s.source }}">{{ sourceLabel[s.source] }}</span>
                </div>
                @switch (s.kind) {
                  @case ('bool') {
                    <label class="switch"><input type="checkbox" [ngModel]="boolValue(s)" (ngModelChange)="set(s, $event ? 'true' : 'false')" /><span></span></label>
                  }
                  @case ('select') {
                    <select [ngModel]="value(s)" (ngModelChange)="set(s, $event)">
                      @for (o of s.options; track o) { <option [value]="o">{{ o }}</option> }
                    </select>
                  }
                  @case ('textarea') {
                    <textarea rows="3" [ngModel]="value(s)" (ngModelChange)="set(s, $event)"></textarea>
                  }
                  @case ('secret') {
                    <div class="row">
                      <input [type]="reveal[s.key] ? 'text' : 'password'" autocomplete="new-password"
                        [placeholder]="s.isSet ? 'Set (' + s.hint + '). Type a new value to change it' : 'Not set'"
                        [ngModel]="edits[s.key] ?? ''" (ngModelChange)="set(s, $event)" />
                      <button class="btn ghost sm" type="button" (click)="reveal[s.key] = !reveal[s.key]">{{ reveal[s.key] ? '🙈' : '👁' }}</button>
                      @if (s.key === 'WhatsApp:VerifyToken') { <button class="btn ghost sm" type="button" (click)="generate(s)">🎲 Generate</button> }
                    </div>
                  }
                  @default {
                    <input [type]="s.kind === 'number' ? 'number' : 'text'" [attr.list]="s.options ? 'opt-' + s.key : null"
                      [ngModel]="value(s)" (ngModelChange)="set(s, $event)" />
                    @if (s.options) { <datalist [id]="'opt-' + s.key">@for (o of s.options; track o) { <option [value]="o"></option> }</datalist> }
                  }
                }
                <small>{{ s.help }}</small>
                @if (s.source === 'dashboard') { <button class="link small" type="button" (click)="clear(s)">Reset to default</button> }
              </div>
            }
          </div>
        </section>
      }
    }
  `,
})
export class SettingsPage {
  private api = inject(Api);
  private toasts = inject(Toasts);
  data = signal<SettingsResponse | null>(null);
  busy = signal(false);
  testing = signal(false);
  results = signal<Record<string, TestResult>>({});
  /** key → new value (string), or null (remove the dashboard value). */
  edits: Record<string, string | null> = {};
  reveal: Record<string, boolean> = {};
  private version = signal(0); // bump when edits change so dirty() re-runs
  dirty = computed(() => (this.version(), Object.keys(this.edits).length > 0));
  groups = computed(() => [...new Set(this.data()?.settings.map(s => s.group) ?? [])]);
  sourceLabel = { dashboard: 'Dashboard', config: 'appsettings / env', default: 'Default' };

  constructor() { this.load(); }

  async load() {
    try {
      this.data.set(await this.api.settings());
      this.edits = {};
      this.version.update(v => v + 1);
    } catch (e) {
      this.toasts.error(errorText(e));
    }
  }

  inGroup(g: string) { return this.data()!.settings.filter(s => s.group === g); }
  value(s: Setting) { return this.edits[s.key] ?? s.value ?? ''; }
  boolValue(s: Setting) { return this.value(s) === 'true' || this.value(s) === 'True'; }

  set(s: Setting, v: string) {
    if (s.kind === 'secret' && v === '') delete this.edits[s.key];
    else if (s.kind !== 'secret' && v === (s.value ?? '')) delete this.edits[s.key];
    else this.edits[s.key] = String(v);
    this.version.update(x => x + 1);
  }

  clear(s: Setting) {
    this.edits[s.key] = null;
    this.version.update(x => x + 1);
    this.save();
  }

  generate(s: Setting) {
    const bytes = crypto.getRandomValues(new Uint8Array(18));
    const token = 'chatserve-' + Array.from(bytes, b => b.toString(16).padStart(2, '0')).join('');
    this.reveal[s.key] = true;
    this.set(s, token);
    navigator.clipboard?.writeText(token).catch(() => undefined);
    this.toasts.ok('New token generated and copied. Click Save, then enter the same token in Meta.');
  }

  async save() {
    this.busy.set(true);
    try {
      const changedPassword = this.edits['Dashboard:AdminPassword'];
      await this.api.saveSettings(this.edits);
      this.toasts.ok(changedPassword ? 'Saved. Use the new password next time you log in.' : 'Settings saved and applied.');
      await this.load();
    } catch (e) {
      this.toasts.error(errorText(e));
    } finally {
      this.busy.set(false);
    }
  }

  async test(kind: 'whatsapp' | 'ai') {
    if (this.dirty()) { this.toasts.error('Save first, then test.'); return; }
    this.testing.set(true);
    try {
      const r = kind === 'whatsapp' ? await this.api.testWhatsApp() : await this.api.testAi();
      this.results.update(x => ({ ...x, [kind === 'whatsapp' ? 'WhatsApp' : 'AI (Claude)']: r }));
    } catch (e) {
      this.toasts.error(errorText(e));
    } finally {
      this.testing.set(false);
    }
  }

  copy(text: string) { navigator.clipboard?.writeText(text).then(() => this.toasts.ok('Copied'), () => this.toasts.error('Could not copy. Please select the text and copy it yourself')); }
}
