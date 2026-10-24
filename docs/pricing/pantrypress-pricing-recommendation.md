# PantryPress pricing recommendation (draft for PP-D09, 2026-10-24)

## Recommended prices

| | Starter | **Plus** | **Pro** |
| --- | --- | --- | --- |
| Billed annually | $6/mo ($72/yr) | **$15/mo ($180/yr)** | **$45/mo ($540/yr)** |
| Billed monthly | $9/mo | **$19/mo** | **$55/mo** |
| Monthly pageviews included | 10k | 50k | 200k across all sites |
| Overage | Upgrade prompt | Upgrade prompt | $15 per extra 100k pageviews |

Everything else stays as in requirements section 2.3. The one new limit is the pageview allowance. It's there because bandwidth is the only cost that grows with a blogger's success.

## Reading of the hosting estimate

"$200/mo for the first 100 sites, plus $35 per extra Front Door Standard profile." Front Door Standard costs $35/mo per profile. A profile holds 100 custom domains (Premium holds 500). The `*.pantrypress.app` wildcard counts as **one** domain, so the extra $35 is owed for every 100 **custom domains**, not every 100 sites. That works out to about $0.35 per custom-domain site per month, which barely matters.

The $200 does not include anything that grows with traffic. Front Door bills about $0.083/GB from edge to reader and about $0.009 per 10k requests (North America, first 10 TB). Food blogs are image-heavy. At roughly 1.2 MB and 25 requests per pageview, the edge cost comes to about **$12.60 per 100k pageviews**.

Not in the $200 either: Log Analytics ingestion (cap it, or it will surprise you), SQL/Redis tier bumps as sites grow, and blob storage (cheap, about $0.02–0.03/GB).

## Per-subscriber unit economics (modelled)

Assumptions: Stripe 2.9% + $0.30, plus 0.7% Billing, per charge (add 0.5% if you turn on Stripe Tax). Support time is valued at $60/hr: Starter 1 min, Plus 3 min, Pro 8 min per account per month at steady state. Typical traffic is 5k / 25k / 150k pageviews.

| Plan | Price | Stripe | Bandwidth + infra share | Support | Contribution |
| --- | --- | --- | --- | --- | --- |
| Starter annual | $6.00 | $0.24 | $1.13 | $1.00 | $3.63 (61%) |
| Plus annual | $15.00 | $0.56 | $3.67 | $3.00 | $7.76 (52%) |
| Plus monthly | $19.00 | $0.98 | $3.67 | $3.00 | $11.34 (60%) |
| Pro annual | $45.00 | $1.65 | $19.36 | $8.00 | $16.00 (36%) |
| Pro monthly | $55.00 | $2.28 | $19.36 | $8.00 | $25.40 (46%) |

At $39 a Pro blog with 150k pageviews nets about $10 (26%), and one at 300k loses money. That's why Pro is priced at $45 and has an allowance.

## Break-even and scale

The mix assumed is 25% Starter, 60% Plus and 15% Pro, half annual. That gives about **$9–10 contribution per site per month**.

| Paying sites | MRR | Fixed infra | Profit/mo |
| --- | --- | --- | --- |
| 25 | ~$470 | $200 | ~$30 |
| 100 | ~$1,900 | $200 | ~$720 |
| 250 | ~$4,700 | ~$325 | ~$2,000 |
| 500 | ~$9,300 | ~$510 | ~$4,100 |
| 1,000 | ~$18,700 | ~$915 | ~$8,300 |

- **Hosting break-even: about 22 paying sites.**
- **$5k/mo profit: about 600 sites. $10k/mo: about 1,200 sites.**
- Support is the real limit. At 1,000 sites the assumptions above add up to about 55–60 hours a month.

## Competitors (checked 2026-10-24)

| Product | Price |
| --- | --- |
| WordPress.com | Personal $4 annual / $9 monthly; Premium $8 / $18; Business $25 / $40 (plugins on all paid plans since Apr 2026) |
| Squarespace | Personal $16 / $23; Business $23 / $33 |
| Ghost(Pro) | Starter $18 (1k members); Publisher from $29, about $75 at 5k members |
| Feast (WordPress plugin + themes) | Starter $97/yr; Plugin $249/yr; Feast+ $999/yr. Hosting not included |
| Recipe plugins | WP Recipe Maker $49–199/yr; Tasty Recipes $129–259/yr |
| Managed WP hosting | WP Engine from about $30/mo; Kinsta $35–115/mo |

A serious self-hosted food blogger pays for hosting, Feast and a recipe plugin, which comes to about $60–110/mo. Pro at $45 undercuts that and takes away plugin upkeep. Plus at $15 sits below Squarespace Business and WP.com Business, and it beats both on recipe features.

## Plus vs Pro split of Studio options (PP-D17)

Recommendation: **give Plus every Studio option.** Make Pro about money and scale instead: three sites, the ad-network ads.txt and sidebar slot, 200k pageviews, 100 GB, unlimited WordPress imports, 24-month analytics with export, and priority support.

Locking textures and the Banner and Masthead headers is a weak reason to pay three times more, and it adds "why is this greyed out?" tickets. It also enlarges the downgrade test matrix (CUS-T11). Keep the design gate between Starter (presets only) and Plus.

## Other recommendations

- Push annual billing: two months free, and show the annual price first. It cuts Stripe fees by about 60% and reduces churn.
- Keep PP-D10's default of a 14-day trial with no free-forever tier.
- Turn on AVIF/WebP and long cache lifetimes from day one. Page weight drives the largest variable cost.
- Plan pricing for early bloggers: founding members get Plus at $10/mo annual, locked for life, for the first 100 sites.

Sources: [WordPress.com](https://eseospace.com/blog/wordpress-com-plans/), [Squarespace](https://www.shopping-cart-migration.com/must-know-tips/squarespace-pricing-is-squarespace-expensive), [Ghost](https://typeflo.io/blog/ghost-pricing-plans), [Feast](https://feastdesignco.com/shop), [food blog costs](https://projectcostestimator.com/blog/food-blog-website-cost-2026), [Front Door pricing](https://learn.microsoft.com/en-us/azure/frontdoor/understanding-pricing).
