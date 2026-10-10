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
  date, pause, resume or cancel (up to `Wholesale:ChangeCutoffHours` before a delivery). A pause or skip made by
  the kitchen can only be lifted by the kitchen, and an order the kitchen cancelled is never re-created.

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
- **Inventory** (`/admin/inventory`): record what the kitchen buys (ingredients and supplies): item, quantity, unit and
  the price paid, several items per receipt, with **receipt photos** (taken on a phone or uploaded, JPG/PNG/WebP up to
  10 MB, 10 per purchase). Item names are **saved and suggested next time**, matched regardless of case and spacing, and
  the item's usual unit is filled in. Spending this month and last month, search by item, store or note and by date,
  and a **price history** per item. Saved names can be renamed (fixing every purchase) or deleted while unused.
  Receipt photos are private: they are kept outside `wwwroot` and shown to admins only.
- Catering and "join our kitchen" inquiries, customers (grant/revoke admin)
- **Settings** (`/admin/settings`): every business setting editable in the browser, live without a restart,
  with validation, change history and email diagnostics (**Test connection**, **Send test email**); **Email log** with retry

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
applies the schema, and seeds the roles, an administrator, the 9 starter menu items and a starter inventory (23 common
ingredient and supply names plus three sample purchases, marked "Sample entry" in their notes, to delete once you
record real ones).

| What | Value |
|---|---|
| Website | the URL printed in the console (e.g. `http://localhost:5072`) |
| Admin panel | `/admin` |
| Admin login | `admin@example.com` / `ChangeMe!2026` (set in `Seed`, **change before going live**) |
| Development emails | written to `src/ShutkiVorta.Web/App_Data/mail/` as `.eml` and `.html` files |

---

## Settings

There are two kinds of settings:

| Where | What | Who changes it |
|---|---|---|
| **Admin → Settings** (stored in the database) | Business details, online orders, restaurant orders, email (incl. the SMTP server and password), website & search engines, customer accounts, spam protection | Admins, in the browser. **Changes apply immediately**, with no restart, and survive updates. |
| `src/ShutkiVorta.Web/appsettings.json` (or environment variables) | Database connection, data-protection keys, logging, allowed hosts, HTTPS redirection, the email pickup folder, the receipt photo folder, the first admin account (`Seed`) | Whoever hosts the site. Read at startup. |

### Admin → Settings

Each page validates what you enter (with the error shown next to the field) and records every change in
**Recent changes** (when, what, from, to, by whom). Notes on specific settings:

- **Email → Password** is stored encrypted with ASP.NET Data Protection and is never shown again. Leave the box
  blank to keep it. If you change the server, port, user name or encryption, you must type the password again,
  so a saved password can't be sent to a different server.
- **Online orders → Sales tax** is entered as a percentage (`8.25` for Dallas). **Delivery ZIP codes** take one
  code or prefix per line (`752` = all of 752xx); leave the list empty to deliver anywhere.
- **Website → Website address** should be your public `https://` domain in production (used for canonical URLs,
  the sitemap and links in emails). Leave it empty to use the address the site is reached on.
- **Customer accounts → Require email confirmation before sign-in** applies at the next sign-in. Administrators are never
  blocked by it.
- If two admins edit the same page at once, the second save is refused with a request to reload, so nobody
  overwrites someone else's change without seeing it.
- If several servers share one database, a change saved on one reaches the others within 30 seconds.

### appsettings.json

```json
"Database": { "Provider": "Sqlite", "AutoMigrate": true, "AutoCreateDatabase": true },
"ConnectionStrings": {
  "Sqlite": "Data Source=App_Data/shutkivorta.db",
  "SqlServer": "Server=localhost;Database=ShutkiVorta;User Id=sa;Password=...;TrustServerCertificate=True;Encrypt=True"
},
"DataProtection": { "KeysPath": "" },     // empty = App_Data/keys
"Inventory": { "ReceiptsPath": "" },      // empty = App_Data/receipts
"Seed": { "SeedMenu": true, "SeedInventory": true, "AdminEmail": "admin@example.com", "AdminPassword": "ChangeMe!2026", "AdminName": "Site Administrator" },
"Settings": { "ReimportFromConfiguration": false }
```

Any of these can also be set as an environment variable (e.g. `ConnectionStrings__SqlServer`). To switch to SQL
Server, set `"Provider": "SqlServer"` and fill in `ConnectionStrings:SqlServer`. The database is created if it
doesn't exist and migrations run at startup. Migrations are plain SQL scripts in
`src/ShutkiVorta.Infrastructure/Persistence/Migrations/{Sqlite|SqlServer}/`; add a new numbered script to change
the schema. `Email:PickupDirectory` (default `App_Data/mail`) is the folder for emails that are saved instead of sent.
`Inventory:ReceiptsPath` (default `App_Data/receipts`) is the private folder for receipt photos. `Seed:SeedInventory`
adds the starter inventory once, when the inventory tables are created (so deleted sample entries never come back);
set it to `false` before upgrading a live site if you don't want the samples.

**Keep the data-protection keys.** They encrypt sign-in cookies and the saved SMTP password. Store `KeysPath` on
persistent storage and back it up together with the database. If the keys are lost, everyone is signed out
and the SMTP password has to be entered again. Until then, emails wait in the outbox and are retried; they are
not lost.

### First start and upgrading

On the first start of a new database, the settings above are filled in from the built-in defaults, plus any
values still present in `appsettings.json`, `appsettings.{Environment}.json`, user secrets or environment
variables in the old layout (`Email:Smtp:Host`, `Email__Smtp__Password`, `Ordering:TaxRate`, …). After that,
the database is the only source. Values left in those files are ignored, and Admin → Settings lists any that
differ from what is saved.

