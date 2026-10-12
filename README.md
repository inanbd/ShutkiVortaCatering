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
  (read-only status by order number + email; changes need the emailed link or the customer's account)
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

**Admin panel** (`/admin`, for staff: Admins see everything; Managers and Employees see the modules their permissions
allow, and every staff member needs a confirmed email). Staff sign in on the normal sign-in page and start on the
dashboard, or on the time clock if they may not see the dashboard.
- **Time clock** (`/admin/time-clock`) for every staff member, see [Time clock](#time-clock) below
- Dashboard: pending and in-progress orders, today's and this month's sales, 14-day chart, upcoming
  pickups/deliveries, best sellers (staff with the dashboard's "Counts" level see it without any money)
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
  Receipt photos are private: they are kept outside `wwwroot` and shown only to staff who may view the inventory.
- Catering and "join our kitchen" inquiries
- **Customers** (`/admin/customers`): accounts, guests and restaurants with what they ordered, see
  [Customers](#customers) below
- **Users** (`/admin/users`) and **Permissions** (`/admin/permissions`): who works here and what they may do, see
  [Staff roles and permissions](#staff-roles-and-permissions) below
- **Settings** (`/admin/settings`, Admins only): every business setting editable in the browser, live without a restart,
  with validation, change history and email diagnostics (**Test connection**, **Send test email**)
- **Email log** (`/admin/emails`) with retry

**Email notifications** (branded HTML templates with a plain-text alternative)
- To admins: new order, order cancelled by customer, new inquiry, new restaurant request, a restaurant
  pausing/cancelling or skipping a delivery
- To customers: order confirmation, status updates, inquiry received, confirm email, welcome, password reset
- To restaurants: request received, approved/declined/paused/resumed/cancelled
- To new staff: the invitation to set their password, or "You've been added to the staff" for an existing account (both
  only once the website address is set, see [Admin → Settings](#admin--settings))
- Emails go through a durable outbox in the database and are sent in the background with retries, so
  checkout is never slowed down by SMTP and nothing is lost when the mail server is down

**SEO**
- Unique titles and meta descriptions, canonical URLs, Open Graph and Twitter cards
- JSON-LD structured data: `FoodEstablishment`, `WebSite`, `Menu`, `Product` (with a per-pound
  `UnitPriceSpecification`), `BreadcrumbList` and `FAQPage`
- **`/robots.txt` and `/sitemap.xml` are generated from the database and refresh automatically** whenever
  a menu item is added, edited, hidden or deleted. Private areas (admin, the `/clock` link, account, cart, checkout,
  order pages) are disallowed and marked `noindex`.
- Lowercase clean URLs, permanent redirects to canonical slugs, lazy-loaded WebP images, compression and caching

### Staff roles and permissions

Every staff member has one role: **Admin**, **Manager** or **Employee**. Admins always have everything. What Managers and
Employees may do is chosen per module on **Admin → Permissions**; until an Admin changes it they have these defaults:

| Module | Manager | Employee |
|---|---|---|
| Dashboard | With sales | Counts (no money) |
| Orders | View & change | View |
| Restaurant orders | View & change | No access |
| Production plan | View | View |
| Menu items | View & change | View |
| Inventory (incl. receipt photos) | View & change | View |
| Inquiries | View & change | No access |
| Customers | View | No access |
| Time clock | See & fix team | Own shifts only |
| Users | View the staff list | No access |
| Email log | View | No access |

"View & change" adds what changes things: an order's status (which can email the customer), kitchen notes and the
customer's tracking link; approving, pausing and editing restaurant orders; adding, editing, hiding and deleting menu items;
adding, editing and deleting purchases; marking inquiries handled. The Email log's top level is "View & retry". On the time
clock, "See team" shows everyone's shifts, locations and hours with the CSV downloads, and "See & fix team" also adds,
corrects and voids other people's shifts. The Permissions page describes each level.

- **Admins only, never granted to anyone else:** Settings (including test emails), adding and removing staff and editing
  their details, roles, permissions, and unlocking accounts.
- **Every staff member can always** clock in and out, see their own shifts and hours, and fix their own forgotten
  clock-out (flagged for review).
- Roles and permissions are read from the database on every request, so a change applies from the person's next click, on
  every server, and nobody is signed out. The menu shows only the modules a person may open.

**Permissions** (`/admin/permissions`, Admins only)
- One row per module with a choice for Managers and one for Employees (the defaults are marked "(default)"). Each role is
  saved on its own and applies to everyone with that role from their next click, except people with custom permissions.
  **Restore defaults** brings back the defaults above. If another admin saved the same role meanwhile, the save is refused
  with a request to reload.
- The Admin-only areas are shown as locked rows. The page also lists the people with custom permissions (role changes don't
  reach them) and the recent permission changes.
- **Custom permissions for one person:** on a Manager's or Employee's page, choose "Custom" and a level per module, or
  "Follow the role" again. Changing someone's role resets their custom permissions to the new role's.

**Users** (`/admin/users`: the list for anyone with "Users: View the staff list"; every change is for Admins)
- **Staff** tab with a role filter and search by name, email or phone. Each person shows their role, their permissions
  (Everything, Role defaults or Custom) and what stands out: on the clock, invitation pending, email not confirmed, locked out.
- **Add user:** name, email, optional mobile phone and job title, and the role. New people get an email with a link to
  **set their password**; it works once, for 7 days, and setting the password confirms their email and opens the staff pages.
  The admin sees the same link **once** on the person's page, with **Copy** and **Share** (text message) buttons and how the
  email got on (Sent, Waiting to send, Saved to a folder, Failed). After leaving or reloading the page the link is gone,
  and like password-reset emails, the invitation's content is removed from the Email log once delivered.
- **Existing accounts:** an email already on the staff is refused (change their role on their page instead). Someone who
  already has an account on the website with a confirmed email is shown to the admin to confirm; they keep their password
  and get a short "You've been added to the staff" email. An account whose email was never confirmed has its password
  removed and is invited, so whoever registered the address cannot get in with it.
- **Until they can sign in:** **Send a new invitation** (earlier links stop working), and, while no password is set,
  **Change email and invite** to fix a mistyped address.
- **A person's page:** what they can do, their time clock (this week's hours for staff who may see the team's), sign-in
  details and recent changes. Admins can edit the name, phone and job title, change the role, set custom permissions,
  **Unlock sign-in** after too many wrong passwords, and **Remove from staff**. Nobody can change their own role or remove
  themselves, and there must always be at least one Admin who can sign in (with a password and a confirmed email, and
  not locked out).
- **Removing someone** keeps their account and shifts; they lose staff access at once and an unused invitation link stops
  working. If they are on the clock, the shift is closed and flagged for review (a forgotten one from an earlier day as a
  missed clock-out, without hours).
- **Former staff** tab: **Re-add** someone with a role. If they can sign in with their own password they are told by
  email, otherwise they get a new invitation.

### Time clock

`/admin/time-clock`, and the short link **`/clock`** to save on a phone's home screen (it asks people to sign in first).
- **Every staff member** (Admin, Manager or Employee) clocks in and out with one big button, **Clock in · কাজ শুরু** or
  **Clock out · কাজ শেষ**, with an optional note. The top bar of every admin page shows "On the clock since …" or a
  **Clock in** link.
- The **time comes from the server's clock**, not the phone's. The **location comes from the phone or computer**: the
  browser asks for it when the button is tapped (staff can tap **Skip**), and it is saved only at clock-in and clock-out,
  never in between. A "How to turn location on" guide for iPhone and Android is on the page.
- **What is stored** for each clock-in and clock-out: the time, the latitude and longitude with the accuracy the device
  reported, the distance to the kitchen (when the kitchen location is set), why there is no location if there is none
  (blocked, not found, timed out, not supported, not sent, no https connection) and the note. The person's name at the time
  is kept with the shift.
- **Near or far from the kitchen:** a clock-in or clock-out farther from the kitchen than the radius in Settings → Time clock
  (300 m by default) is marked "Away from the kitchen" and flagged for review. Catering at a venue is far away too, so it is
  a note for managers, not a rule. Distances are worked out at the moment of the punch. Each shift's page links to the
  location on Google Maps.
- **Approximate locations:** a location less precise than 1 km (e.g. an iPhone with Precise Location turned off) is marked
  **Approximate** and flagged for review.
- **No location:** the shift is still recorded and flagged for review, unless **Require location** is on: then the person
  is told how to fix it, and a manager can add the shift by hand if a phone can't share it.
- **Forgotten clock-outs:** an open shift from an earlier day, or more than 16 hours old, is not closed by the next tap. The
  person is asked when they left (at most 16 hours after clocking in), or taps **I don't remember**: the shift is then a
  missed clock-out that counts no hours until a manager enters the time. Either way it is flagged for review, and then they
  can clock in again.
- **Night shifts:** a shift that ran past midnight and is not over 16 hours gets an **I'm finishing now** button (a normal
  clock-out now, with the location), or the person can enter an earlier finish time. A manager checks shifts that ran past
  midnight. A second tap on any button within two minutes shows the first tap's result instead of recording twice.
- **My shifts:** this week and last week by day, with each week's hours.
- **Team** (time clock "See team"): who is on the clock now ("Probably forgot to clock out" on old shifts), how many shifts
  **need review**, shifts edited this week, and every shift, filtered by person (former staff included), dates and Needs
  review, Edited, Still open or Voided.
- **Needs review** means a manager should look: no location, an approximate one, away from the kitchen, a shift over
  16 hours, a forgotten clock-out, one that ran past midnight, one closed when the person was removed from the staff, or
  one corrected or added by hand. **Mark as reviewed** clears it.
- **Manager corrections** (time clock "See & fix team"): correct a shift's times (the punched times are kept and shown as
  the original), close an open shift, **add a shift** by hand for someone who could not clock in, or **void** a shift
  recorded by mistake (it is kept, struck through, and doesn't count). Each needs a reason; a shift can't be in the future,
  longer than 24 hours or overlap the person's other shifts. Every change is in the shift's history. Managers cannot change
  their own shifts ("Ask an admin to fix your own shifts"); only Admins can fix their own.
- **Weekly hours:** per person per workweek (from the day set in Settings → Time clock, Monday by default), in decimal hours
  and h:mm, with the number of shifts and of shifts to review. Each shift counts to the whole minute, a shift crossing the
  start of a week counts its part in each week, and over 40 hours a week is highlighted with the overtime. Voided shifts,
  missed clock-outs and open shifts don't count (open ones are shown "so far").
- **Export CSV** of the shifts (with the current filters) or of the weekly hours, e.g. for payroll: UTF-8 so Excel shows
  Bengali names, and cells that would start a formula are made safe. Up to 20,000 shifts per file.
- Times are typed and shown in the business time zone (Settings → Business details) and stay right when the clocks change.

### Customers

`/admin/customers`, for staff with "Customers: View".
- **Who counts as a customer:** **Accounts** (registered accounts with the Customer role or at least one order, so
  staff-only accounts are not listed), **Guests** (orders placed without an account, one row per email) and
  **Restaurants** (accounts with a standing order, approved or not). Tabs with counts, search by name, email, phone or
  restaurant name, and sorting by last order, order value, name or newest.
- Every order is counted once: on the account that placed it, else on the account whose **confirmed** email it was placed
  with, else on the guest with that email.
- **Orders** and **Order value** leave out cancelled orders and include orders not delivered yet, such as a restaurant's
  generated upcoming deliveries. **Order value is what was ordered, not what was paid:** payments are not tracked.
- **A customer's page:** order count, order value, average order, first and last order; their orders (the newest 50,
  cancelled ones greyed out); contact details, saved address and latest delivery address; restaurant standing orders; and
  inquiries sent with the same email (only for staff who may see inquiries). Orders and standing orders link to their pages
  for staff who may open them. Guest pages are addressed by an order number, never by the email, and say so when an
  unconfirmed account uses the same email.

---

## Quick start

Prerequisite: the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet run --project src/ShutkiVorta.Web
```

On first start the app creates the SQLite database (`src/ShutkiVorta.Web/App_Data/shutkivorta.db`),
applies the schema, and seeds the roles (Admin, Manager, Employee, Customer), an administrator, the 9 starter menu
items and a starter inventory (23 common ingredient and supply names plus three sample purchases, marked "Sample entry"
in their notes, to delete once you record real ones).

| What | Value |
|---|---|
| Website | the URL printed in the console (e.g. `http://localhost:5072`) |
| Admin panel | `/admin` |
| Admin login | `admin@example.com` / `ChangeMe!2026` (set in `Seed`, **change before going live**) |
| Time clock | `/clock` (opens `/admin/time-clock`). Phones share their location only over https; `localhost` counts as secure. |
| Development emails | written to `src/ShutkiVorta.Web/App_Data/mail/` as `.eml` and `.html` files |

---

## Settings

There are two kinds of settings:

| Where | What | Who changes it |
|---|---|---|
| **Admin → Settings** (stored in the database) | Business details, online orders, restaurant orders, email (incl. the SMTP server and password), website & search engines, customer accounts, spam protection, time clock | Admins, in the browser. **Changes apply immediately**, with no restart, and survive updates. |
| `src/ShutkiVorta.Web/appsettings.json` (or environment variables) | Database connection, data-protection keys, logging, allowed hosts, HTTPS redirection, the email pickup folder, the receipt photo folder, the first admin account (`Seed`) | Whoever hosts the site. Read at startup. |

### Admin → Settings

Each page validates what you enter (with the error shown next to the field) and records every change in
**Recent changes** (when, what, from, to, by whom). Notes on specific settings:

- **Email → Password** is stored encrypted with ASP.NET Data Protection and is never shown again. Leave the box
  blank to keep it. If you change the server, port, user name or encryption, you must type the password again,
  so a saved password can't be sent to a different server.
- **Online orders → Sales tax** is entered as a percentage (`8.25` for Dallas). **Delivery ZIP codes** take one
  code or prefix per line (`752` = all of 752xx); leave the list empty to deliver anywhere.
- **Website → Website address** (`Site:BaseUrl`) should be your public `https://` domain in production (used for
  canonical URLs, the sitemap and links in emails). Leave it empty to use the address the site is reached on, **except for
  staff invitations**: while it is empty, invitations and "You've been added to the staff" emails are not sent (a link
  built from the address a visitor used could point anywhere). The admin still sees the set-password link once, with a
  warning to send it themselves.
- **Customer accounts → Require email confirmation before sign-in** applies at the next sign-in. Administrators are never
  blocked by it. (Managers and Employees need a confirmed email for the staff pages anyway; setting their password
  through the invitation confirms it.)
- **Time clock:**
  - **Kitchen latitude / longitude** is private: it is only used to work out how far from the kitchen staff clocked in or
    out, and is separate from the map position in Business details. Leave both empty to record no distances. Easiest: an
    Admin standing in the kitchen taps **Use my current location as the kitchen** on **Time clock → Team** (refused when
    the phone's location is less precise than 1 km). A new location applies from the next clock-in.
  - **Counts as at the kitchen within** (25 m to 50 km, 300 m by default): clock-ins and clock-outs farther away are
    flagged for review.
  - **Workweek starts on** (Monday by default): weekly hours and overtime (over 40 hours) are counted from this day.
  - **Require location** (off by default): when on, a clock-in or clock-out without the device's location is refused with
    instructions to turn location on; a manager can still add the shift by hand. When off, the shift is recorded and
    flagged for review.
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
set it to `false` before upgrading a live site if you don't want the samples. The `Seed:AdminEmail` account is created
as an Admin when no account has that email. An existing account with that email is made an Admin only when **no** account
holds the Admin role (a way back in if every admin was removed), so an admin who was deliberately demoted or removed stays
that way.

**Keep the data-protection keys.** They protect sign-in cookies, the links in staff invitations and in
password-reset and confirmation emails, and the saved SMTP password. Store `KeysPath` on persistent storage and back
it up together with the database. If the keys are lost, everyone is signed out, links already emailed stop working
(send staff a new invitation from their page on Users) and the SMTP password has to be entered again. Until then,
emails wait in the outbox and are retried; they are not lost.

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

**Upgrading a live site to the version with staff roles and the time clock:** nothing to do by hand. On the first start
the migrations add the tables for permissions, staff profiles, the staff audit log and shifts, and the Manager and
Employee roles are created. Everyone who was an Admin stays an Admin with every permission, and their email is marked
confirmed so they keep their access to the admin pages. Nobody else gets staff access until an Admin adds them on
**Users**, and Managers and Employees start with the default permissions. Customer accounts and orders are unchanged and
appear on the new Customers page; staff roles are now given on Users, no longer on the customer list. The new **Time
clock** settings page starts with its defaults (no kitchen location, 300 m, Monday, location not required) and is not
flagged with "Please check these settings".

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
                               InventoryPurchase (+ lines, receipts), InventoryItem (saved item names),
                               TimeClockShift (+ punch locations, shift rules, business-time conversions)
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
- **Security in depth:** staff roles and permissions are read from the database on every request (never from the
  sign-in cookie). The `/admin` folder requires staff access, every admin page and action handler states what it needs
  (`[RequirePermission]`, `[AdminOnly]`, `[AnyStaff]`, checked by `PermissionPageFilter`), *and* every staff request carries
  `[RequirePermission]`, `IRequireAdmin` or `IRequireStaff`, which the MediatR `AuthorizationBehavior` enforces. The
  modules, their levels and the Manager and Employee defaults are defined in one place, `PermissionCatalog`.
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

`PageAccessCoverageTests` fails when an admin page does not state who may open it, or when an action handler on a
view-only page does not state what it needs, so a new page cannot be left open by mistake.

---

## Going live checklist

1. Change `Seed:AdminPassword` (or sign in and change the password right away). In Admin → Settings → Business
   details, enter your real name, phone, email and address. Then add the people who work with you on **Users**, each
   with their own account and role, instead of sharing the admin login, and check what Managers and Employees may do on
   **Permissions**.
2. In Admin → Settings → Email, fill in the mail server, sender address and who is notified about new orders;
   then run **Test connection** and **Send test email**, and check that no warnings remain. Staff invitations are
   emailed too: until email is set up, a new person's page shows that the invitation was only saved to a folder on the
   server (or why sending failed), and you have to send them the link yourself.
3. In Admin → Settings → Website, set the website address (`Site:BaseUrl`) to your domain (e.g.
   `https://www.yourdomain.com`). Until it is set, staff invitations are not emailed at all: you only get the link to
   send yourself.
4. Run behind HTTPS. Phones and browsers share their location with the time clock only over https: without it every
   clock-in and clock-out is recorded without a location and flagged for review (or refused, when **Require location** is
   on). Behind a reverse proxy, set `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`.
5. Set the kitchen location for the time clock: stand in the kitchen and tap **Use my current location as the kitchen**
   on Time clock → Team, or enter it in Admin → Settings → Time clock. Check the radius, the day the workweek starts
   and **Require location** there too.
6. Persist and back up the `App_Data/` folder next to the app (`src/ShutkiVorta.Web/App_Data/` when run from the source):
   the SQLite database `shutkivorta.db` (orders, customers, staff, permissions and every shift), the data-protection keys
   in `keys/` and the receipt photos in `receipts/`; and `wwwroot/uploads/` (menu photos). With SQL Server, back up the
   database instead, and point `DataProtection:KeysPath` and `Inventory:ReceiptsPath` to persistent storage that is
   backed up.
7. Submit `https://yourdomain.com/sitemap.xml` in Google Search Console and Bing Webmaster Tools.

## Photos

The starter menu photos are Creative Commons images from Wikimedia Commons (credited on each item page and
on the About page). Replace them any time from **Admin → Menu items → Edit → Upload a new photo**.
