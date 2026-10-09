# Shutki Vorta Catering · শুঁটকি ভর্তা ক্যাটারিং

An online ordering platform for a Bangladeshi catering kitchen in **Dallas, TX**. It specializes in
**shutki vorta** (dried-fish vorta) and classic homestyle vortas, all sold **by the pound** for
**pickup or local delivery**.

Built with **.NET 10, C#, Razor Pages and ASP.NET Core Identity**, using **Clean Architecture**,
**CQRS with MediatR** and **Dapper (no Entity Framework)**. It runs on **SQLite or Microsoft SQL Server**.

---

## Features

**Public website** (traditional Bangladeshi design: bottle green, sindoor red, turmeric gold, nakshi-kantha
stitching, jamdani patterns, alpona motifs, Bengali typography)
- Home page with signature shutki vortas, homestyle vortas, "how it works", story and catering sections
- Full menu, plus an **SEO page for every menu item** at `/menu/{slug}`
- Order by the pound: ½ lb minimum and ½ lb steps (configurable per item), with live price totals
- Cart → checkout with **Pickup** or **Delivery**, date and time-slot picker (lead time, closed days and
  slots are configurable), Dallas sales tax, delivery fee, free-delivery threshold, delivery-ZIP check
- Thank-you page, private order-status page (emailed link, no account needed), guest "track my order"
- Catering inquiry and contact forms (with honeypot spam protection), FAQ, About, Privacy
- **Our Kitchen** (`/kitchen`): our vortas are made by Bangladeshi mothers and homemakers in an inspected
  Dallas kitchen, with a "Join our kitchen" form for homemakers who want to cook with us. The same story
  appears on **Our Story** (`/about`) and the home page.

**Restaurants: wholesale standing orders** (`/restaurants`)
- Landing page with the wholesale price list (per-item wholesale price, or retail minus a default discount),
  how it works, FAQ and SEO metadata
- Restaurants (signed in) request a **standing order**: vortas and pounds per delivery (wholesale minimum per
  item and per delivery), **days of the week**, delivery time, first and optional last delivery date,
  delivery or pickup, tax-permit number. A live estimate shows the cost per delivery and per week.
- After an admin approves, **orders are generated automatically** a few days ahead (`Wholesale:GenerateDaysAhead`)
  as normal kitchen orders marked "Restaurant". Generation is idempotent (one ledger row per date), skips
  closed days and blackout dates, and runs at startup and every hour.
- Restaurants manage everything from **My account → Restaurant orders**: upcoming deliveries, skip or restore a
  date, pause, resume or cancel (up to `Wholesale:ChangeCutoffHours` before a delivery)

**Customer accounts**
- Register, sign in, confirm email, forgot/reset password, change password, profile and saved address
- "My orders" with a status timeline; customers can cancel while an order is still pending
- Guest orders placed with the same, confirmed email address show up automatically in the account

**Admin panel** (`/admin`, Admin role only)
- Dashboard: pending and in-progress orders, today's and this month's sales, 14-day chart, upcoming
  pickups/deliveries, best sellers
- Orders: search and filter by status, type and date; detail view, status workflow
  (Pending → Confirmed → Preparing → Ready / Out for delivery → Completed, or Cancelled), optional email
  to the customer, internal kitchen notes, printable layout
- Menu items: create, edit, upload photo, hide/show, delete (blocked once an item has been ordered),
  per-item SEO title and description
- **Restaurant orders** (`/admin/recurring`): approve or decline requests; edit items, agreed per-lb prices,
  days, time, delivery fee and **tax exemption** (once a Texas resale certificate is on file); pause, resume,
  cancel; skip or restore dates; generate deliveries now; monthly **statement** per restaurant
- **Production plan** (`/admin/production`): pounds of each vorta per day for online orders, generated
  restaurant orders and standing orders not generated yet
- Catering and "join our kitchen" inquiries, customers (grant/revoke admin)
- Settings overview with email diagnostics, **Test connection** and **Send test email**; **Email log** with retry

**Email notifications** (branded HTML templates with a plain-text alternative)
- To admins: new order, order cancelled by customer, new inquiry, new restaurant request, a restaurant
  pausing/cancelling or skipping a delivery
- To customers: order confirmation, status updates, inquiry received, confirm email, welcome, password reset
- To restaurants: request received, approved/declined/paused/resumed/cancelled
- Emails go through a durable outbox in the database and are sent in the background with retries, so
  checkout is never slowed down by SMTP and nothing is lost when the mail server is down

**SEO**
- Unique titles and meta descriptions, canonical URLs, Open Graph and Twitter cards
- JSON-LD structured data: `FoodEstablishment`, `WebSite`, `Menu`, `Product` (with a per-pound
  `UnitPriceSpecification`), `BreadcrumbList` and `FAQPage`
- **`/robots.txt` and `/sitemap.xml` are generated from the database and refresh automatically** whenever
  a menu item is added, edited, hidden or deleted. Private areas (admin, account, cart, checkout, order
  pages) are disallowed and marked `noindex`.
