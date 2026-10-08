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
- Catering inquiries, customers (grant/revoke admin), settings overview with a **send test email** button

**Email notifications** (branded HTML templates with a plain-text alternative)
- To admins: new order, order cancelled by customer, new catering inquiry
- To customers: order confirmation, status updates, inquiry received, confirm email, welcome, password reset
- Emails are queued and sent in the background with retries, so checkout is never slowed down by SMTP

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

```json
"Email": {
  "Enabled": true,
  "DeliveryMethod": "Smtp",                 // or "PickupDirectory" for local development
  "FromName": "Shutki Vorta Catering",
  "FromAddress": "orders@yourdomain.com",
  "ReplyToAddress": "hello@yourdomain.com",
  "AdminRecipients": [ "owner@yourdomain.com", "kitchen@yourdomain.com" ],
  "SendCustomerStatusUpdates": true,
  "Smtp": { "Host": "smtp.yourprovider.com", "Port": 587, "Security": "StartTls", "UserName": "...", "Password": "..." }
}
```

`Security` accepts `None`, `Auto`, `SslOnConnect` (port 465) or `StartTls` (port 587). Use
**Admin → Settings → Send test email** to verify the configuration. The templates live in
`src/ShutkiVorta.Infrastructure/Email/Templates/`.

### Business, ordering and website

| Section | Highlights |
|---|---|
| `Business` | name, Bengali name, phone, email, address (leave `StreetAddress` empty to share it only after ordering), hours, time zone (`America/Chicago`) |
| `Ordering` | `AcceptingOrders`, `MinimumLeadTimeHours`, `MaxDaysInAdvance`, slot times, `ClosedDays`, `DeliveryFee`, `FreeDeliveryThreshold`, `MinimumDeliverySubtotal`, `TaxRate` (8.25% Dallas), `DeliveryZipPrefixes`, payment instructions |
| `Site` | `BaseUrl` (set your public domain in production, used for canonical URLs, sitemap and email links; empty = current host), `AllowSearchEngineIndexing` (set `false` on staging), search-console verification codes |
| `Identity` | `RequireConfirmedEmail` (default `false`: customers can sign in before confirming) |
| `Seed` | first administrator account and whether to seed the starter menu |

---

## Architecture

```
src/
  ShutkiVorta.Domain           Entities and business rules, no dependencies
                               MenuItem, Order (+ lines, status history, pricing), CateringInquiry
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
2. Configure SMTP (`Email:DeliveryMethod = "Smtp"`) and `Email:AdminRecipients`; send a test email.
3. Set `Site:BaseUrl` to your domain (e.g. `https://www.yourdomain.com`).
4. Run behind HTTPS. Behind a reverse proxy, set `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`.
5. Persist `App_Data/` (SQLite database and data-protection keys) and `wwwroot/uploads/` (menu photos),
   or use SQL Server and point `DataProtection:KeysPath` to persistent storage.
6. Submit `https://yourdomain.com/sitemap.xml` in Google Search Console and Bing Webmaster Tools.

## Photos

The starter menu photos are Creative Commons images from Wikimedia Commons (credited on each item page and
on the About page). Replace them any time from **Admin → Menu items → Edit → Upload a new photo**.