**Upgrading a live site from a version that kept these settings in `appsettings.json`:** start the new
version once with your old `appsettings.json` (or the matching environment variables) still in place, so
your values are copied in. If the new `appsettings.json` replaced the old one first, each page that got only
defaults is flagged with **Please check these settings** in Admin → Settings (on sites that already have
orders). Enter the correct values and save each page.

**Recovery switch:** start with `Settings:ReimportFromConfiguration=true` (e.g. the environment variable
`Settings__ReimportFromConfiguration=true`) to copy the configured values over the saved ones. This is useful
when a wrong setting locks you out of the admin pages. The values are applied once each time the switch is
turned on. Later starts with the switch still on keep changes made in Admin → Settings, unless the configured
values change; a changed password alone does not count. Turn it off again afterwards. To apply the same values
again, start once with it off.

### Email

In **Admin → Settings → Email**, fill in the mail server (SMTP server, port, user name, password), the sender
address and who should be notified about new orders. With the delivery method **Automatic** (the default), the
site sends through SMTP as soon as an SMTP server is filled in. While it is empty, emails are only saved as files
in `App_Data/mail/` (handy for development). The templates live in `src/ShutkiVorta.Infrastructure/Email/Templates/`.

**Automatic** encryption never sends your password unencrypted over the network. If the server doesn't offer
STARTTLS, sending fails with an explanation. Only a mail server on the same machine (`localhost`) may skip
encryption.

**How sending works.** Emails are written to an outbox table in the database, then a background service
delivers them. Temporary problems (network, timeouts, "try again later" replies) are retried after 1 min,
5 min, 30 min and 2 h. Permanent problems (wrong password, rejected sender, certificate errors) fail
straight away with an explanation. Nothing is lost if the mail server or the site is down; queued emails
go out once it is back.

**Checking your setup (Admin → Settings → Email):**
- The panel shows whether real emails are being sent, plus warnings for common mistakes: SMTP server
  empty, placeholder `example.com` addresses, no password, a port that doesn't match its encryption setting.
- **Test connection** connects and signs in step by step (connect → TLS → sign in) and shows where it fails.
- **Send test email** sends a real message and shows the server's reply, or the exact error with a
  plain-English hint.
- **Admin → Email log** lists every email with its status (Sent, Failed, Waiting to send, Saved to folder).
  Open one to see the error and the content. **Retry** (or **Retry all failed**) re-sends after you fix the settings.
  Password-reset and email-confirmation emails contain private links, so their content is removed from the log once
  delivered and they are never re-sent from it (the customer simply requests a new link).

**Common problems**

| Symptom | Fix |
|---|---|
| Emails appear in `App_Data/mail` but never arrive | The SMTP server is empty, or the delivery method (Advanced) is "Never send; save to a folder". Fill in the mail server and keep "Automatic". |
| "Authentication failed" / 535 | Wrong user name or password. Gmail and Outlook.com need an **app password** (2-step verification on). Microsoft 365 needs *Authenticated SMTP* enabled for the mailbox. |
| "Sender address rejected" / 550 / 553 | The sender address must be the account you sign in with (or a verified alias/domain at your provider). |
| Timeout when connecting | Port 465 needs SSL/TLS, 587 needs STARTTLS ("Automatic" picks the right one). Some hosts block outbound SMTP ports; ask them or use your provider's alternative port (e.g. 2525). |
| Certificate error | Use the host name on the server's certificate (e.g. `mail.yourdomain.com`, not the IP). Only for your own server with a self-signed certificate, tick "Accept untrusted certificates" (Advanced). |
| Delivered but lands in spam | Add SPF, DKIM and DMARC records for your domain at your DNS provider (your mail provider documents the values). |
| "The saved SMTP password cannot be decrypted" | The data-protection keys changed (see above). Enter the password again; waiting emails then go out. |

---

## Architecture

```
src/
  ShutkiVorta.Domain           Entities and business rules, no dependencies
                               MenuItem, Order (+ lines, status history, pricing), CateringInquiry,
                               StandingOrder (restaurant recurring orders, WeekDays schedule),
                               InventoryPurchase (+ lines, receipts), InventoryItem (saved item names)
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

# also run the integration tests against SQL Server (temporary databases are created and dropped):
SHUTKIVORTA_TEST_SQLSERVER="Server=localhost,1433;User Id=sa;Password=...;TrustServerCertificate=True" dotnet test
```

---

## Going live checklist

1. Change `Seed:AdminPassword` (or sign in and change the password right away). In Admin → Settings → Business
   details, enter your real name, phone, email and address.
2. In Admin → Settings → Email, fill in the mail server, sender address and who is notified about new orders;
   then run **Test connection** and **Send test email**, and check that no warnings remain.
3. In Admin → Settings → Website, set the website address to your domain (e.g. `https://www.yourdomain.com`).
4. Run behind HTTPS. Behind a reverse proxy, set `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`.
5. Persist and back up `App_Data/` (SQLite database, data-protection keys and receipt photos) and `wwwroot/uploads/`
   (menu photos), or use SQL Server and point `DataProtection:KeysPath` and `Inventory:ReceiptsPath` to persistent
   storage.
6. Submit `https://yourdomain.com/sitemap.xml` in Google Search Console and Bing Webmaster Tools.

## Photos

The starter menu photos are Creative Commons images from Wikimedia Commons (credited on each item page and
on the About page). Replace them any time from **Admin → Menu items → Edit → Upload a new photo**.
