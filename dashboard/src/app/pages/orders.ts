import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Api, downloadCsv, money, Order, ORDER_STATUS_LABEL, ORDER_TYPE_LABEL, OrderStatus, when } from '../core/api';
import { onRestaurant } from '../core/page';
import { errorText, Toasts } from '../core/toast';

type Filter = 'active' | 'PendingConfirmation' | 'Completed' | 'Cancelled' | '';

/** The next step (button) after each status. */
const NEXT: Partial<Record<OrderStatus, { status: OrderStatus; label: string }[]>> = {
  PendingConfirmation: [{ status: 'Confirmed', label: 'Confirm manually' }],
  Confirmed: [{ status: 'Preparing', label: '👨‍🍳 Preparing' }],
  Preparing: [{ status: 'OutForDelivery', label: '🛵 Out for delivery' }, { status: 'Completed', label: '✅ Completed' }],
  OutForDelivery: [{ status: 'Completed', label: '✅ Delivered' }],
};

@Component({
  selector: 'app-orders',
  imports: [FormsModule],
  template: `
    <div class="page-head">
      <h1>Orders</h1>
      <div class="row">
        <label class="check"><input type="checkbox" [(ngModel)]="notify" /> Notify customer on WhatsApp when status changes</label>
        <button class="btn ghost" (click)="exportCsv()">⬇ CSV</button>
      </div>
    </div>

    <div class="tabs">
      @for (f of filters; track f.value) {
        <button [class.active]="filter() === f.value" (click)="setFilter(f.value)">{{ f.label }}</button>
      }
    </div>

    @if (orders().length === 0 && !loading()) {
      <div class="card empty"><p class="muted">No orders match this filter. This page refreshes every 10 seconds.</p></div>
    }

    <div class="order-grid">
      @for (o of orders(); track o.id) {
        <article class="card order">
          <div class="order-head">
            <div><b>#{{ o.id }}</b> · {{ typeLabel[o.type] }}</div>
            <span class="badge s-{{ o.status }}">{{ statusLabel[o.status] }}</span>
          </div>
          <div class="order-customer">
            <b>{{ o.customerName }}</b><span class="phone-no">+{{ o.phone }}</span>
            @if (o.type === 'Delivery') { <div class="small">📍 {{ o.deliveryAddress }} <span class="muted">({{ o.deliveryArea }})</span></div> }
          </div>
          <ul class="items">
            @for (i of o.items; track i.id) {
              <li><span>{{ i.quantity }} × {{ i.name }}@if (i.note) { <span class="muted small"> ({{ i.note }})</span> }</span><span>{{ money(i.lineTotal) }}</span></li>
            }
            @if (o.deliveryCharge) { <li class="muted"><span>Delivery</span><span>{{ money(o.deliveryCharge) }}</span></li> }
            <li class="total"><span>Total</span><span>{{ money(o.total) }}</span></li>
          </ul>
          @if (o.paymentMethod || o.notes) {
            <div class="small muted">
              @if (o.paymentMethod) { <div>💳 {{ o.paymentMethod }}</div> }
              @if (o.notes) { <div>📝 {{ o.notes }}</div> }
            </div>
          }
          <div class="small muted">{{ when(o.createdAt) }}@if (o.confirmedAt) { · confirmed {{ when(o.confirmedAt) }} }</div>
          <div class="order-actions">
            @for (n of next[o.status] ?? []; track n.status) {
              <button class="btn primary sm" [disabled]="busy() === o.id" (click)="setStatus(o, n.status)">{{ n.label }}</button>
            }
            @if (o.status !== 'Cancelled' && o.status !== 'Completed') {
              <button class="btn danger-ghost sm" [disabled]="busy() === o.id" (click)="cancel(o)">Cancel</button>
            }
          </div>
        </article>
      }
    </div>
  `,
})
export class OrdersPage {
  private api = inject(Api);
  private toasts = inject(Toasts);
  orders = signal<Order[]>([]);
  filter = signal<Filter>('active');
  loading = signal(true);
  busy = signal<number | null>(null);
  notify = true;
  money = money;
  when = when;
  statusLabel = ORDER_STATUS_LABEL;
  typeLabel = ORDER_TYPE_LABEL;
  next = NEXT;
  filters: { value: Filter; label: string }[] = [
    { value: 'active', label: 'Active' },
    { value: 'PendingConfirmation', label: 'Awaiting confirmation' },
    { value: 'Completed', label: 'Completed' },
    { value: 'Cancelled', label: 'Cancelled' },
    { value: '', label: 'All' },
  ];
  private current = onRestaurant(rid => this.load(rid), 10);
  private rid = computed(() => this.current.id()!);

  async load(rid: number) {
    try {
      this.orders.set(await this.api.orders(rid, this.filter() || undefined));
    } catch (e) {
      this.toasts.error(errorText(e));
    } finally {
      this.loading.set(false);
    }
  }

  setFilter(f: Filter) {
    this.filter.set(f);
    this.loading.set(true);
    this.load(this.rid());
  }

  async setStatus(o: Order, status: OrderStatus) {
    this.busy.set(o.id);
    try {
      const res = await this.api.setOrderStatus(this.rid(), o.id, status, this.notify);
      if (res.notifyError) this.toasts.error(`Status updated, but the customer message failed: ${res.notifyError}`);
      else this.toasts.ok(`Order #${o.id}: ${ORDER_STATUS_LABEL[status]}`);
      await this.load(this.rid());
    } catch (e) {
      this.toasts.error(errorText(e));
    } finally {
      this.busy.set(null);
    }
  }

  cancel(o: Order) {
    if (confirm(`Cancel order #${o.id}?`)) this.setStatus(o, 'Cancelled');
  }

  exportCsv() {
    downloadCsv(`orders-${new Date().toISOString().slice(0, 10)}.csv`, [
      ['Order', 'Status', 'Type', 'Customer', 'Phone', 'Area', 'Address', 'Items', 'Subtotal', 'Delivery', 'Total', 'Payment', 'Created'],
      ...this.orders().map(o => [
        o.id, ORDER_STATUS_LABEL[o.status], o.type, o.customerName, o.phone, o.deliveryArea, o.deliveryAddress,
        o.items.map(i => `${i.quantity}x ${i.name}`).join('; '), o.subtotal, o.deliveryCharge, o.total, o.paymentMethod,
        new Date(o.createdAt).toLocaleString('en-PK'),
      ]),
    ]);
  }
}
