import { Injectable, signal } from '@angular/core';

export interface Toast { id: number; text: string; kind: 'ok' | 'error'; }

@Injectable({ providedIn: 'root' })
export class Toasts {
  readonly items = signal<Toast[]>([]);
  private next = 1;

  ok(text: string) { this.push(text, 'ok'); }
  error(text: string) { this.push(text, 'error'); }

  dismiss(id: number) { this.items.update(list => list.filter(t => t.id !== id)); }

  private push(text: string, kind: Toast['kind']) {
    const id = this.next++;
    this.items.update(list => [...list, { id, text, kind }]);
    setTimeout(() => this.dismiss(id), kind === 'error' ? 7000 : 3000);
  }
}

/** Server ke ProblemDetails / error se padhne layak message. */
export function errorText(err: unknown): string {
  const e = err as { error?: { detail?: string; title?: string } | string; status?: number; message?: string };
  if (e?.error && typeof e.error === 'object') return e.error.detail || e.error.title || 'Kuch ghalat ho gaya';
  if (typeof e?.error === 'string' && e.error) return e.error;
  if (e?.status === 0) return 'Server se rabta nahi ho saka. Kya API chal rahi hai?';
  return e?.message || 'Kuch ghalat ho gaya';
}
