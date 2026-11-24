import { Component, computed, ElementRef, inject, signal, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Api, CurrentRestaurant } from '../core/api';
import { errorText, Toasts } from '../core/toast';

interface Line { from: 'me' | 'bot' | 'system' | 'staff'; text: string; meta?: string; }

const PKEY = 'chatserve.simPhone';
function newPhone() { return '000' + Math.floor(100000000 + Math.random() * 899999999); }

@Component({
  selector: 'app-simulator',
  imports: [FormsModule, RouterLink],
  template: `
    <div class="page-head">
      <h1>Bot test (simulator)</h1>
      <button class="btn ghost" (click)="reset()">+ New chat</button>
    </div>
    <p class="muted">Chat with the bot as a customer, just like on WhatsApp. No real WhatsApp needed:
      replies, orders and bookings are saved to the real database (they show with a "Test" tag on the Orders and Chats pages).
      Messages that would go to the kitchen show here in a yellow card. The AI needs an Anthropic key in Settings.</p>

    <div class="sim-wrap">
      <div class="phone">
        <div class="phone-head">
          <div class="avatar">🍛</div>
          <div><b>{{ restaurants.current?.name ?? 'Restaurant' }}</b><div class="small">{{ busy() ? 'typing…' : 'online' }}</div></div>
        </div>
        <div class="phone-body" #scroller>
          @for (l of lines(); track $index) {
            @if (l.from === 'system' || l.from === 'staff') {
              <div class="sys" [class.staff]="l.from === 'staff'">{{ l.text }}</div>
            } @else {
              <div class="bubble-row" [class.out]="l.from === 'me'">
                <div class="bubble" [class.me]="l.from === 'me'"><div class="text">{{ l.text }}</div>
                  @if (l.meta) { <div class="meta">{{ l.meta }}</div> }</div>
              </div>
            }
          } @empty {
            <div class="sys">Type something to start, for example: "How much is the karahi?" or "Do you deliver to DHA?"</div>
          }
        </div>
        <form class="phone-input" (ngSubmit)="send()">
          <input name="text" [(ngModel)]="text" placeholder="Message" autocomplete="off" [disabled]="busy()" />
          <button class="send" [disabled]="busy() || !text.trim()">➤</button>
        </form>
      </div>

      <aside class="card sim-side">
        <label class="field"><span>Customer name</span><input [(ngModel)]="name" /></label>
        <label class="field"><span>Test number</span><input [ngModel]="phone()" disabled /></label>
        <div class="muted small">Test numbers start with "000". Nothing is sent to real WhatsApp.</div>
        <h3>Quick questions</h3>
        <div class="chips">
          @for (q of quick; track q) { <button class="chip" (click)="text = q; send()" [disabled]="busy()">{{ q }}</button> }
        </div>
        <p class="small muted">If a reply looks wrong, fix the <a routerLink="/menu">Menu/FAQs</a> or <a routerLink="/restaurant">Restaurant info</a>. The bot uses the new data from the next message.</p>
      </aside>
    </div>
  `,
})
export class SimulatorPage {
  private api = inject(Api);
  private toasts = inject(Toasts);
  protected restaurants = inject(CurrentRestaurant);
  private scroller = viewChild<ElementRef<HTMLElement>>('scroller');
  lines = signal<Line[]>([]);
  busy = signal(false);
  phone = signal(this.loadPhone());
  name = 'Test Customer';
  text = '';
  quick = ['Send me the menu', 'How much is the karahi?', 'Do you deliver to DHA?', "What's in the family deal?",
    '1 full chicken karahi and 4 naan, Johar Town', 'confirm', 'Book a table for 4 tomorrow at 9 pm', 'Where is my order?'];
  private rid = computed(() => this.restaurants.id());

  private loadPhone() {
    try { const p = localStorage.getItem(PKEY); if (p?.startsWith('000')) return p; } catch { /* ignore */ }
    const p = newPhone();
    try { localStorage.setItem(PKEY, p); } catch { /* ignore */ }
    return p;
  }

  reset() {
    const p = newPhone();
    this.phone.set(p);
    try { localStorage.setItem(PKEY, p); } catch { /* ignore */ }
    this.lines.set([]);
  }

  async send() {
    const text = this.text.trim();
    const rid = this.rid();
    if (!text || !rid) return;
    this.text = '';
    this.push({ from: 'me', text });
    this.busy.set(true);
    try {
      const res = await this.api.simulate(rid, this.phone(), this.name, text);
      if (res.reply) this.push({ from: 'bot', text: res.reply, meta: `${res.usedAi ? 'AI' : 'Welcome reply (AI OFF)'} · ${(res.milliseconds / 1000).toFixed(1)}s` });
      if (res.note) this.push({ from: 'system', text: res.note });
      for (const a of res.staffAlerts) this.push({ from: 'staff', text: '📲 Would be sent to kitchen/staff:\n' + a });
    } catch (e) {
      this.toasts.error(errorText(e));
      this.push({ from: 'system', text: 'Error: ' + errorText(e) });
    } finally {
      this.busy.set(false);
    }
  }

  private push(l: Line) {
    this.lines.update(x => [...x, l]);
    setTimeout(() => { const el = this.scroller()?.nativeElement; if (el) el.scrollTop = el.scrollHeight; });
  }
}
