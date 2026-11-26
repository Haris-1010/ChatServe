import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Api, CurrentRestaurant } from '../core/api';
import { onRestaurant } from '../core/page';
import { errorText, Toasts } from '../core/toast';

const IMAGE_TYPES = ['image/jpeg', 'image/png', 'image/webp', 'image/gif'];
const MAX_IMAGE_BYTES = 5 * 1024 * 1024;

@Component({
  selector: 'app-training',
  imports: [FormsModule],
  template: `
    <div class="page-head"><h1>Bot training</h1></div>

    <div class="card form-col">
      <div>
        <b>1. Bot rules</b>
        <div class="muted small">
          Tell the bot what to do, in your own words. One rule per line, in English or Roman Urdu. No code needed:
          the bot follows these from the next message. Facts like prices and timings go in {{ terms.items }} and Business info.
        </div>
      </div>

      <label class="field">
        <span>Welcome message (when a customer says hi)</span>
        <input [(ngModel)]="welcome" [placeholder]="welcomeHint()" maxlength="500" />
        <small>Optional. The bot sends it in the customer's language, with buttons to choose from.</small>
      </label>

      <label class="field">
        <span>Rules</span>
        <textarea rows="8" [(ngModel)]="rules" placeholder="- Always call the customer 'sir' or 'madam'&#10;- Offer the family deal when someone orders 2 or more items"></textarea>
        <small [class.over]="rules().length > rulesMax()">{{ rules().length.toLocaleString() }} / {{ rulesMax().toLocaleString() }} characters</small>
      </label>

      <div>
        <div class="muted small">Tap to add a ready-made rule:</div>
        <div class="chips">
          @for (s of suggestions(); track s) { <button class="chip" type="button" (click)="addRule(s)">+ {{ s }}</button> }
        </div>
      </div>

      <div class="row">
        <button class="btn primary" (click)="saveRules()" [disabled]="busy() || rules().length > rulesMax()">Save rules</button>
      </div>
      <div class="muted small">Tip: in <b>Test the bot</b>, press <b>✏️ Correct</b> under any wrong reply and write what it should have said. It's added here as a rule.</div>
    </div>

    <div class="card form-col">
      <div>
        <b>2. Teach the bot how you talk (example chats)</b>
        <div class="muted small">
          Add old WhatsApp chats with your customers. The bot copies your tone, greetings and way of writing
          (for example Roman Urdu, "Ji sir", "bhai"). Prices and timings always come from {{ terms.items }} and Business info, not from these chats.
        </div>
      </div>

      <div class="tips small">
        <b>How to add chats</b>
        <ul>
          <li><b>Export a chat (best):</b> in WhatsApp open a customer chat → ⋮ → More → <i>Export chat</i> → <i>Without media</i>. Upload the .txt file here.</li>
          <li><b>Screenshots:</b> upload chat screenshots (JPG/PNG). The AI reads the text out of them.</li>
          <li><b>Or type / paste</b> example conversations below, like:<br />
            <code>Customer: menu bhej dein</code><br /><code>Business: Ji zaroor! Ye rahi details 😊</code></li>
        </ul>
        5 to 10 good chats are enough. Phone numbers are hidden automatically. Check the text and remove anything private before saving.
      </div>

      <div class="row">
        <label class="btn" [class.disabled]="importing()">
          📎 Add chat files / screenshots
          <input type="file" multiple accept=".txt,text/plain,image/*" hidden (change)="addFiles($event)" [disabled]="importing()" />
        </label>
        @if (importing()) { <span class="muted small">Reading {{ importing() }}…</span> }
      </div>

      <label class="field">
        <span>Example chats</span>
        <textarea rows="18" [(ngModel)]="text" [placeholder]="placeholder"></textarea>
        <small [class.over]="length() > maxLength()">{{ length().toLocaleString() }} / {{ maxLength().toLocaleString() }} characters</small>
      </label>

      <div class="row">
        <button class="btn primary" (click)="save()" [disabled]="busy() || !!importing() || length() > maxLength()">Save</button>
        <button class="btn ghost" (click)="text.set('')" [disabled]="busy() || !text()">Clear</button>
      </div>
      <div class="muted small">After saving, try it in <b>Test the bot</b>. The bot uses the new examples from the next message.</div>
    </div>
  `,
  styles: [`
    .tips { background: var(--primary-soft, rgba(16,185,129,.08)); border-radius: 10px; padding: 10px 14px; line-height: 1.55; }
    .tips ul { margin: 6px 0; padding-left: 18px; }
    .row { display: flex; gap: 10px; align-items: center; flex-wrap: wrap; }
    label.btn { cursor: pointer; }
    label.btn.disabled { opacity: .5; pointer-events: none; }
    .over { color: #e5484d; font-weight: 600; }
    code { font-size: 12px; }
    .chips { display: flex; flex-wrap: wrap; gap: 6px; margin-top: 6px; }
  `],
})
export class TrainingPage {
  private api = inject(Api);
  private toasts = inject(Toasts);
  private restaurants = inject(CurrentRestaurant);
  protected get terms() { return this.restaurants.terms; }
  rules = signal('');
  welcome = signal('');
  rulesMax = signal(6000);

