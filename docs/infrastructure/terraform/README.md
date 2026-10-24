# Terraform runbook

How to stand up and run PantryPress in Azure from a workstation. The reasoning behind every choice is in
[../README.md](../README.md). Nothing here needs a pipeline; everything is `az login` plus `run.ps1`.

```text
terraform/
├── backend.hcl.example   # copy to backend.hcl after bootstrap (git-ignored)
├── run.ps1               # one stack, one environment, one action
├── bootstrap/            # once per subscription: state account and container registry (local state)
├── platform/             # per environment, shared with CreatorPantry: monitoring, VNet, Container Apps env, Front Door
│   └── env/{dev,prod}.tfvars
└── pantrypress/          # per environment, PantryPress only: data, identities, apps, edge routes, DNS, budget
    ├── env/{dev,prod}.tfvars
    └── sql/grant-access.sql.tftpl
```

State lives in one storage account under `platform-<env>.tfstate` and `pantrypress-<env>.tfstate`. Only `bootstrap`
keeps local state.

## Prerequisites

- Terraform 1.11 or later (validated with 1.17.0), Azure CLI, PowerShell 5.1 or 7.
- `sqlcmd` (the Go version: `winget install sqlcmd`) for the one-time database grants.
- The `communication` CLI extension for email verification: `az extension add --name communication`.
- On the subscription: Owner, or Contributor plus User Access Administrator. The stacks create role assignments.
- Docker running, if you push images with `dotnet publish -t:PublishContainer`.

```powershell
az login
az account set --subscription <subscription-id>
```

Resource providers register themselves on first use. If an apply stops with "MissingSubscriptionRegistration", run
`az provider register --namespace <name>` for `Microsoft.App`, `Microsoft.Cdn`, `Microsoft.Cache`,
`Microsoft.Communication`, `Microsoft.OperationalInsights` and try again.

## 1. Bootstrap (once)

```powershell
cd docs/infrastructure/terraform/bootstrap
Copy-Item terraform.tfvars.example terraform.tfvars   # fill in subscription and two globally unique names
terraform init
terraform plan -out bootstrap.tfplan
terraform apply bootstrap.tfplan
terraform output -raw backend_hcl | Set-Content ../backend.hcl
```

Keep `bootstrap/terraform.tfstate` somewhere safe. It holds no secrets, but without it this stack has to be
re-imported (`terraform import`) before it can change.

## 2. An environment (dev first)

Fill in `platform/env/dev.tfvars` and `pantrypress/env/dev.tfvars`. Set at least the subscription, the registry name
from bootstrap, `dns_zone_name`, `sql.entra_admin_login` (your UPN), and a budget email. The Container Apps subnet in
both files must match.

```powershell
cd docs/infrastructure/terraform
./run.ps1 platform dev plan
./run.ps1 platform dev apply          # ~5 minutes

./run.ps1 pantrypress dev plan
./run.ps1 pantrypress dev apply       # ~20-30 minutes; Redis and Front Door domains are the slow part
```

`run.ps1` refuses to run if the signed-in subscription is not the one the variables name. It saves every plan and
applies exactly the plan you reviewed.

### DNS delegation

```powershell
./run.ps1 pantrypress dev output name_servers
```

