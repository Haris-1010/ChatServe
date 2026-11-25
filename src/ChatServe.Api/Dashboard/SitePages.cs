using System.Net;
using System.Text;
using Microsoft.Extensions.Options;

namespace ChatServe.Api.Dashboard;

/// <summary>Business details shown on the public website (Settings → Website). They must match the NTN certificate for Meta.</summary>
public sealed class SiteOptions
{
    public const string SectionName = "Site";
    public string BusinessName { get; set; } = "ChatServe";
    public string ContactEmail { get; set; } = string.Empty;
    public string ContactPhone { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
}

/// <summary>
/// A small public website at /site (home), /site/privacy and /site/terms: what Meta business verification and
/// the app's privacy-policy URL need. Plain server-rendered HTML, no login.
/// </summary>
public static class SitePages
{
    public static void MapSitePages(this WebApplication app)
    {
        app.MapGet("/site", (IOptionsMonitor<SiteOptions> o) => Page(o.CurrentValue, "", Home(o.CurrentValue)));
        app.MapGet("/site/privacy", (IOptionsMonitor<SiteOptions> o) => Page(o.CurrentValue, "Privacy Policy", Privacy(o.CurrentValue)));
        app.MapGet("/site/terms", (IOptionsMonitor<SiteOptions> o) => Page(o.CurrentValue, "Terms of Service", Terms(o.CurrentValue)));
    }

    private static string E(string s) => WebUtility.HtmlEncode(s);

    private static IResult Page(SiteOptions s, string title, string body)
    {
        var name = E(s.BusinessName);
        var html = $$"""
            <!doctype html>
            <html lang="en">
            <head>
              <meta charset="utf-8" />
              <meta name="viewport" content="width=device-width, initial-scale=1" />
              <title>{{(title.Length == 0 ? name + ": WhatsApp ordering for restaurants" : E(title) + " | " + name)}}</title>
              <meta name="description" content="{{name}} answers restaurant customers on WhatsApp 24/7, takes orders and table bookings, and sends them to the kitchen." />
              <style>
                :root { --bg:#f7f8f7; --card:#fff; --text:#16201b; --muted:#5b6762; --line:#e2e7e4; --brand:#12a06b; }
                @media (prefers-color-scheme: dark) { :root { --bg:#0f1412; --card:#161d1a; --text:#e7efeb; --muted:#9aa8a1; --line:#25302b; --brand:#2cc28a; } }
                * { box-sizing:border-box; }
                body { margin:0; background:var(--bg); color:var(--text); font:16px/1.6 system-ui,-apple-system,"Segoe UI",Roboto,sans-serif; }
                a { color:var(--brand); }
                header, main, footer { max-width:880px; margin:0 auto; padding:0 16px; }
                header { display:flex; justify-content:space-between; align-items:center; padding-top:18px; gap:12px; flex-wrap:wrap; }
                .logo { font-weight:800; font-size:20px; color:var(--text); text-decoration:none; }
                nav a { margin-left:14px; text-decoration:none; color:var(--muted); font-size:15px; }
                nav a:first-child { margin-left:0; }
                h1 { font-size:clamp(28px,5vw,40px); line-height:1.2; margin:40px 0 12px; }
                h2 { font-size:22px; margin:32px 0 8px; }
                .lead { font-size:18px; color:var(--muted); max-width:640px; }
                .grid { display:grid; grid-template-columns:repeat(auto-fit,minmax(190px,1fr)); gap:14px; margin-top:22px; }
                .card { background:var(--card); border:1px solid var(--line); border-radius:14px; padding:16px 18px; }
                .card b { display:block; margin-bottom:4px; }
                .muted { color:var(--muted); }
                .contact p { margin:4px 0; }
                footer { color:var(--muted); font-size:14px; padding-top:30px; padding-bottom:30px; border-top:1px solid var(--line); margin-top:48px; }
                footer a { color:var(--muted); margin-right:14px; }
              </style>
            </head>
            <body>
              <header>
                <a class="logo" href="/site">💬 {{name}}</a>
                <nav><a href="/site">Home</a><a href="/site/privacy">Privacy</a><a href="/site/terms">Terms</a><a href="/">Login</a></nav>
              </header>
              <main>{{body}}</main>
              <footer>
                © {{DateTime.UtcNow.Year}} {{name}}{{(s.Address.Length > 0 ? " · " + E(s.Address) : "")}}<br />
                <a href="/site/privacy">Privacy Policy</a><a href="/site/terms">Terms of Service</a>
              </footer>
            </body>
            </html>
            """;
        return Results.Content(html, "text/html; charset=utf-8");
    }

    private static string Contact(SiteOptions s)
    {
        var sb = new StringBuilder("<div class=\"card contact\">");
        sb.Append($"<p><b>{E(s.BusinessName)}</b></p>");
        if (s.Address.Length > 0) sb.Append($"<p>📍 {E(s.Address)}</p>");
        if (s.ContactEmail.Length > 0) sb.Append($"<p>✉️ <a href=\"mailto:{E(s.ContactEmail)}\">{E(s.ContactEmail)}</a></p>");
        if (s.ContactPhone.Length > 0) sb.Append($"<p>📞 {E(s.ContactPhone)}</p>");
        sb.Append("</div>");
        return sb.ToString();
    }