  welcomeHint = computed(() => {
    const name = this.restaurants.current?.name ?? 'us';
    return `Assalam o alaikum! ${name} mein khush aamdeed 😊 Aap kya dekhna chahenge?`;
  });

  suggestions = computed(() => {
    const common = [
      'Reply in Roman Urdu unless the customer writes in English',
      "Call customers 'sir' or 'madam'",
      'Keep replies very short',
      'If someone asks for a discount, say a team member will reply',
      'Never share the owner\'s personal number',
    ];
    switch (this.restaurants.current?.businessType) {
      case 'Shop': return ['Always ask for the size and colour before taking an order', 'Delivery all over Pakistan takes 3-5 working days', 'Exchange within 7 days, no refunds', ...common];
      case 'Services': return ['Always ask which service and a preferred time', 'Tell customers to arrive 10 minutes early', 'Prices can change after consultation: say so', ...common];
      case 'Other': return common;
      default: return ['Always suggest a drink with the order', 'Offer the family deal when someone orders for 4 or more people', 'Tell customers food is freshly made, so it takes 20-30 minutes', ...common];
    }
  });

  addRule(rule: string) {
    const now = this.rules().trim();
    if (now.toLowerCase().includes(rule.toLowerCase())) return;
    this.rules.set((now ? now + '\n' : '') + '- ' + rule);
  }

  async saveRules() {
    const rid = this.current.id();
    if (!rid) return;
    this.busy.set(true);
    try {
      const r = await this.api.saveBotRules(rid, this.rules(), this.welcome());
      this.rules.set(r.instructions ?? '');
      this.welcome.set(r.welcomeMessage ?? '');
      this.toasts.ok('Saved. The bot follows these rules from the next message.');
    } catch (e) {
      this.toasts.error(errorText(e));
    } finally {
      this.busy.set(false);
    }
  }
  text = signal('');
  maxLength = signal(20000);
  length = computed(() => this.text().length);
  busy = signal(false);
  importing = signal('');
  placeholder = 'Customer: Assalam o alaikum, delivery hoti hai Johar Town mein?\nBusiness: Walaikum assalam! Ji bilkul, Johar Town mein delivery hoti hai 😊 Kya order karna chahenge?';
  private current = onRestaurant(rid => this.load(rid));

  async load(rid: number) {
    try {
      const [t, r] = await Promise.all([this.api.training(rid), this.api.botRules(rid)]);
      this.rules.set(r.instructions ?? '');
      this.welcome.set(r.welcomeMessage ?? '');
      this.rulesMax.set(r.maxLength);
      this.text.set(t.text ?? '');
      this.maxLength.set(t.maxLength);
    } catch (e) { this.toasts.error(errorText(e)); }
  }

  async addFiles(event: Event) {
    const input = event.target as HTMLInputElement;
    const files = Array.from(input.files ?? []);
    input.value = '';
    const rid = this.current.id();
    if (!rid) return;

    for (const file of files) {
      this.importing.set(file.name);
      try {
        let body;
        if (file.type.startsWith('image/')) {
          if (!IMAGE_TYPES.includes(file.type)) { this.toasts.error(`${file.name}: use JPG, PNG, WEBP or GIF.`); continue; }
          if (file.size > MAX_IMAGE_BYTES) { this.toasts.error(`${file.name}: too large (max 5 MB).`); continue; }
          body = { fileName: file.name, imageBase64: await toBase64(file), mediaType: file.type };
        } else {
          body = { fileName: file.name, text: await file.text() };
        }
        const res = await this.api.importTraining(rid, body);
        if (res.text) {
          const before = this.text().trim();
          this.text.set((before ? before + '\n\n' : '') + `--- ${file.name} ---\n` + res.text);
        }
      } catch (e) {
        this.toasts.error(errorText(e));
      }
    }
    this.importing.set('');
    if (files.length) this.toasts.ok('Added. Check the text, then press Save.');
  }

  async save() {
    const rid = this.current.id();
    if (!rid) return;
    this.busy.set(true);
    try {
      const t = await this.api.saveTraining(rid, this.text());
      this.text.set(t.text ?? '');
      this.toasts.ok('Saved. The bot will talk like this from the next message.');
    } catch (e) {
      this.toasts.error(errorText(e));
    } finally {
      this.busy.set(false);
    }
  }
}

function toBase64(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(String(reader.result).split(',')[1] ?? '');
    reader.onerror = () => reject(reader.error);
    reader.readAsDataURL(file);
  });
}
