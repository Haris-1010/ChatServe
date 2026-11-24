import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Api, Customer, downloadCsv, money, when } from '../core/api';
import { onRestaurant } from '../core/page';
import { errorText, Toasts } from '../core/toast';

@Component({
  selector: 'app-customers',
  imports: [FormsModule],
  template: `
    <div class="page-head">
      <h1>Customers</h1>
      <div class="row">
        <input class="search inline" placeholder="Naam ya number…" [(ngModel)]="search" (keyup.enter)="load(rid())" />
        <button class="btn ghost" (click)="exportCsv()">⬇ CSV (Excel)</button>
      </div>
    </div>
    <section class="card flush">
      <table class="table">
        <thead><tr><th>Customer</th><th class="num">Orders</th><th class="num">Kharcha</th><th class="hide-sm">Messages</th><th>Aakhri message</th><th>Bot</th></tr></thead>
        <tbody>
          @for (c of customers(); track c.id) {
            <tr>
              <td><b>{{ c.name || '—' }}</b> @if (c.isSimulated) { <span class="pill xs">Test</span> }<div class="muted small">+{{ c.phone }}</div></td>
              <td class="num">{{ c.orders }}</td>
              <td class="num">{{ money(c.totalSpent) }}</td>
              <td class="hide-sm">{{ c.messages }}</td>
              <td class="muted">{{ when(c.lastMessageAt) }}</td>
              <td>
                @if (paused(c)) {
                  <button class="btn sm" (click)="resume(c)" title="Bot paused hai">▶ ON karein</button>
                } @else {
                  <button class="btn ghost sm" (click)="pause(c)">⏸ Rokein</button>
                }
              </td>
            </tr>
          } @empty { <tr><td colspan="6" class="muted">Koi customer nahi.</td></tr> }
        </tbody>
      </table>
    </section>
    <p class="muted small">Customer labels (Ordered / Interested / Fake), no-shows aur blacklist Phase 4 mein aayenge.</p>
  `,
})
export class CustomersPage {
  private api = inject(Api);
  private toasts = inject(Toasts);
  customers = signal<Customer[]>([]);
  search = '';
  money = money;
  when = when;
  private current = onRestaurant(rid => this.load(rid));
  rid = computed(() => this.current.id()!);

  paused(c: Customer) { return !!c.botPausedUntil && new Date(c.botPausedUntil) > new Date(); }

  async load(rid: number) {
    try { this.customers.set(await this.api.customers(rid, this.search)); } catch (e) { this.toasts.error(errorText(e)); }
  }

  async pause(c: Customer) {
    try { await this.api.pauseBot(this.rid(), c.id); this.toasts.ok('Bot ruk gaya'); await this.load(this.rid()); }
    catch (e) { this.toasts.error(errorText(e)); }
  }

  async resume(c: Customer) {
    try { await this.api.resumeBot(this.rid(), c.id); this.toasts.ok('Bot wapas ON'); await this.load(this.rid()); }
    catch (e) { this.toasts.error(errorText(e)); }
  }

  exportCsv() {
    downloadCsv(`customers-${new Date().toISOString().slice(0, 10)}.csv`, [
      ['Name', 'Phone', 'Orders', 'Total spent', 'Messages', 'First seen', 'Last message'],
      ...this.customers().map(c => [c.name, c.phone, c.orders, c.totalSpent, c.messages,
        new Date(c.firstSeenAt).toLocaleString('en-PK'), new Date(c.lastMessageAt).toLocaleString('en-PK')]),
    ]);
  }
}
