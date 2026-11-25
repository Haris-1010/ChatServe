import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { API_BASE } from '../../environments';

// ---------- Types (match the backend JSON) ----------

export type OrderStatus = 'PendingConfirmation' | 'Confirmed' | 'Preparing' | 'OutForDelivery' | 'Completed' | 'Cancelled';
export type OrderType = 'Delivery' | 'Takeaway' | 'DineIn';
export type BookingStatus = 'Requested' | 'Confirmed' | 'Cancelled';
export type ChatRole = 'Customer' | 'Bot' | 'Staff';

export interface RestaurantSummary {
  id: number; name: string; whatsAppPhoneNumberId: string; isBotEnabled: boolean;
  organizationId: number; organizationName: string;
}

export interface RestaurantProfile {
  id: number; name: string; whatsAppPhoneNumberId: string; isBotEnabled: boolean;
  botTone: string | null; address: string | null; timings: string | null; deliveryAreas: string | null;
  minimumOrder: number | null; paymentMethods: string | null; extraInfo: string | null;
  currency: string; staffWhatsAppNumber: string | null;
}

export interface MenuItem { id: number; category: string; name: string; description: string | null; price: number; isAvailable: boolean; sortOrder: number; }
export interface Deal { id: number; name: string; details: string; price: number; isActive: boolean; }
export interface Faq { id: number; question: string; answer: string; }
export interface Zone { id: number; name: string; charge: number; isActive: boolean; }

export interface OrderItem { id: number; name: string; unitPrice: number; quantity: number; lineTotal: number; note: string | null; }
export interface Order {
  id: number; status: OrderStatus; type: OrderType; customerId: number; customerName: string; phone: string;
  deliveryAddress: string | null; deliveryArea: string | null; paymentMethod: string | null; notes: string | null;
  subtotal: number; deliveryCharge: number; total: number;
  createdAt: string; updatedAt: string; confirmedAt: string | null; items: OrderItem[];
}

export interface Booking {
  id: number; customerName: string; phone: string; bookingTime: string; people: number;
  notes: string | null; status: BookingStatus; createdAt: string;
}

export interface Customer {
  id: number; phone: string; name: string | null; firstSeenAt: string; lastMessageAt: string;
  botPausedUntil: string | null; orders: number; totalSpent: number; messages: number; isSimulated: boolean;
}

export interface Conversation {
  id: number; phone: string; name: string | null; lastMessageAt: string; botPausedUntil: string | null;
  lastRole: ChatRole | null; lastText: string | null; isSimulated: boolean;
}

export interface ChatMessage {
  id: number; role: ChatRole; text: string; createdAt: string;
  inputTokens: number | null; outputTokens: number | null; cacheReadTokens: number | null;
}

export interface Overview {
  ordersToday: number; revenueToday: number; activeOrders: number; awaitingConfirmation: number;
  customers: number; chatsToday: number; botRepliesToday: number; inputTokensToday: number; outputTokensToday: number;
  pausedChats: number; upcomingBookings: number;
  recentOrders: { id: number; customerName: string; status: OrderStatus; type: OrderType; total: number; createdAt: string }[];
}

export interface StatusCheck { name: string; state: 'ok' | 'warn' | 'error'; message: string; }
export interface Status { checks: StatusCheck[]; webhookUrl: string; environment: string; }

export interface Setting {
  key: string; group: string; label: string; kind: 'text' | 'secret' | 'bool' | 'number' | 'select' | 'textarea';
  help: string; options: string[] | null; value: string | null; isSet: boolean; hint: string | null;
  source: 'dashboard' | 'config' | 'default';
}
export interface SettingsResponse { settings: Setting[]; webhookUrl: string; database: string; }
export interface TestResult { ok: boolean; message: string; }

export type Role = 'admin' | 'owner';
export interface Me { username: string; role: Role; organizationId: number | null; organizationName: string | null; }

/** Admin "All clients" page: every client (organization) with its branches, today's numbers and logins. */
export interface ClientBranch {
  id: number; name: string; whatsAppPhoneNumberId: string; isBotEnabled: boolean;
  ordersToday: number; salesToday: number; activeOrders: number; chatsToday: number; lastMessageAt: string | null;
}
export interface ClientLogin { id: number; username: string; lastLoginAt: string | null; }
export interface Client {
  id: number; name: string; isActive: boolean; createdAt: string;
  ordersToday: number; salesToday: number; chatsToday: number;
  restaurants: ClientBranch[]; users: ClientLogin[];
}
export interface ClientsResponse {
  totals: { clientCount: number; activeClients: number; restaurants: number; ordersToday: number; salesToday: number; chatsToday: number };
  clients: Client[];
}
export interface NewClient {
  name: string; restaurantName?: string; whatsAppPhoneNumberId?: string; username?: string; password?: string;
}

