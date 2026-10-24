<#
.SYNOPSIS
    Runs one Terraform stack against one environment from a workstation. No pipeline needed.

.DESCRIPTION
    Picks the stack folder, initializes it against the shared state account with a per-stack, per-environment state
    key, and runs the action with that environment's variables. Plans are saved and applied exactly as reviewed.

.EXAMPLE
    ./run.ps1 platform dev plan
    ./run.ps1 platform dev apply
    ./run.ps1 pantrypress prod plan
    ./run.ps1 pantrypress dev output sql_grant_script
#>
param(
    [Parameter(Mandatory)][ValidateSet('platform', 'pantrypress')][string] $Stack,
    [Parameter(Mandatory)][ValidateSet('dev', 'prod')][string] $Environment,
    [Parameter(Mandatory)][ValidateSet('init', 'plan', 'apply', 'output', 'destroy', 'validate')][string] $Action,
    [Parameter(ValueFromRemainingArguments)][string[]] $Rest
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$stackDir = Join-Path $root $Stack
$backend = Join-Path $root 'backend.hcl'
$varFile = Join-Path $stackDir "env/$Environment.tfvars"
$planFile = Join-Path $stackDir "$Environment.tfplan"

if (-not (Test-Path $backend)) {
    throw "backend.hcl is missing. Copy backend.hcl.example and fill it in from the bootstrap stack's output."
}

# Fail before touching anything if the signed-in subscription is not the one the variables name.
$expected = (Select-String -Path $varFile -Pattern '^\s*subscription_id\s*=\s*"([^"]+)"').Matches[0].Groups[1].Value
$actual = (az account show --query id -o tsv)
if ($LASTEXITCODE -ne 0) { throw 'Not signed in. Run az login first.' }
if ($actual -ne $expected) {
    throw "Signed in to subscription $actual, but env/$Environment.tfvars names $expected. Run az account set --subscription $expected."
}

function Invoke-Terraform {
    & terraform "-chdir=$stackDir" @args
    if ($LASTEXITCODE -ne 0) { throw "terraform $($args[0]) failed with exit code $LASTEXITCODE." }
}

# -reconfigure because one folder serves two environments: each run must point at its own state. Quiet unless it fails,
# so `output -raw` can be captured cleanly.
$initLog = & terraform "-chdir=$stackDir" init -input=false -reconfigure "-backend-config=$backend" "-backend-config=key=$Stack-$Environment.tfstate" 2>&1
if ($LASTEXITCODE -ne 0) {
    $initLog | Write-Host
    throw "terraform init failed with exit code $LASTEXITCODE."
}

switch ($Action) {
    'init' { }
    'validate' { Invoke-Terraform validate }
    'plan' { Invoke-Terraform plan -input=false "-var-file=$varFile" "-out=$planFile" @Rest }
    'apply' {
        if (-not (Test-Path $planFile)) { throw "No saved plan. Run: ./run.ps1 $Stack $Environment plan" }
        Invoke-Terraform apply -input=false $planFile
        Remove-Item $planFile
    }
    'output' { Invoke-Terraform output @Rest }
    'destroy' {
        if ($Environment -eq 'prod') { throw 'Refusing to destroy prod from this script. Do it by hand, deliberately.' }
        Invoke-Terraform destroy "-var-file=$varFile" @Rest
    }
}
