/**
 * Meta's Embedded Signup popup (Facebook JS SDK). The client logs in with Facebook and picks or adds their WhatsApp
 * number; we get a short-lived code (swapped for a token on the server) and, through a window message, the
 * WhatsApp Business account ID and Phone number ID.
 *
 * coexistence = true: the number stays in the client's WhatsApp Business app (featureType whatsapp_business_app_onboarding).
 * The page must be served over HTTPS from a domain allowed in the Meta app (Facebook Login for Business settings).
 */
export interface SignupOutcome { code: string; wabaId: string | null; phoneNumberId: string | null; }

declare global {
  interface Window { FB?: any; fbAsyncInit?: () => void; }
}

let sdk: Promise<any> | null = null;

function loadSdk(appId: string, version: string): Promise<any> {
  if (!sdk) {
    sdk = new Promise((resolve, reject) => {
      window.fbAsyncInit = () => {
        window.FB.init({ appId, autoLogAppEvents: true, xfbml: false, version });
        resolve(window.FB);
      };
      const s = document.createElement('script');
      s.src = 'https://connect.facebook.net/en_US/sdk.js';
      s.async = true;
      s.defer = true;
      s.crossOrigin = 'anonymous';
      s.onerror = () => { sdk = null; reject(new Error('Could not load the Facebook SDK (blocked by the browser or an ad blocker?)')); };
      document.body.appendChild(s);
    });
  }
  return sdk;
}

function fromFacebook(origin: string): boolean {
  try {
    const host = new URL(origin).hostname;
    return host === 'facebook.com' || host.endsWith('.facebook.com');
  } catch { return false; }
}

export async function runEmbeddedSignup(appId: string, configId: string, version: string, coexistence: boolean): Promise<SignupOutcome> {
  const FB = await loadSdk(appId, version);

  let session: { wabaId: string | null; phoneNumberId: string | null } | null = null;
  let cancelled: string | null = null;
  const onMessage = (e: MessageEvent) => {
    if (!fromFacebook(e.origin) || typeof e.data !== 'string') return;
    try {
      const d = JSON.parse(e.data);
      if (d?.type !== 'WA_EMBEDDED_SIGNUP') return;
      if (typeof d.event === 'string' && d.event.startsWith('FINISH')) {
        session = { wabaId: d.data?.waba_id ?? null, phoneNumberId: d.data?.phone_number_id ?? null };
      } else if (d.event === 'CANCEL') {
        cancelled = d.data?.current_step ? `Cancelled at step "${d.data.current_step}"` : 'Cancelled';
      } else if (d.event === 'ERROR') {
        cancelled = d.data?.error_message ?? 'Meta reported an error';
      }
    } catch { /* not ours */ }
  };
  window.addEventListener('message', onMessage);

  try {
    const code = await new Promise<string>((resolve, reject) => {
      FB.login((response: any) => {
        const c = response?.authResponse?.code;
        if (c) resolve(c); else reject(new Error(cancelled ?? 'The Facebook popup was closed before finishing.'));
      }, {
        config_id: configId,
        response_type: 'code',
        override_default_response_type: true,
        extras: coexistence
          ? { setup: {}, featureType: 'whatsapp_business_app_onboarding', sessionInfoVersion: '3' }
          : { setup: {}, sessionInfoVersion: '3' },
      });
    });

    // The session info message can arrive just after the login callback.
    for (let i = 0; i < 30 && !session; i++) await new Promise(r => setTimeout(r, 100));
    const s = session as { wabaId: string | null; phoneNumberId: string | null } | null;
    return { code, wabaId: s?.wabaId ?? null, phoneNumberId: s?.phoneNumberId ?? null };
  } finally {
    window.removeEventListener('message', onMessage);
  }
}