- Lowercase clean URLs, permanent redirects to canonical slugs, lazy-loaded WebP images, compression and caching

---

## Quick start

Prerequisite: the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet run --project src/ShutkiVorta.Web
```

On first start the app creates the SQLite database (`src/ShutkiVorta.Web/App_Data/shutkivorta.db`),
applies the schema, and seeds the roles, an administrator and the 9 starter menu items.

| What | Value |
|---|---|
| Website | the URL printed in the console (e.g. `http://localhost:5072`) |
| Admin panel | `/admin` |
| Admin login | `admin@example.com` / `ChangeMe!2026` (set in `Seed`, **change before going live**) |
| Development emails | written to `src/ShutkiVorta.Web/App_Data/mail/` as `.eml` and `.html` files |

---

## Configuration (`src/ShutkiVorta.Web/appsettings.json`)

Any setting can also be supplied as an environment variable (e.g. `Email__Smtp__Password`) or with
`dotnet user-secrets`. That is recommended for passwords.

### Database: SQLite or SQL Server

```json
"Database": { "Provider": "Sqlite", "AutoMigrate": true, "AutoCreateDatabase": true },
"ConnectionStrings": {
  "Sqlite": "Data Source=App_Data/shutkivorta.db",
  "SqlServer": "Server=localhost;Database=ShutkiVorta;User Id=sa;Password=...;TrustServerCertificate=True;Encrypt=True"
}
```

To switch to SQL Server, set `"Provider": "SqlServer"` and fill in `ConnectionStrings:SqlServer`. The
database is created if it doesn't exist and migrations run at startup. Migrations are plain SQL scripts in
`src/ShutkiVorta.Infrastructure/Persistence/Migrations/{Sqlite|SqlServer}/`; add a new numbered script to
change the schema.

### Email

Fill in the `Smtp` section with your mail provider's details. With `"DeliveryMethod": "Auto"` (the default), the
site sends through SMTP as soon as `Smtp:Host` is filled in. While it is empty, emails are only saved as files
in `App_Data/mail/` (handy for development).

```json
"Email": {
  "Enabled": true,
  "DeliveryMethod": "Auto",                 // Auto | Smtp | PickupDirectory
  "FromName": "Shutki Vorta Catering",
  "FromAddress": "orders@yourdomain.com",   // must be an address your SMTP account may send from
  "ReplyToAddress": "hello@yourdomain.com",
  "AdminRecipients": [ "owner@yourdomain.com", "kitchen@yourdomain.com" ],
  "SendCustomerStatusUpdates": true,
  "Smtp": {
    "Host": "smtp.yourprovider.com",
    "Port": 587,                            // 587 = STARTTLS, 465 = SSL/TLS
    "Security": "Auto",                     // Auto | StartTls | SslOnConnect | None
    "UserName": "orders@yourdomain.com",
    "Password": "app-password",
    "TimeoutSeconds": 30,
    "AcceptInvalidCertificates": false,     // only for a self-signed certificate on your own server
    "CheckCertificateRevocation": true,
    "LocalDomain": ""                       // HELO name, if your server insists on one
  }
}
```

The templates live in `src/ShutkiVorta.Infrastructure/Email/Templates/`.

**How sending works.** Emails are written to an outbox table in the database, then a background service
delivers them. Temporary problems (network, timeouts, "try again later" replies) are retried after 1 min,
5 min, 30 min and 2 h. Permanent problems (wrong password, rejected sender, certificate errors) fail
straight away with an explanation. Nothing is lost if the mail server or the site is down; queued emails
go out once it is back.

**Checking your setup (Admin → Settings → Email):**
- The panel shows whether real emails are being sent, plus warnings for common mistakes: SMTP host
  empty, placeholder `example.com` addresses, no password, a port that doesn't match its `Security` setting.
- **Test connection** connects and signs in step by step (connect → TLS → sign in) and shows where it fails.
- **Send test email** sends a real message and shows the server's reply, or the exact error with a
  plain-English hint.
- **Admin → Email log** lists every email with its status (Sent, Failed, Waiting to send, Saved to folder).
  Open one to see the error and the content. **Retry** (or **Retry all failed**) re-sends after you fix the settings.

**Common problems**

| Symptom | Fix |
|---|---|
| Emails appear in `App_Data/mail` but never arrive | `Smtp:Host` is empty or `DeliveryMethod` is `PickupDirectory`. Fill in the Smtp section and keep `"Auto"`. |
| "Authentication failed" / 535 | Wrong user name or password. Gmail and Outlook.com need an **app password** (2-step verification on). Microsoft 365 needs *Authenticated SMTP* enabled for the mailbox. |
| "Sender address rejected" / 550 / 553 | `FromAddress` must be the account you sign in with (or a verified alias/domain at your provider). |
| Timeout when connecting | Port 465 needs `SslOnConnect`, 587 needs `StartTls` (`Auto` picks the right one). Some hosts block outbound SMTP ports; ask them or use your provider's alternative port (e.g. 2525). |
| Certificate error | Use the host name on the server's certificate (e.g. `mail.yourdomain.com`, not the IP). Only for your own server with a self-signed certificate, set `AcceptInvalidCertificates: true`. |
| Delivered but lands in spam | Add SPF, DKIM and DMARC records for your domain at your DNS provider (your mail provider documents the values). |
| Works locally, not on the server | Settings stored with `dotnet user-secrets` only load in Development. On the server use environment variables, e.g. `Email__Smtp__Password`, `Email__AdminRecipients__0`. |

