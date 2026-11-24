import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Api, CurrentRestaurant, RestaurantProfile } from '../core/api';
import { onRestaurant } from '../core/page';
import { errorText, Toasts } from '../core/toast';

@Component({
  selector: 'app-restaurant',
  imports: [FormsModule],
  template: `
    <div class="page-head"><h1>Restaurant info</h1></div>

    @if (p(); as r) {
      <form class="card form-col" (ngSubmit)="save(r)">
        <div class="bot-toggle">
          <label class="switch big"><input type="checkbox" name="bot" [(ngModel)]="r.isBotEnabled" /><span></span></label>
          <div><b>Bot {{ r.isBotEnabled ? 'ON' : 'OFF' }}</b>
            <div class="muted small">When OFF, the bot replies to no one. Messages are only saved in Chats (staff reply themselves).</div></div>
        </div>

        <div class="grid-2">
          <label class="field"><span>Restaurant name</span><input name="name" [(ngModel)]="r.name" /></label>
          <label class="field"><span>WhatsApp Phone number ID</span><input name="pnid" [(ngModel)]="r.whatsAppPhoneNumberId" />
            <small>The ID from Meta > API Setup. It tells the app which restaurant a message belongs to.</small></label>
          <label class="field"><span>Staff / kitchen WhatsApp number</span><input name="staff" [(ngModel)]="r.staffWhatsAppNumber" placeholder="923001234567" />
            <small>Confirmed orders, bookings and handovers are sent here. Staff should send "start" to the business number once a day (24-hour window).</small></label>
          <label class="field"><span>Currency</span><input name="cur" [(ngModel)]="r.currency" /></label>
          <label class="field"><span>Address</span><input name="addr" [(ngModel)]="r.address" /></label>
          <label class="field"><span>Timings</span><input name="time" [(ngModel)]="r.timings" placeholder="Daily 12 PM to 2 AM" /></label>
          <label class="field"><span>Minimum order</span><input name="min" type="number" min="0" [(ngModel)]="r.minimumOrder" /></label>
          <label class="field"><span>Payment methods</span><input name="pay" [(ngModel)]="r.paymentMethods" placeholder="Cash on delivery, JazzCash, Easypaisa" /></label>
        </div>
        <label class="field"><span>Bot tone</span><input name="tone" [(ngModel)]="r.botTone" placeholder="Friendly, polite, short" /></label>
        <label class="field"><span>Delivery notes</span><textarea name="del" rows="2" [(ngModel)]="r.deliveryAreas"></textarea>
          <small>Areas and charges go in "Menu > Delivery areas". Put only general notes here (e.g. "Delivery takes 40-50 minutes").</small></label>
        <label class="field"><span>Extra info (things the bot should know)</span><textarea name="extra" rows="3" [(ngModel)]="r.extraInfo"></textarea></label>
        <div><button class="btn primary" [disabled]="busy()">Save</button></div>
      </form>
    }

    <section class="card">
      <h2>New restaurant (multi-tenant)</h2>
      <p class="muted small">Each restaurant has its own WhatsApp number (Phone number ID). One app can run many restaurants.</p>
      <form class="form-row" (ngSubmit)="create()">
        <label class="field grow"><span>Name</span><input name="nn" [(ngModel)]="newName" /></label>
        <label class="field grow"><span>Phone number ID</span><input name="np" [(ngModel)]="newPhoneId" /></label>
        <button class="btn">+ Create</button>
      </form>
    </section>
  `,
})
export class RestaurantPage {
  private api = inject(Api);
  private toasts = inject(Toasts);
  private restaurants = inject(CurrentRestaurant);
  p = signal<RestaurantProfile | null>(null);
  busy = signal(false);
  newName = '';
  newPhoneId = '';
  private current = onRestaurant(rid => this.load(rid));
  private rid = computed(() => this.current.id()!);

  async load(rid: number) {
    try { this.p.set(await this.api.restaurant(rid)); } catch (e) { this.toasts.error(errorText(e)); }
  }

  async save(r: RestaurantProfile) {
    this.busy.set(true);
    try {
      this.p.set(await this.api.saveRestaurant(this.rid(), { ...r, minimumOrder: r.minimumOrder || null }));
      await this.restaurants.load();
      this.toasts.ok('Saved. The bot will use the new data from the next message.');
    } catch (e) {
      this.toasts.error(errorText(e));
    } finally {
      this.busy.set(false);
    }
  }

  async create() {
    try {
      const res = await this.api.createRestaurant(this.newName, this.newPhoneId);
      await this.restaurants.load();
      this.restaurants.select(res.id);
      this.newName = this.newPhoneId = '';
      this.toasts.ok('Restaurant created. Now fill in the menu and info.');
    } catch (e) {
      this.toasts.error(errorText(e));
    }
  }
}