    private static string Home(SiteOptions s) => $"""
        <h1>Your restaurant's WhatsApp, answered 24/7.</h1>
        <p class="lead">{E(s.BusinessName)} is a WhatsApp assistant for restaurants in Pakistan. It answers customers in Roman Urdu, Urdu or English,
        takes orders and table bookings, and sends every confirmed order straight to the kitchen.</p>

        <div class="grid">
          <div class="card"><b>🧾 Orders on WhatsApp</b><span class="muted">Menu, prices, deals and delivery charges from your own menu. Orders reach the kitchen only after the customer says yes.</span></div>
          <div class="card"><b>👨‍🍳 Staff stay in control</b><span class="muted">Kitchen and riders get order tickets on WhatsApp and update the customer with one word.</span></div>
          <div class="card"><b>📅 Table bookings</b><span class="muted">Customers request a table; your team confirms it.</span></div>
          <div class="card"><b>📊 Dashboard</b><span class="muted">Live orders, chats, customers and menu in one place. Take over any chat at any time.</span></div>
        </div>

        <h2>How it works</h2>
        <p>The restaurant gets its own WhatsApp Business number on the official WhatsApp Business Platform (Meta).
        Customers message that number; {E(s.BusinessName)} replies using the restaurant's menu and information, and the restaurant's staff
        can read and answer every chat from the dashboard.</p>

        <h2>Contact</h2>
        <p>For restaurants interested in {E(s.BusinessName)}, or questions about your data:</p>
        {Contact(s)}
        """;

    private static string Privacy(SiteOptions s)
    {
        var n = E(s.BusinessName);
        var email = s.ContactEmail.Length > 0 ? $"<a href=\"mailto:{E(s.ContactEmail)}\">{E(s.ContactEmail)}</a>" : "the contact details below";
        return $"""
            <h1>Privacy Policy</h1>
            <p class="muted">Last updated: 25 November 2026</p>
            <p>{n} is a WhatsApp assistant that restaurants use to answer customer questions, take orders and table bookings, and pass conversations to their staff. This policy explains what information {n} handles when you message a restaurant that uses it.</p>

            <h2>Information we collect</h2>
            <p>When you message a restaurant's WhatsApp number that runs {n}, we receive and store:</p>
            <ul>
              <li>your WhatsApp phone number and WhatsApp profile name;</li>
              <li>the messages you send (including voice notes, which are turned into text) and the replies sent to you;</li>
              <li>order and booking details you provide, such as your name, delivery address, area, items, payment method and booking time;</li>
              <li>basic technical data about each message (message ID, time, delivery status).</li>
            </ul>
            <p>We do not collect your contacts, location (unless you type an address), photos or other data from your phone.</p>

            <h2>How we use it</h2>
            <ul>
              <li>to reply to your questions about the restaurant, its menu, prices, delivery and timings;</li>
              <li>to create, confirm and track your orders and table bookings;</li>
              <li>to send your order or booking to the restaurant's staff;</li>
              <li>to let restaurant staff read and reply to your chat;</li>
              <li>to keep a short chat history so replies stay relevant.</li>
            </ul>
            <p>We do not sell your information or use it for advertising.</p>

            <h2>Who we share it with</h2>
            <ul>
              <li><b>The restaurant you message</b>, so it can prepare your order or booking and reply to you.</li>
              <li><b>Meta (WhatsApp Business Platform)</b>, which delivers the messages.</li>
              <li><b>Anthropic</b>, an AI provider that writes replies. It receives the restaurant's menu and the recent messages of your chat.</li>
              <li><b>OpenAI</b>, only for voice notes: the audio is sent to turn it into text.</li>
              <li><b>Hosting and database providers</b> that store the data on our behalf.</li>
            </ul>
            <p>We may also share information if the law requires it.</p>

            <h2>How long we keep it</h2>
            <p>Chats, orders and bookings are kept while the restaurant uses {n}, so it can see past orders and support you. You can ask for your data to be deleted at any time.</p>

            <h2>Your choices and data deletion</h2>
            <p>You can stop messaging at any time. To ask for a copy or deletion of your data, contact us at {email} with the phone number you used. We will delete your chats, orders and bookings within 30 days, unless we must keep something by law.</p>

            <h2>Security</h2>
            <p>Data is sent over encrypted connections (HTTPS) and access to the restaurant dashboard is password protected.</p>

            <h2>Children</h2>
            <p>{n} is not meant for children under 13, and we do not knowingly collect their data.</p>

            <h2>Changes</h2>
            <p>We may update this policy. The date at the top shows the latest version.</p>

            <h2>Contact</h2>
            {Contact(s)}
            """;
    }

    private static string Terms(SiteOptions s)
    {
        var n = E(s.BusinessName);
        return $"""
            <h1>Terms of Service</h1>
            <p class="muted">Last updated: 25 November 2026</p>
            <p>These terms apply to restaurants that use {n} and to customers who message a restaurant's WhatsApp number that runs {n}.</p>

            <h2>For customers</h2>
            <ul>
              <li>Replies are written automatically from the restaurant's menu and information. Prices, availability and delivery times are set by the restaurant.</li>
              <li>An order is placed only after you agree to the order summary. The restaurant is responsible for preparing and delivering it and for payments.</li>
              <li>You can ask for a person at any time; the restaurant's staff will reply.</li>
              <li>Please don't send abusive, illegal or fake orders.</li>
            </ul>

            <h2>For restaurants</h2>
            <ul>
              <li>The restaurant is responsible for its menu, prices, offers, food, delivery and customer payments, and for keeping its information up to date.</li>
              <li>The restaurant's WhatsApp use must follow Meta's WhatsApp Business policies.</li>
              <li>Fees, the monthly AI reply allowance and support are agreed separately with {n}.</li>
              <li>{n} aims to run 24/7 but can't guarantee there will never be interruptions (for example from WhatsApp, the AI provider or hosting).</li>
            </ul>

            <h2>Privacy</h2>
            <p>How we handle data is explained in the <a href="/site/privacy">Privacy Policy</a>.</p>

            <h2>Contact</h2>
            {Contact(s)}
            """;
    }
}
