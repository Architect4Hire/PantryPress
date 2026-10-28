# PantryPress plans, pricing, and tiers

- **Status:** Recommendation for the owner. Nothing here is decided. It feeds **D-09** (prices and limits, ADR 0009)
  and **D-10** (trial or free tier, ADR 0010), which both close at PP14.
- **Date:** 2026-10-28. Azure prices are East US 2 (or Front Door "Zone 1") pay-as-you-go list prices from the Azure
  Retail Prices API on this date. Every other price was checked on the vendor's page on this date unless marked
  **unverified**.
- **Builds on:** [the 2026-10-24 draft](../../pricing/pantrypress-pricing-recommendation.md) and
  [the hosting plan](../../infrastructure/README.md). Where this document disagrees with either, it says so and why
  (section 11).
- **For:** the owner deciding PP-D09 and D-10, and the Claude Code session that writes the plan catalogue (PP14.1).

**Every price, limit, and duration in this document is a Recommendation.** None is decided until the owner closes
D-09 and D-10 in [the decision register](../../architecture-decisions/pantrypress.md).

---

## 1. Summary

| | Starter | **Plus (recommended)** | Pro |
| --- | --- | --- | --- |
| Monthly billing | **$9** / month | **$19** / month | **$59** / month |
| Annual billing | **$90** / year ($7.50 / month) | **$190** / year ($15.83 / month) | **$590** / year ($49.17 / month) |
| Annual saving | $18 (two months free) | $38 (two months free) | $118 (two months free) |
| Monthly pageviews included (soft) | 10,000 | 50,000 | 200,000 across all sites |
| Sites | 1 | 1 | 3 |
| Address | `{name}.pantrypress.app` | Plus one custom domain | Plus one custom domain per site |
| Storage | 2 GB | 20 GB | 100 GB |
| Ads | No | AdSense | AdSense and one allow-listed ad network |
| Support | Help center | Email | Priority email |

All three are **Recommendations**.

- **Trial (D-10):** a 14-day trial with Plus features, no card, no free-forever tier. Trial sites are `noindex`, have
  no custom domain and no ads, and are capped at 1 GB of storage (section 7).
- **Recommended plan:** Plus. It is the cheapest plan that can pay for itself (custom domain and AdSense, BIZ-007), and
  its margin holds up at heavy use. The landing page says "Recommended", never "most popular": there is no customer
  data yet.
- **The one new limit** is a soft monthly pageview allowance. Traffic is the only cost that grows with a blogger's
  success. Going over it brings notices and, if it continues, a request to upgrade. It never slows a site, takes one
  offline, or deletes anything (section 5.3).

**Cost basis, in one line:** production runs about **$248 a month fixed** (about $308 with the dev environment), and
every **100,000 pageviews cost about $13.26** at a 1.2 MB average page. Page weight drives that figure; the cache hit
ratio barely moves it.

---

## 2. Cost model

### 2.1 Rates used

All rates are the Azure Retail Prices API's list prices on 2026-10-28, East US 2 or Front Door Zone 1 (where most
US and European readers are served). Section 13 lists the sources.

| Service | Meter | Rate |
| --- | --- | --- |
| Front Door Standard | Base fee per profile | $35.00 / month |
| | Data transfer, edge to client, Zone 1, first 10 TB | $0.0825 / GB (then $0.065 to 50 TB, $0.056 to 150 TB) |
| | Data transfer, edge to client, Zones 2 to 5 (Asia, South America, Australia, India), first 10 TB | $0.109 to $0.115 / GB |
| | Requests, Zone 1, first 250 million | $0.009 / 10,000 |
| | Edge to origin | $0.02 / GB |
| | Origin in Azure to edge | Free |
| | Custom domains (first 100 per profile) and managed certificates | Free |
| | WAF custom rules | Free on Standard (Microsoft Learn comparison table). The hosting plan budgets $14 a month for them; confirm on the first invoice |
| Container Apps (Consumption) | vCPU active / idle | $0.000024 / $0.000003 per vCPU-second |
| | Memory (active or idle) | $0.000003 per GiB-second |
| | Requests | $0.40 / million |
| | Free each month, per subscription | 180,000 vCPU-s, 360,000 GiB-s, 2 million requests |
| Azure SQL (DTU) | S0 / S1 / S2 / S3 / S4 | $0.4839 / $0.9677 / $2.42 / $4.8387 / $9.678 per day |
| | Serverless General Purpose (rejected by the hosting plan) | $0.5218 per vCore-hour |
| Azure Managed Redis (Balanced) | B0 / B1 / B3 / B5, per node | $0.016 / $0.032 / $0.065 / $0.156 per hour |
| Blob storage (GPv2) | Hot ZRS / Hot LRS / Cool ZRS | $0.023 / $0.0184 / $0.0125 per GB-month |
| | Hot ZRS writes / reads | $0.0625 / $0.004 per 10,000 |
| Defender for Storage | Per storage account | $0.0134 / hour |
| | Malware scanning | $0.15 / GB scanned |
| Log Analytics | Analytics logs ingestion | First 5 GB a month free, then $2.76 / GB |
| | Basic logs / Auxiliary logs ingestion | $0.50 / $0.05 per GB |
| Key Vault (Standard) | Operations | $0.03 / 10,000 |
| Azure DNS | Public zone / queries | $0.50 per zone-month / $0.40 per million |
| Private Link | Private endpoint | $0.01 / hour |
| Communication Services Email | Per email / per MB | $0.00025 / $0.00012 |
| Container Registry | Basic | $0.1666 / day |
| Stripe | Domestic card | 2.9% + $0.30 per successful charge |
| | International card / currency conversion | +1.5% / +1% |
| | Billing (pay as you go) | 0.7% of billing volume |
| | Tax Basic (Billing and Checkout) | 0.5% per transaction where registered |
| | Dispute | $15, plus $15 to contest it manually |

A month is 730 hours (2,628,000 seconds, 30.42 days).

### 2.2 Fixed monthly platform cost

Production at launch, using the SKUs in `docs/infrastructure/terraform/pantrypress/env/prod.tfvars`.

