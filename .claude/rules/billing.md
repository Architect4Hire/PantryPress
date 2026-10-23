# Billing and entitlements

PP-D09, PP-D10, BILL-001 to BILL-008.

- Stripe owns prices, checkout, invoices, tax, the customer portal, and dunning. PantryPress owns an
  entitlements read model derived from verified, de-duplicated webhooks processed through the outbox.
- The subscription belongs to the paying account; entitlements apply to that account's sites.
- **Every gated feature asks the entitlements facade** (ARCH-010). Never check a plan name.
- Entitlements cover: sites, members, custom domains, branding removal, design options and presets, ads, ad
  networks, subscriber cap, storage, analytics retention, and import.
- Downgrade never deletes content or settings; over-limit items become read-only or hidden with a plain
  explanation. Failed payment: ReadOnly, then Offline, content retained (BILL-005).
- Trial (D-10 default, ADR 0010 proposed, closes at PP14): 14 days, reminders before and on the last day;
  expired trial shows "coming soon".
- Limits live in configuration, not code.
- Tests use Stripe test mode and recorded webhook fixtures.