For a child zone (`dev.pantrypress.app`), add an `NS` record named `dev` with these four servers wherever
`pantrypress.app` is hosted. For a zone of its own (prod's `pantrypress.app`), set them at the registrar. Front Door's
domain validation and the managed certificates wait for this, and can take up to an hour after it resolves:

```powershell
az afd custom-domain list -g rg-platform-dev --profile-name afd-platform-dev `
  --query "[].{host:hostName, validation:domainValidationState, tls:tlsSettings.certificateType}" -o table
```

### Database access (once per environment)

SQL accepts Entra ID only. Create a database user for each app's managed identity:

```powershell
./run.ps1 pantrypress dev output -raw sql_grant_script | Set-Content grant-dev.sql
$server = (./run.ps1 pantrypress dev output -raw sql_server)
sqlcmd -S $server -d pantrypress --authentication-method ActiveDirectoryDefault -i grant-dev.sql
```

Prod's SQL firewall admits only `operator_ip_cidrs`, so run this from one of those addresses. The script is safe to
run again.

### Platform administrators

The migration job reads the PlatformAdmin list from Key Vault (ACCT-005). The value is passed in at apply time and
never written to state:

```powershell
$env:TF_VAR_platform_admins = "you@example.com"
# bump platform_admins_version in pantrypress/env/dev.tfvars (for example 1 -> 2), then:
./run.ps1 pantrypress dev plan
./run.ps1 pantrypress dev apply
Remove-Item Env:TF_VAR_platform_admins
```

Until you do, the secret holds `;`, an empty list. The reconciler reads that as "no opinion", so it never removes
anyone.

### Email domain (once per environment)

Terraform writes the verification records into the zone. Ask Azure to check them once DNS has delegated:

```powershell
$rg = "rg-pantrypress-dev"; $svc = "acs-email-pantrypress-dev"
$domain = (./run.ps1 pantrypress dev output -raw email_domain)
foreach ($type in "Domain", "SPF", "DKIM", "DKIM2") {
  az communication email domain initiate-verification -g $rg --email-service-name $svc --domain-name $domain --verification-type $type
}
```

If one fails, compare `./run.ps1 pantrypress dev output email_verification_records` with the records in the zone.

## 3. Deploying a release

Images are part of the configuration: `images` in `pantrypress/env/<env>.tfvars` names every app's image, and an apply
rolls the apps to it. Migrations run first, as their own step (OPS-003).

1. **Build and push** with one tag for the whole release. While `aspire run` is up, build outputs are locked, so
   publish to a separate artifacts path:

   ```powershell
   $acr = (./run.ps1 pantrypress dev output -raw container_registry)
   $tag = "2026.10.24.1"
   az acr login --name $acr.Split('.')[0]
   $apps = @{ gateway = "Gateway"; apiservice = "ApiService"; site = "Site"; worker = "Worker"; migrations = "MigrationService" }
   foreach ($name in $apps.Keys) {
     dotnet publish "../../../src/PantryPress.$($apps[$name])" -c Release --os linux --arch x64 `
       --artifacts-path ../../../artifacts-publish -t:PublishContainer -p:EnableSdkContainerSupport=true `
       -p:ContainerRegistry=$acr -p:ContainerRepository="pantrypress/$name" -p:ContainerImageTags=$tag
   }
   ```

   The SSR server (`pantrypress-site`) needs a Node Dockerfile first (PP19.2). Until then, `site-ssr` keeps its
   placeholder.

2. **Migrate.** Set only `images.migrations` to the new tag, then plan, apply, and start the job:

   ```powershell
   ./run.ps1 pantrypress dev plan
   ./run.ps1 pantrypress dev apply
   az containerapp job start -n pp-migrations -g rg-pantrypress-dev
   az containerapp job execution list -n pp-migrations -g rg-pantrypress-dev --query "[0].properties.status" -o tsv
   ```

   Wait for `Succeeded`. A failed run changes nothing else.

3. **Roll the apps.** Set the remaining images to the new tag, then plan and apply. Each app gets a new revision, and
   Container Apps switches traffic once the new revision is ready.

**Rollback:** set the previous tag and apply. Migrations only go forward, so roll back only to a release that works
with the current schema.

The `images` map replaces the default as a whole: list all six apps in the tfvars once you set it.

## 4. Day two

| Task | How |
| --- | --- |
| Pick up Front Door address changes | Run a plan monthly. A diff on `ip_security_restriction` or `Edge__TrustedProxies__KnownNetworks__*` means the service tag moved; apply it. |
| Renew the wildcard certificate | `./run.ps1 pantrypress <env> output certificate_expiry`. Ahead of that date, run a plan and apply, which refreshes the `_dnsauth` record. Then check validation with the `az afd custom-domain list` command above. |
| Rotate the gateway secret | Bump `gateway_secret_version`, then plan and apply. Restart both apps so they read it together: `az containerapp revision restart` for `pp-gateway` and `pp-apiservice`. Sessions survive, because the secret is between the services, not in cookies. |
| Pause dev | `./run.ps1 pantrypress dev destroy`, then `./run.ps1 platform dev destroy`. The Key Vault is purged on destroy, and DNS delegation must be redone if the zone's name servers change. |
| See what it costs | Cost Management, filtered by tag `product = pantrypress`, or the budget alerts (OPS-009). |
| Change prod's shape | Only through `env/prod.tfvars`, plan, and apply. `run.ps1` will not destroy prod. Removing a delete lock is deliberate work in the portal, never a variable. |

## 5. When an apply fails

- **"Forbidden" on a Key Vault secret, or a Container App that cannot read one:** a role assignment had not propagated
  yet. Wait a minute and apply again; nothing is left half-created.
- **Key Vault, storage, or SQL refuses the operator in prod:** your address is not in `operator_ip_cidrs`.
- **Front Door domain stuck in "Pending":** DNS is not delegated yet, or the `_dnsauth` record has not propagated.
  Check it with `Resolve-DnsName _dnsauth.<zone> -Type TXT`.
- **State locked:** a previous run was interrupted. Check that no other run is active, then run
  `terraform -chdir=<stack> force-unlock <lock-id>`.
- **The platform stack rejects `environment_telemetry`:** the managed OpenTelemetry agent's API version or shape has
  changed. The resource is isolated, so fix it or comment it out and apply the rest.
