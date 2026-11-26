import { Component, computed, ElementRef, inject, signal, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Api, CurrentRestaurant, ReplyButtons, ReplyOption } from '../core/api';
import { errorText, Toasts } from '../core/toast';

interface Line { from: 'me' | 'bot' | 'system' | 'staff'; text: string; meta?: string; buttons?: ReplyButtons | null; }

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
      Messages that would go to your staff show here in a yellow card. The AI needs an Anthropic key in Settings.</p>

    <div class="sim-wrap">
      <div class="phone">
        <div class="phone-head">
          <div class="avatar">{{ restaurants.terms.icon }}</div>
          <div><b>{{ restaurants.current?.name ?? 'Business' }}</b><div class="small">{{ busy() ? 'typing…' : 'online' }}</div></div>
        </div>
        <div class="phone-body" #scroller>
          @for (l of lines(); track $index) {
            @if (l.from === 'system' || l.from === 'staff') {
              <div class="sys" [class.staff]="l.from === 'staff'">{{ l.text }}</div>
            } @else {
              <div class="bubble-row" [class.out]="l.from === 'me'">
                <div class="bubble" [class.me]="l.from === 'me'">
                  @if (l.buttons?.header) { <div class="wa-header">{{ l.buttons!.header }}</div> }
                  <div class="text">{{ l.text }}</div>
                  @if (l.buttons?.footer) { <div class="wa-footer">{{ l.buttons!.footer }}</div> }
                  @if (l.meta) { <div class="meta">{{ l.meta }}@if (l.from === 'bot') { · <a class="fix" (click)="startCorrect($index)">✏️ Correct</a> }</div> }
                  @if (correcting() === $index) {
                    <div class="correct">
                      <div class="small"><b>What should the bot have said?</b></div>
                      <textarea rows="3" [(ngModel)]="better" name="better" placeholder="Write the reply you want"></textarea>
                      <div class="row">
                        <button class="btn primary small" (click)="saveCorrect($index)" [disabled]="busy() || !better.trim()">Teach the bot</button>
                        <button class="btn ghost small" (click)="correcting.set(null)">Cancel</button>
                      </div>
                    </div>
                  }
                  @if (l.buttons; as b) {
                    @if (b.listButton) {
                      <button class="wa-btn" (click)="openList.set(openList() === $index ? null : $index)" [disabled]="busy()">☰ {{ b.listButton }}</button>
                      @if (openList() === $index) {
                        <div class="wa-list">
                          @for (o of b.options; track o.id) {
                            <button class="wa-row" (click)="tap(o)" [disabled]="busy()">
                              <b>{{ o.title }}</b>@if (o.description) { <span class="muted small">{{ o.description }}</span> }
                            </button>
                          }
                        </div>
                      }
                    } @else {
                      @for (o of b.options; track o.id) {
                        <button class="wa-btn" (click)="tap(o)" [disabled]="busy()">{{ o.title }}</button>
                      }
                    }
                  }
                </div>
              </div>
            }
          } @empty {
            <div class="sys">Type something to start, for example "Hi" or one of the quick questions.</div>
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
          @for (q of quick; track q) { <button class="chip" (click)="send(q)" [disabled]="busy()">{{ q }}</button> }
        </div>
        <p class="small muted">If a reply looks wrong, press <b>✏️ Correct</b> under it and write what it should have said, or fix <a routerLink="/menu">{{ restaurants.terms.items }}/FAQs</a> or <a routerLink="/restaurant">Business info</a>. The bot uses the new data from the next message.</p>
      </aside>
    </div>
  `,
  styles: [`
    .fix { cursor: pointer; text-decoration: underline; }
    .correct { margin-top: 8px; padding-top: 8px; border-top: 1px dashed rgba(0,0,0,.2); display: flex; flex-direction: column; gap: 6px; }
    .correct textarea { width: 100%; box-sizing: border-box; font: inherit; }
    .correct .row { display: flex; gap: 6px; }
  `],
})
export class SimulatorPage {
  private api = inject(Api);
  private toasts = inject(Toasts);
  protected restaurants = inject(CurrentRestaurant);
  private scroller = viewChild<ElementRef<HTMLElement>>('scroller');
  lines = signal<Line[]>([]);
  busy = signal(false);
  openList = signal<number | null>(null);
  phone = signal(this.loadPhone());
  name = 'Test Customer';
  text = '';
  correcting = signal<number | null>(null);
  better = '';
  get quick() {
    switch (this.restaurants.current?.businessType) {
      case 'Shop': return ['Hi', 'What products do you have?', 'Do you deliver to Karachi?', 'Is cash on delivery available?', 'I want to order', 'confirm', 'Where is my order?'];
      case 'Services': return ['Hi', 'What services do you offer?', 'How much is a haircut?', 'Book an appointment tomorrow at 5 pm', 'Where are you located?'];
      case 'Other': return ['Hi', 'What do you offer?', 'What are your prices?', 'Where are you located?', 'I want to talk to someone'];
      default: return ['Hi', 'Send me the menu', 'How much is the karahi?', 'Do you deliver to DHA?', "What's in the family deal?",
        '1 full chicken karahi and 4 naan, Johar Town', 'confirm', 'Book a table for 4 tomorrow at 9 pm', 'Where is my order?', 'Order mil gaya'];
    }
  }

  startCorrect(i: number) {
    this.better = '';
    this.correcting.set(i);
  }

  /** "It should have said...": saved as a bot rule, used from the next message. */
  async saveCorrect(i: number) {
    const rid = this.rid();
    const all = this.lines();
    const bot = all[i];
    if (!rid || !bot) return;
    const asked = [...all.slice(0, i)].reverse().find(l => l.from === 'me')?.text ?? null;
    this.busy.set(true);
    try {
      await this.api.correctBot(rid, asked, bot.text, this.better.trim());
      this.correcting.set(null);
      this.push({ from: 'system', text: '✅ Learned. The bot will reply like this from the next message (see Bot training > Bot rules).' });
    } catch (e) {
      this.toasts.error(errorText(e));
    } finally {
      this.busy.set(false);
    }
  }
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

  /** A tapped WhatsApp button / list row: sent like the real webhook, with its id. */
  tap(o: ReplyOption) { this.openList.set(null); this.send(o.title, o.id); }

  async send(typed?: string, replyId?: string) {
    const text = (typed ?? this.text).trim();
    const rid = this.rid();
    if (!text || !rid) return;
    if (typed === undefined) this.text = '';
    this.push({ from: 'me', text });
    this.busy.set(true);
    try {
      const res = await this.api.simulate(rid, this.phone(), this.name, text, replyId);
      if (res.reply) this.push({ from: 'bot', text: res.reply, buttons: res.buttons,
        meta: `${res.usedAi ? 'AI' : 'Welcome reply (AI OFF)'} · ${(res.milliseconds / 1000).toFixed(1)}s` });
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