### Business, ordering and website

| Section | Highlights |
|---|---|
| `Business` | name, Bengali name, phone, email, address (leave `StreetAddress` empty to share it only after ordering), hours, time zone (`America/Chicago`) |
| `Ordering` | `AcceptingOrders`, `MinimumLeadTimeHours`, `MaxDaysInAdvance`, slot times, `ClosedDays`, `BlackoutDates` (e.g. `"2027-03-20"` for Eid; applies to restaurant deliveries too), `DeliveryFee`, `FreeDeliveryThreshold`, `MinimumDeliverySubtotal`, `TaxRate` (8.25% Dallas), `DeliveryZipPrefixes`, payment instructions |
| `Wholesale` | `AcceptingRequests`, `DiscountPercent` (default wholesale price = retail minus this, unless a menu item has its own wholesale price), `MinimumQuantityPerItem`, `QuantityStep`, `MinimumSubtotalPerDelivery`, `DeliveryFee`, `LeadTimeDays`, `GenerateDaysAhead`, `AutoGenerate`, `ChangeCutoffHours`, delivery time slots, `PaymentTerms` |
| `Site` | `BaseUrl` (set your public domain in production, used for canonical URLs, sitemap and email links; empty = current host), `AllowSearchEngineIndexing` (set `false` on staging), search-console verification codes |
| `Identity` | `RequireConfirmedEmail` (default `false`: customers can sign in before confirming) |
| `Seed` | first administrator account and whether to seed the starter menu |

---

## Architecture

```
src/
  ShutkiVorta.Domain           Entities and business rules, no dependencies
                               MenuItem, Order (+ lines, status history, pricing), CateringInquiry,
                               StandingOrder (restaurant recurring orders, WeekDays schedule)
  ShutkiVorta.Application      Use cases (CQRS): MediatR commands/queries/notifications, FluentValidation,
                               pipeline behaviors (logging, authorization, validation), interfaces, DTOs
  ShutkiVorta.Infrastructure   Dapper repositories, SQL dialects (SQLite/SQL Server), SQL migrations,
                               Identity user/role stores (no EF), MailKit email + templates, seeding
  ShutkiVorta.Web              Razor Pages (public site, account, admin), cart cookie, SEO, Program.cs
tests/
  ShutkiVorta.UnitTests        Domain and application tests (xUnit, NSubstitute)
  ShutkiVorta.IntegrationTests Real app host (WebApplicationFactory) against SQLite and SQL Server
```

- **CQRS:** every page talks to the application layer only through `ISender` (MediatR): commands such as
  `PlaceOrderCommand` and `UpdateOrderStatusCommand`, queries such as `GetMenuQuery` and `GetSitemapQuery`.
  Side effects such as emails and cache refreshes run in `INotificationHandler`s (`OrderPlacedNotification`,
  `MenuChangedNotification`, …).
- **Security in depth:** admin pages use an authorization policy, *and* admin commands implement
  `IRequireAdmin`, which the MediatR `AuthorizationBehavior` enforces.
- **No Entity Framework:** data access is Dapper with a small `ISqlDialect` for the few SQL differences
  (identity retrieval and paging). ASP.NET Core Identity uses custom Dapper stores (`DapperUserStore`,
  `DapperRoleStore`).
- **MediatR 12.5.0** is pinned on purpose: it is the last Apache-2.0 release (13+ needs a commercial license).

---

## Tests

```bash
dotnet test                                        # unit + integration tests (SQLite)

# also run the persistence tests against SQL Server (a temporary database is created and dropped):
SHUTKIVORTA_TEST_SQLSERVER="Server=localhost,1433;User Id=sa;Password=...;TrustServerCertificate=True" dotnet test
```

---

## Going live checklist

1. Change `Seed:AdminPassword` (or sign in and change the password right away), and set real `Business` details.
2. Fill in `Email:Smtp`, `Email:FromAddress` and `Email:AdminRecipients`; in Admin → Settings run
   **Test connection** and **Send test email**, and check that no warnings remain.
3. Set `Site:BaseUrl` to your domain (e.g. `https://www.yourdomain.com`).
4. Run behind HTTPS. Behind a reverse proxy, set `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`.
5. Persist `App_Data/` (SQLite database and data-protection keys) and `wwwroot/uploads/` (menu photos),
   or use SQL Server and point `DataProtection:KeysPath` to persistent storage.
6. Submit `https://yourdomain.com/sitemap.xml` in Google Search Console and Bing Webmaster Tools.

## Photos

The starter menu photos are Creative Commons images from Wikimedia Commons (credited on each item page and
on the About page). Replace them any time from **Admin → Menu items → Edit → Upload a new photo**.