export interface SimulatorResult {
  phone: string; reply: string | null; note: string | null; staffAlerts: string[]; usedAi: boolean; milliseconds: number;
}

// ---------- Labels ----------

export const ORDER_STATUS_LABEL: Record<OrderStatus, string> = {
  PendingConfirmation: 'Awaiting confirmation',
  Confirmed: 'New (confirmed)',
  Preparing: 'Preparing',
  OutForDelivery: 'Out for delivery',
  Completed: 'Completed',
  Cancelled: 'Cancelled',
};

export const ORDER_TYPE_LABEL: Record<OrderType, string> = { Delivery: 'Delivery', Takeaway: 'Takeaway', DineIn: 'Dine-in' };

// ---------- API ----------

export interface Training { text: string | null; maxLength: number; }
export interface TrainingFile { fileName: string; text?: string; imageBase64?: string; mediaType?: string; }

@Injectable({ providedIn: 'root' })
export class Api {
  private http = inject(HttpClient);

  private get<T>(url: string, params?: Record<string, string | number | boolean | undefined>) {
    let p = new HttpParams();
    for (const [k, v] of Object.entries(params ?? {})) if (v !== undefined && v !== '') p = p.set(k, String(v));
    return firstValueFrom(this.http.get<T>(API_BASE + url, { params: p }));
  }
  private post<T>(url: string, body: unknown = {}) { return firstValueFrom(this.http.post<T>(API_BASE + url, body)); }
  private put<T>(url: string, body: unknown) { return firstValueFrom(this.http.put<T>(API_BASE + url, body)); }
  private del(url: string) { return firstValueFrom(this.http.delete<void>(API_BASE + url)); }

  login(username: string, password: string) { return this.post<{ accessToken: string }>('/api/auth/login', { username, password }); }
  me() { return this.get<Me>('/api/me'); }
  changePassword(currentPassword: string, newPassword: string) { return this.post<void>('/api/me/password', { currentPassword, newPassword }); }

  clients() { return this.get<ClientsResponse>('/api/clients'); }
  createClient(c: NewClient) { return this.post<{ id: number; restaurantId: number | null }>('/api/clients', c); }
  updateClient(id: number, name: string, isActive: boolean) { return this.put<void>(`/api/clients/${id}`, { name, isActive }); }
  deleteClient(id: number) { return this.del(`/api/clients/${id}`); }
  moveRestaurant(rid: number, organizationId: number) { return this.put<void>(`/api/restaurants/${rid}/client`, { organizationId }); }

  createUser(username: string, password: string, organizationId: number) { return this.post<{ id: number }>('/api/users', { username, password, organizationId }); }
  resetUserPassword(id: number, password: string) { return this.put<void>(`/api/users/${id}/password`, { password }); }
  deleteUser(id: number) { return this.del(`/api/users/${id}`); }

  status() { return this.get<Status>('/api/status'); }
  settings() { return this.get<SettingsResponse>('/api/settings'); }
  saveSettings(values: Record<string, string | null>) { return this.put<void>('/api/settings', { values }); }
  testWhatsApp() { return this.post<TestResult>('/api/settings/test/whatsapp'); }
  testAi() { return this.post<TestResult>('/api/settings/test/ai'); }

  restaurants() { return this.get<RestaurantSummary[]>('/api/restaurants'); }
  createRestaurant(name: string, whatsAppPhoneNumberId: string, organizationId?: number) {
    return this.post<{ id: number; organizationId: number }>('/api/restaurants', { name, whatsAppPhoneNumberId, organizationId });
  }
  restaurant(rid: number) { return this.get<RestaurantProfile>(`/api/restaurants/${rid}`); }
  saveRestaurant(rid: number, p: RestaurantProfile) { return this.put<RestaurantProfile>(`/api/restaurants/${rid}`, p); }
  training(rid: number) { return this.get<Training>(`/api/restaurants/${rid}/training`); }
  saveTraining(rid: number, text: string) { return this.put<Training>(`/api/restaurants/${rid}/training`, { text }); }
  importTraining(rid: number, file: TrainingFile) { return this.post<{ text: string }>(`/api/restaurants/${rid}/training/import`, file); }

  /** Menu, deals, FAQs, zones: the same CRUD endpoints for each. */
  list<T>(rid: number, kind: 'menu' | 'deals' | 'faqs' | 'zones') { return this.get<T[]>(`/api/restaurants/${rid}/${kind}`); }
  create<T>(rid: number, kind: 'menu' | 'deals' | 'faqs' | 'zones', body: unknown) { return this.post<T>(`/api/restaurants/${rid}/${kind}`, body); }
  update<T>(rid: number, kind: 'menu' | 'deals' | 'faqs' | 'zones', id: number, body: unknown) { return this.put<T>(`/api/restaurants/${rid}/${kind}/${id}`, body); }
  remove(rid: number, kind: 'menu' | 'deals' | 'faqs' | 'zones', id: number) { return this.del(`/api/restaurants/${rid}/${kind}/${id}`); }

