import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Api, BusinessType, CurrentRestaurant, RestaurantProfile, termsFor } from '../core/api';
import { Auth } from '../core/auth';
import { onRestaurant } from '../core/page';
import { errorText, Toasts } from '../core/toast';
import { WhatsAppConnectionCard } from './whatsapp-connection';

@Component({
  selector: 'app-restaurant',
  imports: [FormsModule, RouterLink, WhatsAppConnectionCard],
  template: `
    <div class="page-head"><h1>Business info</h1></div>

    @if (p(); as r) {
      <form class="card form-col" (ngSubmit)="save(r)">
        <div class="bot-toggle">
          <label class="switch big"><input type="checkbox" name="bot" [(ngModel)]="r.isBotEnabled" /><span></span></label>
          <div><b>Bot {{ r.isBotEnabled ? 'ON' : 'OFF' }}</b>
            <div class="muted small">When OFF, the bot replies to no one. Messages are only saved in Chats (staff reply themselves).</div></div>
        </div>

        <div class="field">
          <span>What kind of business is this?</span>
          <div class="types">
            @for (t of types; track t.value) {
              <button type="button" class="type" [class.on]="r.businessType === t.value" (click)="r.businessType = t.value">
                <span class="emoji">{{ t.icon }}</span><b>{{ t.label }}</b><span class="muted small">{{ t.hint }}</span>
              </button>
            }
          </div>
          <small>The bot's words change with this: menu and table bookings for a restaurant, products for a shop, services and appointments for a salon or clinic.</small>
        </div>
        <label class="field"><span>About the business (one or two lines)</span>
          <input name="about" [(ngModel)]="r.businessDescription" [placeholder]="aboutHint(r.businessType)" />
          <small>Tells the bot what you do. Optional.</small></label>

        <div class="grid-2">
          <label class="check"><input type="checkbox" name="orders" [(ngModel)]="r.takesOrders" />
            <span><b>Bot takes orders</b><br /><span class="muted small">Off: the bot answers questions, and a team member takes the order.</span></span></label>
          <label class="check"><input type="checkbox" name="bookings" [(ngModel)]="r.takesBookings" />
            <span><b>Bot takes {{ r.businessType === 'Restaurant' ? 'table bookings' : 'appointments / bookings' }}</b><br /><span class="muted small">Requests come to your staff number to confirm.</span></span></label>
        </div>

        <div class="grid-2">
          <label class="field"><span>Business name</span><input name="name" [(ngModel)]="r.name" /></label>
          <label class="field"><span>WhatsApp Phone number ID</span><input name="pnid" [(ngModel)]="r.whatsAppPhoneNumberId" [readonly]="!auth.isAdmin()" />
            <small>The ID from Meta > API Setup. It tells the app which business a message belongs to.@if (!auth.isAdmin()) { Only the admin can change it. }</small></label>
          <label class="field"><span>Staff WhatsApp numbers</span><input name="staff" [(ngModel)]="r.staffWhatsAppNumber" placeholder="923001234567, 923331234567" />
            <small>One or more numbers, separated by commas (manager, kitchen, rider). Every number gets the order tickets with buttons (👨‍🍳 Prep, 🛵 Rider gaya, ✅ Delivered) to run the orders from WhatsApp; typing "prep 12" or "orders" also works. Each number should send "start" to the business number once a day (24-hour window).</small></label>
          <label class="field"><span>Currency</span><input name="cur" [(ngModel)]="r.currency" /></label>
          <label class="field"><span>Address</span><input name="addr" [(ngModel)]="r.address" /></label>
          <label class="field"><span>Timings</span><input name="time" [(ngModel)]="r.timings" placeholder="Daily 12 PM to 2 AM" /></label>
          <label class="field"><span>Minimum order</span><input name="min" type="number" min="0" [(ngModel)]="r.minimumOrder" /></label>
          <label class="field"><span>Payment methods</span><input name="pay" [(ngModel)]="r.paymentMethods" placeholder="Cash on delivery, JazzCash, Easypaisa" /></label>
        </div>
        <label class="field"><span>Bot tone</span><input name="tone" [(ngModel)]="r.botTone" placeholder="Friendly, polite, short" /></label>
        <label class="field"><span>Delivery notes</span><textarea name="del" rows="2" [(ngModel)]="r.deliveryAreas"></textarea>
          <small>Areas and charges go in "{{ terms(r.businessType).items }} > Delivery areas". Put only general notes here (e.g. "Delivery takes 40-50 minutes").</small></label>
        <label class="field"><span>Extra info (things the bot should know)</span><textarea name="extra" rows="3" [(ngModel)]="r.extraInfo"></textarea></label>
        <div><button class="btn primary" [disabled]="busy()">Save</button></div>
      </form>
    }

    @if (auth.isAdmin() && current.id(); as id) {
      <app-whatsapp-connection [rid]="id" />
    }

    @if (auth.isAdmin()) {
      <p class="muted small">To add another business or branch, or a login for this client, go to <a routerLink="/clients">All clients</a>.</p>
    }
  `,
  styles: [`
    .types { display: grid; grid-template-columns: repeat(auto-fit, minmax(150px, 1fr)); gap: 8px; margin-top: 4px; }
    .type { display: flex; flex-direction: column; align-items: flex-start; gap: 2px; text-align: left; padding: 10px 12px;
      border: 1.5px solid var(--border, #ddd); border-radius: 10px; background: transparent; cursor: pointer; color: inherit; font: inherit; }
    .type.on { border-color: var(--primary, #10b981); background: var(--primary-soft, rgba(16,185,129,.08)); }
    .type .emoji { font-size: 20px; }
    .check { display: flex; gap: 10px; align-items: flex-start; cursor: pointer; }
    .check input { margin-top: 4px; }
  `],
})
export class RestaurantPage {
  private api = inject(Api);
  private toasts = inject(Toasts);
  private restaurants = inject(CurrentRestaurant);
  protected auth = inject(Auth);
  p = signal<RestaurantProfile | null>(null);
  busy = signal(false);
  protected terms = termsFor;
  protected types: { value: BusinessType; icon: string; label: string; hint: string }[] = [
    { value: 'Restaurant', icon: '🍛', label: 'Restaurant / food', hint: 'Menu, delivery, table booking' },
    { value: 'Shop', icon: '🛍️', label: 'Shop / online store', hint: 'Products, delivery, pickup' },
    { value: 'Services', icon: '💼', label: 'Services', hint: 'Salon, clinic, gym: appointments' },
    { value: 'Other', icon: '📦', label: 'Other', hint: 'Anything else' },
  ];
  protected aboutHint(t: BusinessType) {
    return { Restaurant: 'Desi food and BBQ, Johar Town Lahore', Shop: 'Ladies clothing brand, delivers all over Pakistan',
      Services: 'Beauty salon for ladies, DHA Lahore', Other: 'What do you sell or offer?' }[t] ?? '';
  }
  protected current = onRestaurant(rid => this.load(rid));
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
}
