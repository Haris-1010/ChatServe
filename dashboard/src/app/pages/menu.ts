import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Api, CurrentRestaurant, Deal, Faq, MenuItem, money, Zone } from '../core/api';
import { onRestaurant } from '../core/page';
import { errorText, Toasts } from '../core/toast';

type Kind = 'menu' | 'deals' | 'faqs' | 'zones';

@Component({
  selector: 'app-menu',
  imports: [FormsModule],
  template: `
    <div class="page-head"><h1>{{ t.items }}, deals, FAQs</h1></div>
    <p class="muted">The bot only answers from what is written here. Changes apply from the next customer message.</p>

    <div class="tabs">
      <button [class.active]="tab() === 'menu'" (click)="tab.set('menu')">{{ t.icon }} {{ t.items }} ({{ menu().length }})</button>
      <button [class.active]="tab() === 'deals'" (click)="tab.set('deals')">🎁 Deals ({{ deals().length }})</button>
      <button [class.active]="tab() === 'faqs'" (click)="tab.set('faqs')">❓ FAQs ({{ faqs().length }})</button>
      <button [class.active]="tab() === 'zones'" (click)="tab.set('zones')">🛵 Delivery areas ({{ zones().length }})</button>
    </div>

    @switch (tab()) {
      @case ('menu') {
        <section class="card">
          <h2>New {{ t.item.toLowerCase() }}</h2>
          <form class="form-row" (ngSubmit)="add('menu', newItem)">
            <label class="field"><span>Category</span><input name="cat" [(ngModel)]="newItem.category" list="cats" [placeholder]="ex.category" /></label>
            <label class="field grow"><span>Name</span><input name="name" [(ngModel)]="newItem.name" [placeholder]="ex.name" /></label>
            <label class="field grow"><span>Description (optional)</span><input name="desc" [(ngModel)]="newItem.description" /></label>
            <label class="field w-sm"><span>Price</span><input name="price" type="number" min="0" [(ngModel)]="newItem.price" /></label>
            <button class="btn primary">+ Add</button>
          </form>
          <datalist id="cats">@for (c of categories(); track c) { <option [value]="c"></option> }</datalist>
        </section>

        @for (cat of categories(); track cat) {
          <section class="card flush">
            <h2 class="pad">{{ cat }}</h2>
            <table class="table">
              <thead><tr><th>{{ t.item }}</th><th class="hide-sm">Description</th><th class="num">Price</th><th>Available</th><th></th></tr></thead>
              <tbody>
                @for (m of byCategory(cat); track m.id) {
                  @if (editing() === 'menu-' + m.id) {
                    <tr class="editing">
                      <td><input [(ngModel)]="draft.name" /><input class="mt" [(ngModel)]="draft.category" list="cats" placeholder="Category" /></td>
                      <td class="hide-sm"><input [(ngModel)]="draft.description" /></td>
                      <td class="num"><input type="number" class="w-sm" [(ngModel)]="draft.price" /></td>
                      <td><input type="checkbox" [(ngModel)]="draft.isAvailable" /></td>
                      <td class="actions"><button class="btn primary sm" (click)="save('menu', m.id)">Save</button><button class="btn ghost sm" (click)="editing.set(null)">✕</button></td>
                    </tr>
                  } @else {
                    <tr [class.dim]="!m.isAvailable">
                      <td>{{ m.name }}</td>
                      <td class="muted hide-sm">{{ m.description }}</td>
                      <td class="num">{{ money(m.price) }}</td>
                      <td><label class="switch"><input type="checkbox" [checked]="m.isAvailable" (change)="toggle(m)" /><span></span></label></td>
                      <td class="actions"><button class="btn ghost sm" (click)="edit('menu', m)">Edit</button><button class="btn danger-ghost sm" (click)="del('menu', m.id, m.name)">🗑</button></td>
                    </tr>
                  }
                }
              </tbody>
            </table>
          </section>
        } @empty { <div class="card empty muted">Nothing here yet. Add your first {{ t.item.toLowerCase() }} above.</div> }
      }

      @case ('deals') {
        <section class="card">
          <h2>New deal</h2>
          <form class="form-row" (ngSubmit)="add('deals', newDeal)">
            <label class="field"><span>Name</span><input name="n" [(ngModel)]="newDeal.name" placeholder="Family Deal" /></label>
            <label class="field grow"><span>What's included</span><input name="d" [(ngModel)]="newDeal.details" [placeholder]="ex.deal" /></label>
            <label class="field w-sm"><span>Price</span><input name="p" type="number" min="0" [(ngModel)]="newDeal.price" /></label>
            <button class="btn primary">+ Add</button>
          </form>
        </section>
        <section class="card flush">
          <table class="table">
            <thead><tr><th>Deal</th><th>Details</th><th class="num">Price</th><th>Active</th><th></th></tr></thead>
            <tbody>
              @for (d of deals(); track d.id) {
                @if (editing() === 'deals-' + d.id) {
                  <tr class="editing">
                    <td><input [(ngModel)]="draft.name" /></td><td><input [(ngModel)]="draft.details" /></td>
                    <td class="num"><input type="number" class="w-sm" [(ngModel)]="draft.price" /></td>
                    <td><input type="checkbox" [(ngModel)]="draft.isActive" /></td>
                    <td class="actions"><button class="btn primary sm" (click)="save('deals', d.id)">Save</button><button class="btn ghost sm" (click)="editing.set(null)">✕</button></td>
                  </tr>
                } @else {
                  <tr [class.dim]="!d.isActive">
                    <td><b>{{ d.name }}</b></td><td class="muted">{{ d.details }}</td><td class="num">{{ money(d.price) }}</td>
                    <td>{{ d.isActive ? '✅' : '—' }}</td>
                    <td class="actions"><button class="btn ghost sm" (click)="edit('deals', d)">Edit</button><button class="btn danger-ghost sm" (click)="del('deals', d.id, d.name)">🗑</button></td>
                  </tr>
                }
              } @empty { <tr><td colspan="5" class="muted">No deals yet.</td></tr> }
            </tbody>
          </table>
        </section>
      }

      @case ('faqs') {
        <section class="card">
          <h2>New question / answer</h2>
          <form class="form-col" (ngSubmit)="add('faqs', newFaq)">
            <label class="field"><span>Question</span><input name="q" [(ngModel)]="newFaq.question" [placeholder]="ex.faq" /></label>
            <label class="field"><span>Answer</span><textarea name="a" rows="2" [(ngModel)]="newFaq.answer"></textarea></label>
            <div><button class="btn primary">+ Add</button></div>
          </form>
        </section>
        @for (f of faqs(); track f.id) {
          <section class="card faq">
            @if (editing() === 'faqs-' + f.id) {
              <label class="field"><span>Question</span><input [(ngModel)]="draft.question" /></label>
              <label class="field"><span>Answer</span><textarea rows="3" [(ngModel)]="draft.answer"></textarea></label>
              <div class="row"><button class="btn primary sm" (click)="save('faqs', f.id)">Save</button><button class="btn ghost sm" (click)="editing.set(null)">Cancel</button></div>
            } @else {
              <div class="faq-q">{{ f.question }}</div>
              <div class="faq-a">{{ f.answer }}</div>
              <div class="row"><button class="btn ghost sm" (click)="edit('faqs', f)">Edit</button><button class="btn danger-ghost sm" (click)="del('faqs', f.id, f.question)">🗑 Delete</button></div>
            }
          </section>
        } @empty { <div class="card empty muted">No FAQs yet.</div> }
      }

      @case ('zones') {
        <section class="card">
          <h2>New delivery area</h2>
          <p class="muted small">The bot only takes delivery orders for these areas, and adds this charge to the order.</p>
          <form class="form-row" (ngSubmit)="add('zones', newZone)">
            <label class="field grow"><span>Area</span><input name="z" [(ngModel)]="newZone.name" placeholder="Johar Town" /></label>
            <label class="field w-sm"><span>Delivery charge</span><input name="c" type="number" min="0" [(ngModel)]="newZone.charge" /></label>
            <button class="btn primary">+ Add</button>
          </form>
        </section>
        <section class="card flush">
          <table class="table">
            <thead><tr><th>Area</th><th class="num">Charge</th><th>Active</th><th></th></tr></thead>
            <tbody>
              @for (z of zones(); track z.id) {
                @if (editing() === 'zones-' + z.id) {
                  <tr class="editing">
                    <td><input [(ngModel)]="draft.name" /></td>
                    <td class="num"><input type="number" class="w-sm" [(ngModel)]="draft.charge" /></td>
                    <td><input type="checkbox" [(ngModel)]="draft.isActive" /></td>
                    <td class="actions"><button class="btn primary sm" (click)="save('zones', z.id)">Save</button><button class="btn ghost sm" (click)="editing.set(null)">✕</button></td>
                  </tr>
                } @else {
                  <tr [class.dim]="!z.isActive">
                    <td>{{ z.name }}</td><td class="num">{{ money(z.charge) }}</td><td>{{ z.isActive ? '✅' : '—' }}</td>
                    <td class="actions"><button class="btn ghost sm" (click)="edit('zones', z)">Edit</button><button class="btn danger-ghost sm" (click)="del('zones', z.id, z.name)">🗑</button></td>
                  </tr>
                }
              } @empty { <tr><td colspan="4" class="muted">No areas yet: the bot can't take delivery orders.</td></tr> }
            </tbody>
          </table>
        </section>
      }
    }
  `,
})
export class MenuPage {
  private api = inject(Api);
  private toasts = inject(Toasts);
  private restaurants = inject(CurrentRestaurant);
  protected get t() { return this.restaurants.terms; }
  /** Example placeholders that fit the kind of business. */
  protected get ex() {
    switch (this.restaurants.current?.businessType) {
      case 'Shop': return { category: 'Lawn suits', name: '3-piece printed lawn', deal: '2 suits + free dupatta', faq: 'Do you have cash on delivery?' };
      case 'Services': return { category: 'Hair', name: 'Haircut + blow dry', deal: 'Bridal package', faq: 'Do I need an appointment?' };
      case 'Other': return { category: 'Category', name: 'Item name', deal: 'What the deal includes', faq: 'A common question' };
      default: return { category: 'Karahi', name: 'Chicken Karahi (Half)', deal: 'Karahi Full + 4 Naan + 1.5L Drink', faq: 'Is the food halal?' };
    }
  }
  tab = signal<Kind>('menu');
  menu = signal<MenuItem[]>([]);
  deals = signal<Deal[]>([]);
  faqs = signal<Faq[]>([]);
  zones = signal<Zone[]>([]);
  editing = signal<string | null>(null);
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  draft: any = {};
  newItem = { category: '', name: '', description: '', price: 0, isAvailable: true };
  newDeal = { name: '', details: '', price: 0, isActive: true };
  newFaq = { question: '', answer: '' };
  newZone = { name: '', charge: 0, isActive: true };
  money = money;
  categories = computed(() => [...new Set(this.menu().map(m => m.category))]);
  private current = onRestaurant(rid => this.load(rid));
  private rid = computed(() => this.current.id()!);

