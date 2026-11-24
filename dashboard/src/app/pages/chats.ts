import { Component, computed, ElementRef, inject, signal, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Api, ChatMessage, Conversation, when } from '../core/api';
import { onRestaurant } from '../core/page';
import { errorText, Toasts } from '../core/toast';

@Component({
  selector: 'app-chats',
  imports: [FormsModule],
  template: `
    <div class="page-head"><h1>Chats</h1></div>
    <div class="chat-layout card flush" [class.has-selection]="selected()">
      <aside class="conv-list">
        <input class="search" placeholder="Search name or number…" [(ngModel)]="search" />
        @for (c of filtered(); track c.id) {
          <button class="conv" [class.active]="selected()?.id === c.id" (click)="open(c)">
            <div class="conv-top">
              <b>{{ c.name || ('+' + c.phone) }}</b>
              <span class="muted small">{{ when(c.lastMessageAt) }}</span>
            </div>
            <div class="conv-bottom">
              <span class="muted small ellipsis">{{ c.lastRole === 'Customer' ? '' : (c.lastRole === 'Bot' ? '🤖 ' : '👤 ') }}{{ c.lastText }}</span>
              @if (isPaused(c)) { <span class="pill warn xs">Staff</span> }
              @if (c.isSimulated) { <span class="pill xs">Test</span> }
            </div>
          </button>
        } @empty {
          <p class="muted pad">No chats yet.</p>
        }
      </aside>

      <section class="thread">
        @if (selected(); as c) {
          <header class="thread-head">
            <button class="btn ghost sm back" (click)="selected.set(null)">←</button>
            <div>
              <b>{{ c.name || 'Customer' }}</b><span class="phone-no">+{{ c.phone }}</span>
              <div class="small" [class.warn-text]="isPaused(c)">
                {{ isPaused(c) ? 'Bot paused (staff is replying) until ' + when(c.botPausedUntil) : 'Bot is replying' }}
              </div>
            </div>
            <div class="spacer"></div>
            @if (isPaused(c)) {
              <button class="btn sm" (click)="resume(c)">▶ Turn bot back on</button>
            } @else {
              <button class="btn sm" (click)="pause(c)">⏸ Pause bot (I'll reply)</button>
            }
          </header>

          <div class="messages" #scroller>
            @for (m of messages(); track m.id) {
              <div class="bubble-row" [class.out]="m.role !== 'Customer'">
                <div class="bubble" [class.bot]="m.role === 'Bot'" [class.staff]="m.role === 'Staff'">
                  @if (m.role !== 'Customer') { <div class="who">{{ m.role === 'Bot' ? '🤖 Bot' : '👤 Staff' }}</div> }
                  <div class="text">{{ m.text }}</div>
                  <div class="meta">{{ when(m.createdAt) }}@if (m.outputTokens) { · {{ m.inputTokens }}/{{ m.outputTokens }} tokens }</div>
                </div>
              </div>
            }
          </div>

          <form class="composer" (ngSubmit)="send(c)">
            <textarea name="reply" rows="2" [(ngModel)]="reply" placeholder="Write a reply as staff…"
              (keydown.enter)="$event.preventDefault(); send(c)"></textarea>
            <div class="composer-side">
              <label class="check small"><input type="checkbox" name="pause" [(ngModel)]="pauseOnSend" /> Pause bot</label>
              <button class="btn primary" [disabled]="sending() || !reply.trim()">Send</button>
            </div>
          </form>
          @if (c.isSimulated) { <p class="muted small pad">This is a simulator test chat: staff messages are only saved here and are not sent on WhatsApp.</p> }
        } @else {
          <div class="empty-thread muted">Select a chat on the left</div>
        }
      </section>
    </div>
  `,
})
export class ChatsPage {
  private api = inject(Api);
  private toasts = inject(Toasts);
  conversations = signal<Conversation[]>([]);
  selected = signal<Conversation | null>(null);
  messages = signal<ChatMessage[]>([]);
  sending = signal(false);
  search = '';
  reply = '';
  pauseOnSend = true;
  when = when;
  private scroller = viewChild<ElementRef<HTMLElement>>('scroller');
  private current = onRestaurant(rid => this.load(rid), 5);
  private rid = computed(() => this.current.id()!);

  filtered() {
    const s = this.search.trim().toLowerCase();
    return s ? this.conversations().filter(c => c.phone.includes(s) || (c.name ?? '').toLowerCase().includes(s)) : this.conversations();
  }

  isPaused(c: Conversation) { return !!c.botPausedUntil && new Date(c.botPausedUntil) > new Date(); }

  async load(rid: number) {
    try {
      const list = await this.api.conversations(rid);
      this.conversations.set(list);
      const sel = this.selected();
      if (sel) {
        const fresh = list.find(c => c.id === sel.id);
        if (!fresh) { this.selected.set(null); return; }
        this.selected.set(fresh);
        await this.loadMessages(fresh, false);
      }
    } catch (e) {
      this.toasts.error(errorText(e));
    }
  }

  async open(c: Conversation) {
    this.selected.set(c);
    this.messages.set([]);
    await this.loadMessages(c, true);
  }

  private async loadMessages(c: Conversation, scroll: boolean) {
    const msgs = await this.api.messages(this.rid(), c.id);
    const grew = msgs.length !== this.messages().length;
    this.messages.set(msgs);
    if (scroll || grew) setTimeout(() => { const el = this.scroller()?.nativeElement; if (el) el.scrollTop = el.scrollHeight; });
  }

  async send(c: Conversation) {
    const text = this.reply.trim();
    if (!text) return;
    this.sending.set(true);
    try {
      await this.api.sendStaffMessage(this.rid(), c.id, text, this.pauseOnSend);
      this.reply = '';
      await this.load(this.rid());
      await this.loadMessages(this.selected()!, true);
    } catch (e) {
      this.toasts.error(errorText(e));
    } finally {
      this.sending.set(false);
    }
  }

  async pause(c: Conversation) {
    try { await this.api.pauseBot(this.rid(), c.id); this.toasts.ok('Bot paused for this chat'); await this.load(this.rid()); }
    catch (e) { this.toasts.error(errorText(e)); }
  }

  async resume(c: Conversation) {
    try { await this.api.resumeBot(this.rid(), c.id); this.toasts.ok('Bot turned back on'); await this.load(this.rid()); }
    catch (e) { this.toasts.error(errorText(e)); }
  }
}
