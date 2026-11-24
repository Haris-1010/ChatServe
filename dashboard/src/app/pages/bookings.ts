import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Api, Booking, BookingStatus } from '../core/api';
import { onRestaurant } from '../core/page';
import { errorText, Toasts } from '../core/toast';

@Component({
  selector: 'app-bookings',
  imports: [FormsModule],
  template: `
    <div class="page-head">
      <h1>Table bookings</h1>
      <label class="check"><input type="checkbox" [(ngModel)]="notify" /> Customer ko WhatsApp par batayen</label>
    </div>
    <div class="tabs">
      <button [class.active]="upcoming()" (click)="setUpcoming(true)">Aane wali</button>
      <button [class.active]="!upcoming()" (click)="setUpcoming(false)">Sab</button>
    </div>
    <section class="card flush">
      <table class="table">
        <thead><tr><th>Waqt</th><th>Naam</th><th>Afraad</th><th class="hide-sm">Note</th><th>Status</th><th></th></tr></thead>
        <tbody>
          @for (b of bookings(); track b.id) {
            <tr [class.dim]="b.status === 'Cancelled'">
              <td><b>{{ fmt(b.bookingTime) }}</b></td>
              <td>{{ b.customerName }}<div class="muted small">+{{ b.phone }}</div></td>
              <td>{{ b.people }}</td>
              <td class="muted hide-sm">{{ b.notes }}</td>
              <td><span class="badge b-{{ b.status }}">{{ label[b.status] }}</span></td>
              <td class="actions">
                @if (b.status !== 'Confirmed') { <button class="btn primary sm" (click)="set(b, 'Confirmed')">Confirm</button> }
                @if (b.status !== 'Cancelled') { <button class="btn danger-ghost sm" (click)="set(b, 'Cancelled')">Cancel</button> }
              </td>
            </tr>
          } @empty { <tr><td colspan="6" class="muted">Koi booking nahi. Customer WhatsApp par booking maange to bot yahan daal deta hai.</td></tr> }
        </tbody>
      </table>
    </section>
  `,
})
export class BookingsPage {
  private api = inject(Api);
  private toasts = inject(Toasts);
  bookings = signal<Booking[]>([]);
  upcoming = signal(true);
  notify = true;
  label: Record<BookingStatus, string> = { Requested: 'Nayi request', Confirmed: 'Confirmed', Cancelled: 'Cancelled' };
  private current = onRestaurant(rid => this.load(rid), 30);
  private rid = computed(() => this.current.id()!);

  fmt(iso: string) {
    return new Date(iso).toLocaleString('en-PK', { weekday: 'short', day: 'numeric', month: 'short', hour: 'numeric', minute: '2-digit' });
  }

  async load(rid: number) {
    try { this.bookings.set(await this.api.bookings(rid, this.upcoming())); } catch (e) { this.toasts.error(errorText(e)); }
  }

  setUpcoming(v: boolean) { this.upcoming.set(v); this.load(this.rid()); }

  async set(b: Booking, status: BookingStatus) {
    try {
      const res = await this.api.setBookingStatus(this.rid(), b.id, status, this.notify);
      if (res.notifyError) this.toasts.error('Status badal gaya, magar message nahi gaya: ' + res.notifyError);
      else this.toasts.ok('Booking ' + this.label[status]);
      await this.load(this.rid());
    } catch (e) {
      this.toasts.error(errorText(e));
    }
  }
}