  overview(rid: number) { return this.get<Overview>(`/api/restaurants/${rid}/overview`); }
  orders(rid: number, status?: string, days?: number) { return this.get<Order[]>(`/api/restaurants/${rid}/orders`, { status, days }); }
  setOrderStatus(rid: number, id: number, status: OrderStatus, notifyCustomer: boolean) {
    return this.put<{ order: Order; notifyError: string | null }>(`/api/restaurants/${rid}/orders/${id}/status`, { status, notifyCustomer });
  }
  bookings(rid: number, upcoming: boolean) { return this.get<Booking[]>(`/api/restaurants/${rid}/bookings`, { upcoming }); }
  setBookingStatus(rid: number, id: number, status: BookingStatus, notifyCustomer: boolean) {
    return this.put<{ booking: Booking; notifyError: string | null }>(`/api/restaurants/${rid}/bookings/${id}/status`, { status, notifyCustomer });
  }
  customers(rid: number, search?: string) { return this.get<Customer[]>(`/api/restaurants/${rid}/customers`, { search }); }
  pauseBot(rid: number, cid: number, hours?: number) { return this.post<{ botPausedUntil: string }>(`/api/restaurants/${rid}/customers/${cid}/pause`, { hours }); }
  resumeBot(rid: number, cid: number) { return this.post<void>(`/api/restaurants/${rid}/customers/${cid}/resume`); }
  conversations(rid: number) { return this.get<Conversation[]>(`/api/restaurants/${rid}/conversations`); }
  messages(rid: number, cid: number) { return this.get<ChatMessage[]>(`/api/restaurants/${rid}/customers/${cid}/messages`); }
  sendStaffMessage(rid: number, cid: number, text: string, pauseBot: boolean) {
    return this.post<{ botPausedUntil: string | null }>(`/api/restaurants/${rid}/customers/${cid}/messages`, { text, pauseBot });
  }
  simulate(rid: number, phone: string, name: string, text: string) {
    return this.post<SimulatorResult>(`/api/restaurants/${rid}/simulator`, { phone, name, text });
  }
}

// ---------- Currently selected restaurant ----------

const RKEY = 'chatserve.restaurant';

@Injectable({ providedIn: 'root' })
export class CurrentRestaurant {
  private api = inject(Api);
  readonly list = signal<RestaurantSummary[]>([]);
  readonly id = signal<number | null>(null);

  async load() {
    const list = await this.api.restaurants();
    this.list.set(list);
    let saved: number | null = null;
    try { saved = Number(localStorage.getItem(RKEY)) || null; } catch { /* ignore */ }
    const current = this.id() ?? saved;
    this.id.set(list.find(r => r.id === current)?.id ?? list[0]?.id ?? null);
  }

  select(id: number) {
    this.id.set(id);
    try { localStorage.setItem(RKEY, String(id)); } catch { /* ignore */ }
  }

  get current() { return this.list().find(r => r.id === this.id()) ?? null; }

  /** Restaurants grouped by client, for the switcher. */
  groups() {
    const map = new Map<string, RestaurantSummary[]>();
    for (const r of this.list()) map.set(r.organizationName, [...(map.get(r.organizationName) ?? []), r]);
    return [...map.entries()].map(([name, items]) => ({ name, items }));
  }
}

/** A random, easy-to-type password (no look-alike characters). */
export function randomPassword() {
  const chars = 'abcdefghjkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789';
  const bytes = crypto.getRandomValues(new Uint8Array(12));
  return Array.from(bytes, b => chars[b % chars.length]).join('');
}

// ---------- Helpers ----------

export function money(v: number | null | undefined, currency = 'Rs') {
  return `${currency} ${Math.round(v ?? 0).toLocaleString('en-PK')}`;
}

export function when(iso: string | null | undefined) {
  if (!iso) return '';
  const d = new Date(iso);
  const today = new Date();
  const time = d.toLocaleTimeString('en-PK', { hour: 'numeric', minute: '2-digit' });
  if (d.toDateString() === today.toDateString()) return time;
  return d.toLocaleDateString('en-PK', { day: 'numeric', month: 'short' }) + ', ' + time;
}

export function downloadCsv(filename: string, rows: (string | number | null | undefined)[][]) {
  const csv = rows.map(r => r.map(c => {
    const s = c === null || c === undefined ? '' : String(c);
    return /[",\n]/.test(s) ? `"${s.replace(/"/g, '""')}"` : s;
  }).join(',')).join('\r\n');
  // BOM so Excel shows non-Latin text and emoji correctly
  const blob = new Blob(['﻿' + csv], { type: 'text/csv;charset=utf-8' });
  const a = document.createElement('a');
  a.href = URL.createObjectURL(blob);
  a.download = filename;
  a.click();
  URL.revokeObjectURL(a.href);
}
