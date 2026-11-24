import { DestroyRef, effect, inject, untracked } from '@angular/core';
import { CurrentRestaurant } from './api';

/**
 * Re-runs load() when the restaurant changes, and (optionally) refreshes every N seconds.
 * Call it from the page constructor (injection context).
 */
export function onRestaurant(load: (rid: number) => unknown, refreshSeconds?: number) {
  const current = inject(CurrentRestaurant);
  effect(() => {
    const id = current.id();
    if (id) untracked(() => load(id));
  });
  if (refreshSeconds) {
    const timer = setInterval(() => {
      const id = current.id();
      if (id && document.visibilityState === 'visible') load(id);
    }, refreshSeconds * 1000);
    inject(DestroyRef).onDestroy(() => clearInterval(timer));
  }
  return current;
}
