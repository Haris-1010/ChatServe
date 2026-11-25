import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Api } from '../core/api';
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
        <b>Teach the bot how your restaurant talks</b>
        <div class="muted small">
          Add old WhatsApp chats with your customers. The bot copies your tone, greetings and way of writing
          (for example Roman Urdu, "Ji sir", "bhai"). Prices, menu and timings always come from Menu and Restaurant info, not from these chats.
        </div>
      </div>

      <div class="tips small">
        <b>How to add chats</b>
        <ul>
          <li><b>Export a chat (best):</b> in WhatsApp open a customer chat → ⋮ → More → <i>Export chat</i> → <i>Without media</i>. Upload the .txt file here.</li>
          <li><b>Screenshots:</b> upload chat screenshots (JPG/PNG). The AI reads the text out of them.</li>
          <li><b>Or type / paste</b> example conversations below, like:<br />
            <code>Customer: menu bhej dein</code><br /><code>Restaurant: Ji zaroor! Ye raha hamara menu 😊</code></li>
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
  `],
})
export class TrainingPage {
  private api = inject(Api);
  private toasts = inject(Toasts);
  text = signal('');
  maxLength = signal(20000);
  length = computed(() => this.text().length);
  busy = signal(false);
  importing = signal('');
  placeholder = 'Customer: Assalam o alaikum, delivery hoti hai Johar Town mein?\nRestaurant: Walaikum assalam! Ji bilkul, Johar Town mein delivery hoti hai 😊 Kya order karna chahenge?';
  private current = onRestaurant(rid => this.load(rid));

  async load(rid: number) {
    try {
      const t = await this.api.training(rid);
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