| Line | Formula | $ / month |
| --- | --- | ---: |
| Front Door Standard base | 1 profile. It is shared with CreatorPantry (ADR 0016), but charged here in full until CreatorPantry pays its share | 35.00 |
| WAF (2 policies, custom rules) | Free on Standard. The hosting plan budgets $14 | 0.00 |
| Container Apps, memory | 5 apps × 1 GiB × 2,628,000 s = 13,140,000 GiB-s, less 360,000 free, × $0.000003 | 38.34 |
| Container Apps, vCPU | 5 apps × 0.5 vCPU × 2,628,000 s = 6,570,000 vCPU-s. Assumed active share: Worker 50%, SSR 40%, Site 40%, ApiService 15%, Gateway 10% = 2,036,700 active vCPU-s. (2,036,700 − 180,000) × $0.000024 + 4,533,300 idle × $0.000003 | 58.16 |
| Azure SQL S1 | $0.9677 × 30.42 days | 29.43 |
| Managed Redis B0 with HA (2 nodes) | 2 × $0.016 × 730 | 23.36 |
| Private endpoints (SQL, Redis, blob, Key Vault) | 4 × $0.01 × 730 | 29.20 |
| Private DNS zones | 4 × $0.50 | 2.00 |
| Defender for Storage (malware scanning, PP6.3; **not in the Terraform yet**) | $0.0134 × 730, one storage account | 9.78 |
| Storage at launch | 20 GB × $0.023, plus about $1 in operations | 1.46 |
| Key Vault | About 170,000 operations × $0.03 / 10,000 | 0.50 |
| Public DNS | 1 zone $0.50, plus about 2 million queries × $0.40 / million | 1.30 |
| Account email | About 2,000 emails × $0.00025, plus 100 MB × $0.00012 | 0.51 |
| Log Analytics and App Insights | About 10 GB a month, less 5 GB free, × $2.76 | 13.80 |
| Container Registry Basic (serves dev and prod) | $0.1666 × 30.42 | 5.07 |
| **Production total** | | **≈ 248** |
| Dev environment | The hosting plan's figure | ≈ 60 |
| **Business total** | | **≈ 308** |

**Range:** production costs $195 to $400 a month before traffic. The low end has every app idle and no billable logs.
The high end has every app active ($191.70 of compute instead of $96.50) and 30 GB of logs ($69). The vCPU active share
is the softest number here. Measure it in the first month with the Container Apps "Replica count" and the
billing export, and replace the assumption.

**What sets the fixed cost:** about half of it is the always-on public tier (one SSR, Site, Gateway, ApiService,
and Worker replica each, which the hosting plan chose so a cache miss never waits for a cold start), plus private
endpoints, which are a security choice and not a cost necessity. The infra README lists the ways to cut it (no
private endpoints −$31, Redis without HA −$12, SQL S0 −$15). None of them changes the pricing.

### 2.3 Variable cost per 100,000 pageviews

**Assumptions (base case):**

| Assumption | Value | Why |
| --- | --- | --- |
| Bytes sent from our edge per pageview | 1.2 MB | HTML about 50 KB compressed; first-party CSS and JS under 100 KB (RND-007), and cached by the browser on repeat views; up to 4 font files (about 100 KB); about 10 images × 95 KB as AVIF/WebP at phone widths, lazy below the fold. Ads are excluded: ad networks serve their own bytes |
| Requests per pageview | 25 | 1 HTML, about 5 CSS/JS, 2 to 4 fonts, about 12 images, 1 analytics beacon, 1 or 2 island JSON calls |
| Edge cache hit ratio | 95% of requests | PNFR-002's target |
| Reader location | Zone 1 | US and Europe |
| Origin work per cache miss | 0.15 vCPU-s per SSR render; 0.005 vCPU-s per beacon | Estimates. Measure them in PP7 and PP9 |
| Telemetry | 1.5 KB of App Insights per origin request, not sampled | |

**Formulas**, where V is pageviews, W is MB per view, R is requests per view, and h is the hit ratio:

```text
edge egress     = V × W / 1000 GB × $0.0825                 = 100,000 × 1.2 / 1000 = 120 GB × $0.0825  = $9.90
edge requests   = V × R / 10,000 × $0.009                   = 2,500,000 / 10,000 × $0.009             = $2.25
edge to origin  = V × R × (1 − h) × 2 KB × $0.02 / GB                                                  = $0.01
blob reads      = V × R × (1 − h) / 10,000 × $0.004                                                    = $0.05
origin compute  = (V × (1 − h) × 0.15 + V × 0.005) vCPU-s × $0.000024                                  = $0.03
origin requests = (V × R × (1 − h) + V) / 1,000,000 × $0.40     (225,000 requests)                     = $0.09
telemetry       = 225,000 × 1.5 KB = 0.34 GB × $2.76                                                   = $0.93
                                                                                          total ≈ $13.26
```

**Front Door's egress is about three-quarters of the variable cost.** Every byte a reader receives is billed at the
same rate whether it came from cache or from the origin. A cache miss costs origin compute, which is cents. So the
cache hit ratio protects capacity and uptime, not the bill. Page weight sets the bill.

**Sensitivity, $ per 100,000 pageviews:**