  byCategory(cat: string) { return this.menu().filter(m => m.category === cat); }

  async load(rid: number) {
    try {
      const [menu, deals, faqs, zones] = await Promise.all([
        this.api.list<MenuItem>(rid, 'menu'), this.api.list<Deal>(rid, 'deals'),
        this.api.list<Faq>(rid, 'faqs'), this.api.list<Zone>(rid, 'zones'),
      ]);
      this.menu.set(menu); this.deals.set(deals); this.faqs.set(faqs); this.zones.set(zones);
    } catch (e) {
      this.toasts.error(errorText(e));
    }
  }

  edit(kind: Kind, row: object) {
    this.draft = { ...row };
    this.editing.set(`${kind}-${(row as { id: number }).id}`);
  }

  async add(kind: Kind, body: Record<string, unknown>) {
    try {
      await this.api.create(this.rid(), kind, body);
      this.toasts.ok('Added');
      if (kind === 'menu') this.newItem = { ...this.newItem, name: '', description: '', price: 0 };
      if (kind === 'deals') this.newDeal = { name: '', details: '', price: 0, isActive: true };
      if (kind === 'faqs') this.newFaq = { question: '', answer: '' };
      if (kind === 'zones') this.newZone = { name: '', charge: 0, isActive: true };
      await this.load(this.rid());
    } catch (e) {
      this.toasts.error(errorText(e));
    }
  }

  async save(kind: Kind, id: number) {
    try {
      await this.api.update(this.rid(), kind, id, this.draft);
      this.editing.set(null);
      this.toasts.ok('Saved');
      await this.load(this.rid());
    } catch (e) {
      this.toasts.error(errorText(e));
    }
  }

  async toggle(m: MenuItem) {
    try {
      await this.api.update(this.rid(), 'menu', m.id, { ...m, isAvailable: !m.isAvailable });
      this.toasts.ok(`${m.name}: ${m.isAvailable ? 'unavailable' : 'available'}`);
      await this.load(this.rid());
    } catch (e) {
      this.toasts.error(errorText(e));
    }
  }

  async del(kind: Kind, id: number, label: string) {
    if (!confirm(`Delete "${label}"?`)) return;
    try {
      await this.api.remove(this.rid(), kind, id);
      this.toasts.ok('Deleted');
      await this.load(this.rid());
    } catch (e) {
      this.toasts.error(errorText(e));
    }
  }
}