| Page weight | Hit ratio 90% | 95% (base) | 98% | 15 requests per view | 40 requests per view |
| --- | ---: | ---: | ---: | ---: | ---: |
| 0.6 MB (lean: few images, aggressive AVIF) | 8.95 | 8.31 | 7.92 | 7.16 | 10.03 |
| **1.2 MB (base)** | 13.90 | **13.26** | 12.87 | 12.11 | 14.98 |
| 2.0 MB (the hosting plan's assumption) | 20.50 | 19.86 | 19.47 | 18.71 | 21.58 |
| 3.0 MB (typical unoptimized WordPress food post) | 28.75 | 28.11 | 27.72 | 26.96 | 29.83 |

Two things can swing the figure more than the table shows:

- **Readers outside Zone 1** cost $0.109 to $0.115 per GB instead of $0.0825, which is 32% to 39% more egress. A blog
  with many Asian or Australian readers moves toward the 2.0 MB row.
- **Front Door access logs sent to Log Analytics** add about 2.5 GB per 100,000 pageviews (25 requests at roughly
  1 KB per log line), or **+$6.90 per 100,000** on analytics logs. That is half again the whole variable cost. Basic
  logs cost $1.25 and Auxiliary logs $0.13. The Terraform has no Front Door diagnostic setting today. Keep it that way,
  or send them to a storage account or the Auxiliary tier (section 8).

### 2.4 Other unit costs

| Unit | Formula | Cost |
| --- | --- | --- |
| **Media storage**, per GB of quota a month | Originals plus about 50% more for derivatives (AVIF, WebP, JPEG widths, the pinnable image): 1.5 GB × $0.023 hot ZRS | **$0.035**. About $0.024 if originals move to Cool after 30 days (1 × $0.0125 + 0.5 × $0.023) |
| **Malware scanning**, per GB uploaded, once | $0.15 / GB | $0.15. A 20 GB WordPress import costs $3.00 to scan, which is more than 6 months of storing it |
| **Custom domain** | Free on Standard (first 100 per profile, managed certificate included). It costs money only when it fills a profile: D-13b adds a $35 profile before 80 domains | $0 marginal; **about $0.44** a month amortized ($35 ÷ 80) |
| **Per-site overhead**, idle site | Page data about 50 MB × $0.023, its Redis share, SQL rows, sitemap and feed rebuilds | Under **$0.05** a month. It doesn't matter. Support time per site matters more |
| **Email** | $0.00025 per email + $0.00012 per MB (about 50 KB each) | About **$0.26 per 1,000 emails**. A Plus site sending 300 a month (double opt-in confirmations, contact form, comment notices) costs $0.08 |
| **Stripe**, per charge | 2.9% + 0.7% + 0.5% (Stripe Tax, where registered) + $0.30 | 4.1% + $0.30. On $9 monthly: $0.67 (7.4%). On $90 annually: $3.99, or $0.33 a month |

Newsletters are never sent (PP-D14), so email stays a rounding error. The contact form and double opt-in are its only
abuse risk, and the public endpoints' rate limits already cover them.

---

## 3. Unit economics per plan

**Contribution = price − Stripe (fees + Tax) − traffic − storage − support.** Support time is valued at $60 an hour
(the draft's figure): Starter 1 minute, Plus 3 minutes, and Pro 8 minutes per account per month. These are
assumptions, not measurements.

| Plan | Usage assumed (pageviews, storage) |
| --- | --- |
| Starter | typical 5,000 and 1 GB; heavy 10,000 and 2 GB |
| Plus | typical 25,000 and 8 GB; heavy 50,000 and 20 GB |
| Pro | typical 100,000 and 40 GB; heavy 200,000 and 100 GB |

"Heavy" means a blogger using the whole plan allowance.

| Plan, billing, usage | Price / mo | Stripe + Tax | Traffic | Storage | Support | **Contribution** |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Starter annual, typical | 7.50 | 0.33 | 0.66 | 0.04 | 1.00 | **5.47 (73%)** |
| Starter annual, heavy | 7.50 | 0.33 | 1.33 | 0.07 | 1.00 | **4.77 (64%)** |
| Starter monthly, typical | 9.00 | 0.67 | 0.66 | 0.04 | 1.00 | **6.63 (74%)** |
| Starter monthly, heavy | 9.00 | 0.67 | 1.33 | 0.07 | 1.00 | **5.93 (66%)** |
| Plus annual, typical | 15.83 | 0.67 | 3.31 | 0.28 | 3.00 | **8.56 (54%)** |
| Plus annual, heavy | 15.83 | 0.67 | 6.63 | 0.70 | 3.00 | **4.83 (31%)** |
| Plus monthly, typical | 19.00 | 1.08 | 3.31 | 0.28 | 3.00 | **11.33 (60%)** |
| Plus monthly, heavy | 19.00 | 1.08 | 6.63 | 0.70 | 3.00 | **7.59 (40%)** |
| Pro annual, typical | 49.17 | 2.04 | 13.26 | 1.40 | 8.00 | **24.47 (50%)** |
| Pro annual, heavy | 49.17 | 2.04 | 26.52 | 3.50 | 8.00 | **9.11 (19%)** |
| Pro monthly, typical | 59.00 | 2.72 | 13.26 | 1.40 | 8.00 | **33.62 (57%)** |
| Pro monthly, heavy | 59.00 | 2.72 | 26.52 | 3.50 | 8.00 | **18.26 (31%)** |

On the draft's Pro price ($45 a month annual), at these costs a Pro account at its full 200,000 allowance contributes
**$5.11**, or 11%. That is the main reason this document raises Pro (section 11).

### 3.1 Break-even and scale

These figures need a plan mix, and there is no customer data yet. This section uses the draft's **assumed** mix (25%
Starter, 60% Plus, 15% Pro), half on annual billing, at typical usage. That gives an average contribution of **$11.84
per paying account** and an average revenue of **$20.62**. Contribution already pays for support time at $60 an hour.

| Paying accounts | MRR | Fixed cost (est.) | Profit / month | Infra as % of revenue (KPI-006) | Support hours / month |
| ---: | ---: | ---: | ---: | ---: | ---: |
| 25 | 516 | 308 | −12 | 82% | 1.4 |
| 50 | 1,031 | 308 | 284 | 52% | 2.7 |
| 100 | 2,062 | 308 | 876 | 37% | 5.4 |
| 250 | 5,156 | 450 | 2,509 | 31% | 13.5 |
| 500 | 10,312 | 560 | 5,358 | 27% | 27.1 |
| 1,000 | 20,625 | 860 | 10,976 | 26% | 54.2 |
| 2,000 | 41,250 | 1,350 | 22,323 | 25% | 108.3 |

- **Break-even: about 26 paying accounts** ($308 ÷ $11.84).
- **How fixed cost steps up:**
  - At 250 accounts: SQL S2 (+$44), Redis B1 with HA (+$23), more compute (+$50), more logs (+$25).
  - At 500: a second Front Door profile, because about 125 custom domains are expected if 30% of Plus and Pro accounts
    attach one, which is an assumption (+$35). More compute (+$50) and logs (+$25).
  - At 1,000: SQL S3 (+$74), Redis B3 with HA (+$48), four profiles (+$70), compute (+$80), logs (+$28).
  - At 2,000: SQL S4 (+$147), Redis B5 with HA (+$133), seven profiles (+$105), compute (+$80), logs (+$25).

  These are estimates to be replaced by invoices.
- **KPI-006 ("infrastructure under 15% of revenue per site") is not reachable at 1.2 MB pages.** It levels off at about
  25%, because egress is most of the cost and grows with revenue. Even at 0.7 MB pages it is about 20%. Section 12
  asks the owner to restate it.
- **Support, not Azure, is the binding constraint.** At these assumptions, 1,000 accounts need 54 hours a month.
  KPI-007 allows under 4 hours a week, about 17 a month. The product has to make support rare (help center, plain
  in-app explanations, no infrastructure jargon), or the operator has to buy help at about 300 accounts. The draft
  found the same thing.

### 3.2 A viral Pinterest spike

**Scenario:** a Starter site gets 1,000,000 pageviews in one week, against an allowance of 10,000 a month.

```text
egress    1,000,000 × 1.2 MB = 1,200 GB × $0.0825      = $99.00
requests  25,000,000 / 10,000 × $0.009                 = $22.50
origin, telemetry (10 × $1.11 per 100k)                = $11.07
                                                 total ≈ $133
```

That is about 18 months of the Starter annual price. At 0.8 MB pages it is about $100 ($66 of egress).

**Capacity is not a problem.** One million views in a week averages 1.7 a second. Even a 20-to-50-a-second peak is
what PNFR-004 sizes the edge for, and the same few pages stay hot in cache.

**How the plan protects the business without hurting the blogger:**

1. **The allowance is soft and monthly.** A one-month spike brings an in-app notice and an email ("Your recipe is
   taking off"), never a charge, a slowdown, or a block. Throttling a viral food blog would be the worst marketing
   PantryPress could buy. The Starter footer, "Made with PantryPress", has just been shown to a million readers.
2. **Only sustained use asks for an upgrade.** The rule is above 150% of the allowance in 2 of the last 3 months. The
   owner gets a plain request to move to the plan that fits, with 30 days' notice. If they don't, the operator decides
   case by case through an audited override (BILL-008). Nothing changes automatically.
3. **The site is never taken offline or deleted over traffic.** `ReadOnly` and `Offline` are only for non-payment
   (BILL-005), and only after notice.
4. **The platform absorbs one-off spikes.** One $133 spike is affordable once there are about 30 paying accounts. The
   OPS-009 budget alert (prod `amount = 350`) will fire on a spike month. That is its job, and the budget should rise
   with MRR (section 8).
5. **Bots are not readers.** Pageviews are counted by the cookie-free analytics beacon (ANL-001), which is the number
   the blogger sees on their dashboard. Scrapers and bots don't count against the blogger. They cost the platform
   egress, which the WAF rate-limit rule (2,000 a minute per address in prod) and scanner blocks handle.

---

## 4. Tier design

### 4.1 Limits mapped to requirements section 2.3

Each limit is driven by **cost** (it would lose money without it), **abuse** (it would be exploited without it), or
**value** (it is a reason to upgrade and costs almost nothing).

| Capability (2.3 / BILL-003) | Starter | Plus | Pro | Driver and reason |
| --- | --- | --- | --- | --- |
| Sites | 1 | 1 | 3 | **Value**, with some support cost. Each site adds near-zero Azure cost (section 2.4) but more support surface |
| Members per site | 1 | 3 | 10 | **Value.** Each member is a row. Invitations send email, already rate-limited |
| Custom domains | None | 1 per account | 1 per site (3) | **Cost and abuse.** Each one uses a slot on a 100-domain Front Door profile (D-13b) and a certificate. Custom domains on throwaway accounts are a phishing tool (BIZ-009). As DOM-001 |
| "Made with PantryPress" footer | Required | Removable | Removable | **Value** |
| Site design (PP-D17) | 2 presets (Market, Hearth), mode, logo, tagline, brand and highlight color | **All presets and all Studio options** | All presets and all Studio options | **Value.** This differs from the 2.3 default, which holds textures and the Banner and Masthead headers back for Pro. It agrees with the draft: the gate is a weak reason to triple the price, it causes "why is this greyed out?" tickets, and it doubles the downgrade test matrix (CUS-T11). Closes as D-17 at PP11 |
| Editor, recipe card, jump, print, scaling, units, ratings, comments, publishing API | Yes | Yes | Yes | The product itself. Never gated |
| Newsletter subscribers (signup and CSV only) | 500 per site | 5,000 per site | 25,000 per site | **Value.** Cost is negligible: about $0.26 per 1,000 double opt-in emails. The cap also stops list-farming |
| AdSense | No | Yes | Yes | **Value** (BIZ-007), and the consent platform's cost (MON-004) |
| Affiliate links (automatic `rel` and disclosure) | **Yes** | Yes | Yes | **Value and compliance.** This differs from 2.3, which says No for Starter. A link can't be told from an affiliate link reliably, so the platform can't stop a Starter blogger from adding one. It can only withhold the automatic disclosure. Giving every plan the disclosure is safer for readers and for PantryPress |
| Ad-network readiness (ads.txt, slot layout) | No | No | Yes | **Value.** Ad networks want traffic, which is Pro territory |
| Analytics | 30 days, page views | 12 months, full dashboard | 24 months, full dashboard, CSV export | **Value.** Daily aggregates cost almost nothing to keep |
| Media storage (per account) | 2 GB | 20 GB | 100 GB | **Abuse first, then cost.** At $0.035 per GB, even 100 GB is $3.50 a month. The cap stops file-hosting abuse and keeps imports bounded |
| WordPress import | No (the trial can import) | One-time | Unlimited | **Cost and support.** Imports cost scanning ($0.15 per GB) and Worker time, and cause the most tickets of anything here |
| Support | Help center | Email | Priority email, first answer within one business day (OPS-010) | **Cost.** Time is the scarcest resource |
| **Monthly pageviews (new)** | **10,000** | **50,000** | **200,000 across all sites** | **Cost.** The only cost that grows with success. Soft, as in section 5.3 |

### 4.2 Cost-driven or value-driven

- **Cost-driven:** monthly pageviews, custom domains (profile slots), WordPress import, and support level.
- **Abuse-driven:** custom domains, storage, subscriber caps, and the trial's limits.
- **Value-driven (cheap to give):** sites, members, design, branding removal, ads, ad networks, and analytics
  retention.

Value-driven limits are where the owner can be generous. Cost-driven ones are where the numbers in section 3 come
from.

### 4.3 Limits not recommended

- **No post, recipe, or page count limit.** It goes against "the blogger always owns their content" (BIZ-006), and an
  extra post costs nothing.
- **No upload size or rate limits by plan.** These are platform-wide safety settings in configuration, the same for
  everyone.
- **No metered overage billing at launch.** The draft has $15 per extra 100,000 pageviews on Pro. See section 11.

---

## 5. Pricing mechanics

### 5.1 Why these prices

| Plan | Positioning (prices checked 2026-10-28) |
| --- | --- |
| Starter $7.50 annual / $9 monthly | Low-commitment entry. It is not meant to win on price: WordPress.com Personal is $4 / $9 and includes a custom domain. Starter wins on the recipe card, speed, and no plugins. It is well under Squarespace Basic ($19 / $25) and Wix Light ($17) |
| **Plus $15.83 annual / $19 monthly** | The plan that pays for itself: custom domain, AdSense, every design option. Below WordPress.com Business ($25 / $40), Squarespace Core ($29 / $39), Wix Core ($29), and Ghost Starter ($18) |
| Pro $49.17 annual / $59 monthly | For an earning blogger. A self-hosted serious food blog costs about **$79 a month**: BigScoots Essential 125 at $50 a month annual, the Feast Plugin at $249 a year ($20.75), and WP Recipe Maker Pro at $99 a year ($8.25). That is before plugin upkeep. Pro undercuts it by $30 and has three sites |

A hobby blogger's self-hosted stack is about $41 a month: BigScoots Essential 75 at $29.17 annual, Feast Plugin
Starter at $97 a year ($8.08), and WP Recipe Maker Premium at $49 a year ($4.08). Plus is less than half of that.

### 5.2 Annual discount

**Recommendation: annual = 10 × monthly ("two months free", 16.7% off), on every plan, shown annual-first.**

The draft says "two months free", but its prices give different discounts on each plan: Starter $6 against $9 is 33%,
Plus $15 against $19 is 21%, and Pro $45 against $55 is 18%. One rule is simpler to explain, and the catalogue can
work out the saving from it.

Annual billing matters for a solo operator:

- Stripe's $0.30 is paid once a year instead of twelve times. On Starter that is $0.33 a month instead of $0.67.
- There are fewer failed-payment cycles (BILL-005).
- Churn is lower.

### 5.3 How the pageview allowance works

- **Counted by** the analytics beacon (ANL-001) across the account's sites, per calendar month in UTC. It is the same
  number the blogger sees.
- **At 80%:** an in-app note.
- **At 100%:** an in-app note and an email, with an upgrade button. Nothing about the site changes.
- **Sustained** (above 150% in 2 of the last 3 months): a plain request to move to the plan that fits, with 30 days'
  notice. Then an operator decision with an audited override (BILL-008). Nothing is automatic.
- **Beyond Pro:** at launch, the operator sets an override. Once three or more Pro accounts need it, add a
  **flat traffic add-on**: +100,000 pageviews a month for $20, as a licensed quantity on the subscription. That is
  not metered usage, so there are no surprise invoices. At $13.26 cost plus 4.1% Stripe, it contributes about $5.90.
- **Never:** throttling, taking a site offline, or deleting anything for traffic.

### 5.4 Currency, tax, refunds, cancellation (for the owner to decide)

| Topic | Recommendation |
| --- | --- |
| Currency | **USD only at launch.** Foreign cards cost +1.5%, and showing a local currency adds +1%. Revisit EUR and GBP once those countries are over 20% of revenue |
| Tax | **Stripe Tax Basic** (BILL-007), 0.5% per transaction where registered. Register in the home state first; Stripe Tax watches US thresholds. **Before selling to EU or UK consumers, decide:** register for the EU non-Union OSS and UK VAT, or limit checkout to countries where PantryPress is registered. For a seller outside the EU or UK, both generally apply from the first sale to a consumer. **Confirm with an accountant; this is not tax advice.** ADR 0009 rejected a merchant of record, which would have taken this on |
| Prices shown | US prices exclude tax ("plus tax where applicable"). If EU and UK sales open, show prices including tax there, as consumers expect. Stripe supports both |
| Upgrades | Immediate, prorated (Stripe's default) |
| Downgrades | At the end of the paid period. Over-limit items become read-only or hidden with a plain explanation, never deleted (BILL-004) |
| Refunds, monthly | None for partial months. Cancel anytime, effective at period end |
| Refunds, annual | Full refund if asked within 14 days of the first annual charge. Otherwise none, and the plan runs to its end. Send a renewal reminder 7 days before each annual renewal. Some US states and the EU and UK have automatic-renewal and consumer-withdrawal rules: **have them checked.** Stripe keeps its processing fee on refunds (**unverified for 2026 terms**) |
| Voluntary cancellation | The site works to the end of the paid period, then shows "coming soon" like an expired trial. Content and export are kept 90 days (BIZ-006), with reminders at 30 and 7 days before erasure. These are recommended durations |
| Non-payment | As BILL-005: `ReadOnly` 14 days, then `Offline`, with content kept 90 days |

---

## 6. Founding-member offer (optional)

The draft offers Plus at $10 a month annual, "locked for life", for the first 100 sites. **Recommendation: keep the
offer, change the lock.** Make it Plus at **$120 a year**, on annual billing only, for the first 100 accounts, locked
**while the subscription stays active without a gap**, and stated that way. "For life" invites arguments about
reactivated or transferred accounts and stops repricing for ever. At 100 accounts it forgoes about $583 a month
against list price ($190 − $120 = $70 a year each, × 100 ÷ 12). That is cheap for 100 early bloggers who will report bugs.

---

## 7. Trial or free tier (D-10)

**Recommendation: keep the default. A 14-day trial with Plus features, then pay. No free-forever tier, no card
required.**

**Why not a free tier:**

- **Cost.** A free site at 2,000 pageviews a month costs about $0.27 in traffic, plus storage and a share of
  support. A thousand of them is about $300 a month in Azure, and more in operator time than KPI-007 allows.
- **Abuse (BIZ-009).** Free subdomains on a reputable parent domain are what spammers and phishers look for. The cost
  is not Azure. It is the operator's evenings, plus the risk to the reputation of `pantrypress.app` with Safe Browsing
  and email providers. That would hurt every paying blogger.
- **SEO footprint**, the main argument for a free tier, is better earned by the "Made with PantryPress" footer on
  Starter and on trial sites.

**Why no card:** the provisioning design already assumes it (tenant-provisioning section 4: no Stripe call at sign-up).
It keeps sign-up within BIZ-002's 10 minutes, and the abuse controls in front of sign-up do the filtering a card
would: disposable-email blocking, the spam challenge, per-address limits, velocity limits, and verified email before
publishing. Keep it a configuration switch (`Billing:Trial:RequireCard`), so the owner can turn it on if trial abuse
shows up.

**What the trial includes and caps:**

| Item | Trial | Reason |
| --- | --- | --- |
| Features | Plus | Shows the recommended plan at its best |
| Sites | 1 | Abuse |
| Members | Owner only | Invitations send email (abuse) |
| Custom domain | No | Front Door profile slots and phishing (BIZ-009) |
| Ads | No | Nothing to earn in 14 days, and ads on trial sites are an abuse vector |
| Storage | 1 GB | A WordPress import pauses at the cap and finishes when the blogger subscribes. **This needs a small change to the import prompts (PP16)**; without it, cap the import at 1 GB and say so plainly |
| WordPress import | One, within the storage cap | It is the strongest conversion lever for WordPress movers (BIZ-011) |
| Subscribers | 100 | Double opt-in emails (abuse) |
| Search indexing | **`noindex`** on every page, sitemap not offered | Takes the SEO value out of spam sites. It also keeps Google from indexing a trial copy of a blog being imported from WordPress, which would duplicate the original. Removed on payment, then the sitemap is submitted |
| Public access | Yes, at `{slug}.pantrypress.app` once the email is verified (ACCT-001), with a small "Trial site" note in the footer | BIZ-002's "working blog in 10 minutes". Safe Browsing checks on publish (OPS-007) |
| Pageviews | 5,000 soft | Same soft rule. A trial site with real traffic is almost always an import |
| After expiry | "Coming soon" (BILL-006), content kept **60 days**, reminders 3 days before and on the day (BILL-006), then at 30 and 7 days before erasure | Keeping data from abandoned trials indefinitely is cost and liability |

KPI-002 assumes 15% trial-to-paid. If conversion is under 10% after 200 trials, revisit the trial's length and content
before the prices.

---

## 8. Risks and levers

| Risk | What happens | Lever |
| --- | --- | --- |
| **Heavy pages** | Every extra MB per view adds about $8.25 per 100,000 views | The biggest lever. AVIF by default at a sane quality, responsive `srcset` (MED-002, RND-006), lazy below the fold. Add a per-page **image-bytes budget** to the RND-007 CI check. Measure real average transfer per view from Front Door's "Billable response size" against beacon pageviews |
| **Log Analytics surprises** | Front Door access logs to analytics logs add about 52% to traffic cost. Unsampled App Insights grows with every origin request. Prod has **no daily cap** (`log_daily_quota_gb = -1`) | Don't send Front Door access logs to analytics logs; use storage or the Auxiliary tier ($0.05 per GB) if they are needed. Sample App Insights on Site and SSR (10% to 25%). **Recommend a prod daily cap of about 3 GB** (at most $8.28 a day) with an alert at 80%. That disagrees with the tfvars comment ("an incident is when logs matter most"): owner decides |
| **Analytics beacon on SQL** | One beacon per pageview. 62 million views a month (section 9) is about 24 writes a second on average, more than S1 should take as row-per-view inserts | Count in Redis and flush aggregates each minute. Raise this when the analytics write path is planned, not after it |
| **SQL steps** | S1 $29 → S2 $74 → S3 $147 → S4 $294 | Step when sustained DTU p95 passes 70%. Serverless is not an escape: its 0.5-vCore minimum is $190 a month |
| **Redis tier jumps** | B0 HA $23 → B1 HA $47 → B3 HA $95 → B5 HA $228 | Redis is a cache in front of blob page data, so evictions cost latency, not correctness. Step on sustained memory above 70% or rising evictions |
| **Front Door custom-domain cap** | 100 custom domains and 10 endpoints per Standard profile, shared with `app.`, the wildcard, and CreatorPantry | D-13b: add a $35 profile before 80. Premium ($330, 500 domains, managed WAF and bot rules) becomes cheaper only at about 10 Standard profiles, or sooner if D-13's attack trigger fires |
| **Scrapers, hotlinking, bot floods** | Egress that no blogger's pageviews explain | WAF rate limits and scanner-path blocks (already planned). Block abusive referrers and ASNs case by case. **Never block Google Images or Pinterest**, which are food blogs' traffic |
| **Always-on compute** | The vCPU active share could double compute | Measure in month one. The Worker at 0.25 vCPU halves its share if it keeps up |
| **Budget alert too low** | Prod's budget is $350, which a spike or growth will trip | Set the budget to about 1.3 × the trailing three-month average spend. Add a cost anomaly alert |
| **Malware-scanning cost on imports** | $0.15 per GB scanned, once | Fine at these sizes. Watch it if imports of 50 GB or more become common |
| **Disputes** | $15 each, plus $15 to contest manually | Renewal reminders, a clear statement descriptor, and the 14-day annual refund |
| **Safe Browsing licensing** | Google's Safe Browsing APIs are for non-commercial use. Commercial use is the Web Risk API, which has its own pricing (**unverified**) | Check before PP19.9 and add it to this model |
| **Support load** | 1 to 8 minutes per account already breaks KPI-007 at about 300 accounts | Help-center-first product copy, in-app context with each ticket (OPS-010), and a hire or contract trigger (below) |

### 8.1 When to revisit prices

| Trigger | Do this |
| --- | --- |
| First full month with real traffic | Replace the assumptions in section 2.3 (MB per view, requests per view, vCPU active share) with measured values and rerun section 3 |
| 25 paying accounts (break-even) | Check fixed cost against the invoice |
| 100 paying accounts | First real plan mix. Rerun section 3.1. Revisit Starter's price and every allowance |
| Azure spend over 30% of MRR for 2 months | Page weight first, then allowances, then prices |
| Over 5% of accounts above their allowance for 2 months | Allowances are too low. Raise them before raising prices |
| Under 1% ever near their allowance after 6 months | Allowances could be cut, but don't. Generous allowances help retention |
| Custom domains on a profile reach 80 | D-13b: a new profile |
| Support over 4 hours a week (KPI-007) | Fix the product's top ticket causes. Then consider a support contractor |
| Trial conversion under 10% after 200 trials | Revisit trial length and content (section 7) |
| Every 12 months | Recheck competitor prices (section 13) and Azure list prices |

---

## 9. Capacity note

PNFR-004's first-year target is 2,000 sites and **5 million** pageviews a month, which averages 2,500 per site. The
personas (5,000 to 100,000 for a prosumer) and the typical usage in section 3 imply about **31,000** per account on
average (25% × 5,000 + 60% × 25,000 + 15% × 100,000 = 31,250). That would be about **62 million a month at 2,000
accounts**, 12 times the capacity target.

Revenue covers it, because contribution already pays for traffic. Egress also gets cheaper above 10 TB: 75 TB a month
would be billed mostly at $0.065 and $0.056 per GB. But SSR scale-out, the analytics write path, and the budget alert
should be sized for the larger figure. **Recommendation:** restate PNFR-004 after the first 100 paying accounts, from
measured pageviews.

---

## 10. What this means for the catalogue (proposal for PP14.1)

**This is a proposal.** PP14.1 plans it and waits for approval. Limits live in configuration, never in code
(`billing.md`, ADR 0009). The landing page, admin copy, and emails never type a price or a limit. They read the
catalogue, and features ask the entitlements facade (ARCH-010). No code checks a plan key.

**Where prices live.** PP14.1's restriction is "Prices live in Stripe; limits live in configuration." So the
configuration holds **Stripe price lookup keys**, not amounts. The catalogue reads the amounts from Stripe by lookup
key, caches them in the billing read model, and serves them with the limits. The annual saving is derived (monthly ×
12 − annual) and never stored, so it can't drift. A price change is a new Stripe Price with a new lookup key (`_v2`)
and a configuration change. Existing subscribers stay on their Price until moved on purpose.

```jsonc
{
  "Billing": {
    "Catalogue": {
      "Version": 1,
      "Currency": "usd",
      "RecommendedPlan": "plus"          // a recommendation, shown as "Recommended", never "most popular"
    },
    "Trial": {
      "Days": 14,
      "RequireCard": false,
      "FeaturesOf": "plus",
      "Limits": {                         // overrides on top of FeaturesOf
        "Sites": 1,
        "MembersPerSite": 1,
        "CustomDomains": 0,
        "CustomDomainsPerSite": 0,
        "Ads": false,
        "AdNetworks": false,
        "SubscribersPerSite": 100,
        "StorageGb": 1,
        "WordPressImports": 1,
        "MonthlyPageviews": 5000,
        "SearchIndexing": false            // noindex while trialling
      },
      "ExpiredContentRetentionDays": 60,
      "ReminderDaysBeforeEnd": [3, 0]
    },
    "Allowance": {
      "NoticeAtPercent": [80, 100],
      "SustainedPercent": 150,
      "SustainedMonths": 2,
      "SustainedWindowMonths": 3,
      "UpgradeNoticeDays": 30
    },
    "Plans": {
      "starter": {
        "Name": "Starter",
        "Order": 1,
        "StripePriceLookupKeys": { "Monthly": "pp_starter_monthly_v1", "Annual": "pp_starter_annual_v1" },
        "Limits": {
          "Sites": 1,
          "MembersPerSite": 1,
          "CustomDomains": 0,
          "CustomDomainsPerSite": 0,
          "BrandingRemoval": false,
          "DesignOptions": "starter",      // PP-D17 option set: 2 presets, mode, logo, tagline, colors
          "SubscribersPerSite": 500,
          "Ads": false,
          "AffiliateLinks": true,
          "AdNetworks": false,
          "AnalyticsRetentionDays": 30,
          "AnalyticsDashboard": "views",
          "AnalyticsExport": false,
          "StorageGb": 2,
          "WordPressImports": 0,
          "MonthlyPageviews": 10000,
          "Support": "help-center",
          "SearchIndexing": true
        }
      },
      "plus": {
        "Name": "Plus",
        "Order": 2,
        "StripePriceLookupKeys": { "Monthly": "pp_plus_monthly_v1", "Annual": "pp_plus_annual_v1" },
        "Limits": {
          "Sites": 1,
          "MembersPerSite": 3,
          "CustomDomains": 1,
          "CustomDomainsPerSite": 1,
          "BrandingRemoval": true,
          "DesignOptions": "all",
          "SubscribersPerSite": 5000,
          "Ads": true,
          "AffiliateLinks": true,
          "AdNetworks": false,
          "AnalyticsRetentionDays": 365,
          "AnalyticsDashboard": "full",
          "AnalyticsExport": false,
          "StorageGb": 20,
          "WordPressImports": 1,
          "MonthlyPageviews": 50000,
          "Support": "email",
          "SearchIndexing": true
        }
      },
      "pro": {
        "Name": "Pro",
        "Order": 3,
        "StripePriceLookupKeys": { "Monthly": "pp_pro_monthly_v1", "Annual": "pp_pro_annual_v1" },
        "Limits": {
          "Sites": 3,
          "MembersPerSite": 10,
          "CustomDomains": 3,
          "CustomDomainsPerSite": 1,
          "BrandingRemoval": true,
          "DesignOptions": "all",
          "SubscribersPerSite": 25000,
          "Ads": true,
          "AffiliateLinks": true,
          "AdNetworks": true,
          "AnalyticsRetentionDays": 730,
          "AnalyticsDashboard": "full",
          "AnalyticsExport": true,
          "StorageGb": 100,
          "WordPressImports": null,        // null = unlimited; a test pins that meaning
          "MonthlyPageviews": 200000,      // across all the account's sites
          "Support": "priority-email",
          "SearchIndexing": true
        }
      }
    }
  }
}
```

**Amounts to create in Stripe** (Recommendation): `pp_starter_monthly_v1` $9.00, `pp_starter_annual_v1` $90.00,
`pp_plus_monthly_v1` $19.00, `pp_plus_annual_v1` $190.00, `pp_pro_monthly_v1` $59.00, `pp_pro_annual_v1` $590.00.
Founding members, if adopted: `pp_plus_annual_founding_v1` $120.00.

**What the public catalogue answers** (the landing page reads this): each plan's `key`, `name`, `order`, `monthly`
and `annual` as money (`{ "amount": 19.00, "currency": "usd" }`, decimal with currency per `backend.md`),
`annualSaving` (derived), `limits` (the numbers above, with `null` meaning unlimited), `trialDays` (on the plans a
trial can convert to), and `recommended: true` on exactly one plan. Enums are kebab-case strings (`api-contract.md`).

**Notes for PP14.1:**

- **Units are stated in the names:** `StorageGb` is per account, `SubscribersPerSite` and `MembersPerSite` are per
  site, and `MonthlyPageviews` is per account. One ambiguous unit would be a bug in every entitlement check.
- **`DesignOptions` names an option set.** The option sets themselves (which presets and Studio options) belong to
  PP-D17's entitlement configuration (THM-012), not to this block.
- **Validate at startup:** exactly one recommended plan, which must exist; every plan has both lookup keys; no
  negative limit; `CustomDomains` ≥ `CustomDomainsPerSite`; trial `FeaturesOf` names a plan.
- **Account-level and operator overrides (BILL-008)** sit on top of these values in the entitlements read model. They
  never edit this configuration.

---

## 11. Where this differs from the 2026-10-24 draft

| Topic | Draft | This document | Why |
| --- | --- | --- | --- |
| Fixed cost | "$200 a month for the first 100 sites" | **≈ $248 prod, ≈ $308 with dev** | The Terraform's prod SKUs add up to more: always-on compute about $97, private endpoints $31, Redis HA $23, and Defender for Storage $10, which PP6.3 needs but the Terraform doesn't have yet. Break-even moves from about 22 to about 26 accounts |
| Stripe Tax | "Add 0.5% if you turn on Stripe Tax" | Always included | BILL-007 requires Stripe Tax. It is charged only where PantryPress is registered, so 0.5% on everything is the conservative figure |
| Pro price | $45 annual / $55 monthly | **$49.17 annual ($590) / $59 monthly** | At 200,000 pageviews with Stripe Tax and telemetry included, $45 contributes $5.11 (11%). $49.17 contributes $9.11 (19%) and is still about $30 under the self-hosted stack |
| Starter price | $6 annual / $9 monthly | **$7.50 annual ($90) / $9 monthly** | One annual-discount rule across plans (section 5.2). Starter isn't meant to win on price against WordPress.com's $4 |
| Plus price | $15 annual / $19 monthly | **$15.83 annual ($190) / $19 monthly** | Same rule. An 83-cent change |
| Annual discount | "Two months free", but 33% / 21% / 18% in its prices | **Annual = 10 × monthly on every plan** | One rule, easy to say, and derived in the catalogue |
| Pro overage | $15 per extra 100,000 pageviews | **No metered overage at launch.** Operator overrides, then a flat +100,000 add-on at $20 | 100,000 pageviews cost $13.26. At $15 the overage contributes $1.12 after Stripe. Metered billing means surprise invoices, disputes ($15 each), and Stripe usage reporting to build and support |
| How pageviews are counted | Not stated | The analytics beacon (ANL-001), the number the blogger sees | Understandable, already built, and excludes bots. Bytes would need Front Door access logs per host, which section 2.3 shows are expensive in Log Analytics |
| Founding members | Plus $10 a month annual, "locked for life" | Plus $120 a year, locked **while continuously subscribed** | "For life" is ambiguous after a lapse or transfer, and blocks repricing for ever |
| Affiliate links | Starter No (as 2.3) | **Yes on every plan** | Can't be enforced. The disclosure protects readers and PantryPress |
| Plus and Pro Studio split | Plus gets every Studio option | **Agree** | Same reasons. D-17 closes at PP11 |
| Trial | 14 days, no free tier | **Agree**, with caps (`noindex`, 1 GB, no custom domain, no ads) and no card | Section 7 |
| Edge cost per 100,000 | $12.60 (1.2 MB, 25 requests) | $13.26 at the same page | The draft counted the edge only. This adds origin compute, requests, and telemetry |
| Squarespace prices | Personal $16 / $23, Business $23 / $33 | Basic $19 / $25, Core $29 / $39, Plus $49 / $65, Advanced $99 / $139 | Squarespace changed its plan lineup in 2026 |
| Tasty Recipes | $129 to $259 a year | $99 a year; All Access $299 | Its current pricing page |
| KPI-006 | Not discussed | **Not reachable at 1.2 MB pages.** It levels off at about 25% | Section 3.1. Owner decision 9 |

---

## 12. Open decisions for the owner

| # | Decision | Recommendation |
| --- | --- | --- |
| 1 | Prices (D-09) | Starter $9 / $90, **Plus $19 / $190 (recommended)**, Pro $59 / $590 |
| 2 | Annual discount rule | Annual = 10 × monthly on every plan, shown annual-first |
| 3 | Trial or free tier (D-10) | 14-day trial with Plus features, no card, no free-forever tier |
| 4 | Trial caps | 1 site, owner only, 1 GB, 100 subscribers, no custom domain, no ads, `noindex`, one import within the cap |
| 5 | Pageview allowance | Soft: 10,000 / 50,000 / 200,000, counted by the analytics beacon. Notices at 80% and 100%; upgrade request only when sustained; never throttled or offline |
| 6 | Overage | None metered at launch. Operator overrides first, then a flat +100,000 for $20 add-on |
| 7 | Plus and Pro design split (D-17) | Plus gets every Studio option |
| 8 | Affiliate links on Starter | Yes, with the automatic disclosure |
| 9 | KPI-006 (infra under 15% of revenue) | Restate as **under 25%**, and add an image-bytes-per-page budget, which is the lever that actually moves it |
| 10 | Founding-member offer | Plus at $120 a year, first 100 accounts, annual only, locked while continuously subscribed |
| 11 | Where prices live | Stripe Prices by lookup key; configuration holds keys and limits only; the catalogue reads amounts from Stripe |
| 12 | Currency and tax footprint | USD only. Register in the home state; decide EU and UK (register or restrict) with an accountant before selling there |
| 13 | Refunds and cancellation | No partial-month refunds. Annual: full refund within 14 days of the first charge. Cancellation runs to period end, then "coming soon" with 90 days to export |
| 14 | Expired-trial retention | 60 days, with reminders, then erasure |
| 15 | Prod Log Analytics daily cap | About 3 GB a day with an 80% alert (the tfvars currently say no cap) |
| 16 | Capacity basis (PNFR-004) | Restate from measured pageviews after 100 paying accounts. The personas imply 12 times the current target |

---

## 13. Sources

All checked **2026-10-28**.

| Source | What it gave |
| --- | --- |
| [Azure Retail Prices API](https://prices.azure.com/api/retail/prices) (filtered by service and region) | Every Azure rate in section 2.1: Front Door Standard (base, Zone 1 to 5 egress tiers, requests, edge to origin), Container Apps (active and idle vCPU, memory, requests, and the 2026-09-01 environment-management meter), SQL S0 to S4 and serverless, Managed Redis B0 to B5, blob Hot and Cool, LRS and ZRS, Defender for Storage, Log Analytics (analytics, Basic, Auxiliary), Key Vault, DNS, Private Link, Communication Services Email, Container Registry |
| [Front Door pricing comparison (Microsoft Learn, updated 2025-09-25)](https://learn.microsoft.com/en-us/azure/frontdoor/understanding-pricing) | Standard $35 base; custom domains (first 100) and WAF custom rules free on Standard; origin-to-edge transfer free from Azure; managed WAF needs Premium ($330) |
| [Front Door limits (Microsoft Docs source)](https://raw.githubusercontent.com/MicrosoftDocs/azure-docs/main/includes/front-door-limits.md) | Standard: 100 custom domains and 10 endpoints per profile; Premium: 500 and 25; 500 profiles per subscription |
| [WAF pricing](https://azure.microsoft.com/en-us/pricing/details/web-application-firewall/) | Per-policy and per-rule WAF fees are listed for Front Door (classic) only; Premium's WAF is included. The page says nothing explicit about Standard, so the Learn table is relied on |
| [Container Apps pricing](https://azure.microsoft.com/en-us/pricing/details/container-apps/) | Free monthly grants (180,000 vCPU-s, 360,000 GiB-s, 2 million requests per subscription); when idle rates apply. The rate cells did not render, so the rates come from the Retail Prices API |
| [Communication Services pricing](https://azure.microsoft.com/en-us/pricing/details/communication-services/) | Email meters (the amounts did not render; the Retail Prices API gave $0.00025 per email and $0.00012 per MB) |
| [Stripe pricing](https://stripe.com/pricing) | 2.9% + $0.30; +1.5% international; +1% conversion; Billing 0.7%; disputes $15 + $15 |
| [Stripe Tax pricing](https://stripe.com/tax/pricing) | Tax Basic 0.5% per transaction on Billing and Checkout, only where registered; Tax Complete from $90 a month |
| [WordPress.com pricing](https://wordpress.com/pricing/) | Personal $4 / $9, Premium $8 / $18, Business $25 / $40, Commerce $45 / $70 (annual / monthly); free plan |
| [Squarespace pricing](https://www.squarespace.com/pricing) | Basic $19 / $25, Core $29 / $39, Plus $49 / $65, Advanced $99 / $139 |
| [CostBench Squarespace changelog](https://costbench.com/changelog/squarespace-plan-removed-2026-02/) | The Personal and Business plans were replaced in February 2026 (third-party) |
| [Wix plans (eseospace, October 2026)](https://eseospace.com/blog/wix-plans-compared/) and [CostBench Wix](https://costbench.com/software/cms/wix/) | Light $17, Core $29, Business $39, Business Elite $159, billed annually. Monthly prices ($24 / $36 / $46 / $172) are third-party: **unverified** |
| [Ghost pricing](https://ghost.org/pricing/) | Starter $18, Publisher $29, Business $199 a month billed yearly, at 1,000 members. The price at 5,000 members did not render: the draft's "about $75" is **unverified** |
| [Substack pricing (SchoolMaker, 2026)](https://schoolmaker.com/blog/substack-pricing) | Free to publish; 10% of paid-subscription revenue plus Stripe fees. The one-time custom domain fee (about $50) is third-party: **unverified** |
| [Feast shop](https://feastdesignco.com/shop/) | Feast Plugin Starter $97 a year, Feast Plugin $249 a year, Feast+ $999 a year. Hosting is not mentioned |
| [WP Recipe Maker](https://bootstrapped.ventures/wp-recipe-maker/get-the-plugin/) | Premium $49, Pro $99, Elite $149 a year (one site) |
| [WP Tasty pricing](https://www.wptasty.com/pricing) | Tasty Recipes $99 a year; All Access $299 a year; introductory prices renew at full price (renewal amount not shown) |
| [BigScoots pricing](https://www.bigscoots.com/wordpress-pricing/) | Essential 75 $35 a month or $350 a year (75,000 visitors, 10 GB); Essential 125 $60 or $600; Essential 200 $85 or $850; Core 500 $200 or $2,000 |
| [WP Engine plans](https://wpengine.com/plans/) | Essential $28 a month billed annually (shown as a discount); 25,000 to 400,000 visits; overage rates not listed |
| [Infra hosting plan](../../infrastructure/README.md) and `docs/infrastructure/terraform/*/env/prod.tfvars` | The planned SKUs: SQL S1, Managed Redis B0 with HA, 5 apps at 0.5 vCPU / 1 GiB with minimum 1 replica, ZRS storage, 4 private endpoints, no Log Analytics cap, $350 budget |
| [The 2026-10-24 draft](../../pricing/pantrypress-pricing-recommendation.md) | Starting prices, support-time assumptions, the assumed plan mix, and the competitor list this document updates |

**Not verified:** Kinsta's prices (the draft's $35 to $115, not rechecked), Wix monthly-billing prices, Ghost at 5,000
members, Substack's custom-domain fee, Stripe's refund-fee terms for 2026, Google Web Risk pricing, and EU and UK VAT
thresholds for non-established sellers (an accountant's question).
