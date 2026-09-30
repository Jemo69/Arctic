# The first production deploy

**Not yet possible.** Checked against the live account on **2026-09-30**: the
pipeline is fine and staging proves it, but production is missing configuration
that only exists on staging. This page is the list, in the order the deploy
hits it.

Nothing here is a code change. It is five settings, one Azure role grant, and
two Vercel projects.

Budget half a day, most of it waiting on deploys. **Expect the first run to
fail** — step 3 is a known two-run sequence, not a mistake.

---

## What already works

Worth saying first, because the list below is long and none of it is broken
machinery.

- **OIDC is wired for production.** `id-mh-deploy` carries
  `github-production → repo:MorganHacks/Arctic:environment:Production`, plus the
  ID-pinned variant. Authentication will not be the problem.
- **Repository-level variables cover both environments**: `SUPER_ADMIN_EMAIL`,
  `GOOGLE_CLIENT_ID`, `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`,
  `AZURE_SUBSCRIPTION_ID`.
- **Production environment variables already set**: `CONSOLE_BASE_URL`,
  `FORMS_BASE_URL`, `GOOGLE_REDIRECT_URI`, `AWS_REGION`.
- **The Production GitHub environment requires a review** and restricts the
  branch, so nothing reaches it by accident.
- **The two-pass deploy refuses to put new code in front of an old schema.**
  `deploy.sh` runs platform → migrations → apps and exits 1 if migrations fail.

---

## 1. A production database password

**Fails in about five seconds without this.** `deploy/azure/deploy.sh:24`:

```bash
: "${DB_PASSWORD:?set DB_PASSWORD}"
```

The Production environment holds only `PROXY_SHARED_SECRET`. Past that guard it
would fail again at compile: `prod.bicepparam` reads `DB_PASSWORD` with **no
default**, and a missing one is a template error rather than an empty string.

That absence is deliberate — production must not inherit staging's password —
so this is a decision to make, not an oversight to correct.

```bash
# Generate it somewhere it will not end up in shell history or a log.
gh secret set DB_PASSWORD --env Production -R MorganHacks/Arctic
```

**Keep it somewhere real.** Bicep never reads it back, so losing it means
resetting the server admin password rather than looking it up.

## 2. The four settings that fail quietly

None of these stop a deploy. Every one defaults to an empty string, so the
deploy goes green and the thing they control is simply off.

| Setting | Kind | Without it |
|---|---|---|
| `PUBLIC_BASE_URL` | variable | **Emailed sign-in links are built from this.** Empty means every magic link in production points nowhere. |
| `GOOGLE_CLIENT_SECRET` | secret | Google sign-in is off. |
| `AWS_ACCESS_KEY_ID` | secret | `UnconfiguredEmailProvider` is registered instead of SES, so mail queues at `pending` and never sends. |
| `AWS_SECRET_ACCESS_KEY` | secret | As above. Both are needed or neither counts. |

```bash
gh variable set PUBLIC_BASE_URL --env Production -R MorganHacks/Arctic \
  --body "https://www.morganhacks.com"

gh secret set GOOGLE_CLIENT_SECRET   --env Production -R MorganHacks/Arctic
gh secret set AWS_ACCESS_KEY_ID      --env Production -R MorganHacks/Arctic
gh secret set AWS_SECRET_ACCESS_KEY  --env Production -R MorganHacks/Arctic
```

`ENABLE_HACKER_PORTAL_FEATURE` is also unset on production. Leaving it unset
lets `features.json` decide, which keeps the portal off — set it to `true` only
when the portal is meant to be live.

## 3. The role grant, which needs two runs

**This is the one that fails the first time, and it is supposed to.**

`platform.bicep` assigns Storage Blob Data Contributor on the resumes account,
and **Contributor cannot create role assignments**. The deploy identity holds
User Access Administrator on `rg-mh-shared` and `rg-mh-staging` — and nothing on
`rg-mh-prod`, which does not exist yet. The grant cannot be made before the
group exists, and the group is created by the deploy.

So: run it, watch it fail, grant, run it again.

This is the same failure staging hit on 2026-09-02, recorded in
[the backlog](../backlog.md):

```
Authorization failed for template resource ... of type
'Microsoft.Authorization/roleAssignments'. The client ... does not have
permission to perform action 'Microsoft.Authorization/roleAssignments/write'
```

**First run** — Actions → *Deploy to Azure* → environment `production`. It
creates `rg-mh-prod` and fails on the assignment.

**Then grant:**

```bash
SP=$(az identity show -n id-mh-deploy -g rg-mh-shared --query principalId -o tsv)
SUB=$(az account show --query id -o tsv)

az role assignment create \
  --assignee-object-id "$SP" --assignee-principal-type ServicePrincipal \
  --role "User Access Administrator" \
  --scope "/subscriptions/$SUB/resourceGroups/rg-mh-prod"
```

Narrowed to the one group on purpose. The ability to hand out access is the
permission worth being stingy with.

**Second run** — same dispatch. The templates are idempotent and the grant is
in place by then.

If the apps fail to pull images on this run, re-run once more: the AcrPull
assignment occasionally has not propagated by the time containers start.

## 4. The frontends, which are a separate problem

The backend being up does not make the site work. These are Vercel settings,
unrelated to everything above.

**`API_ORIGIN` is unset on production** for portaladmin and portalweb. Both fall
back to `http://localhost:5050`, and Vercel refuses to proxy to a private
address — so every call answers 404 `DNS_HOSTNAME_RESOLVED_PRIVATE`. Confirmed
live: `admin.morganhacks.com/api/health` 404s today while
`admin-stg.morganhacks.com/api/health` returns `{"status":"ok"}`.

```bash
vercel env add API_ORIGIN production   # for morganhacks-portaladmin
vercel env add API_ORIGIN production   # and again for morganhacks-portalweb
```

**portalforms has never built.** Its Ignored Build Step on the Vercel dashboard
cancels every deployment, production included, in about two seconds. Both
`forms` domains answer `DEPLOYMENT_NOT_FOUND`. The repository already carries
the correct `ignoreCommand` in `src/portalforms/vercel.json`; the dashboard
setting is what overrides it. See [the backlog](../backlog.md) for the full
explanation of the inverted exit code.

## 5. Before announcing anything

```bash
curl -s -o /dev/null -w '%{http_code}\n' https://admin.morganhacks.com/api/health
```

**200 is the gate.** Anything else means the frontends still cannot reach the
backend, whatever the Azure portal says.

Then, in order:

- [ ] Sign in to the console with Google. Proves `GOOGLE_CLIENT_SECRET` and the
      redirect URI.
- [ ] Request a magic link and follow it. Proves `PUBLIC_BASE_URL` and that SES
      is actually sending rather than queueing.
- [ ] Open a form's public URL and submit it. Proves portalforms deploys and
      reaches the API.
- [ ] Confirm the super admin was seeded, and that there are **two** of them.
      The migration runner warns when there is only one, because one graduation
      should not lock the organisation out.

---

## Two things that are not configuration

**Check what this subscription is licensed for.** A Visual Studio
subscription's monthly credit is for development and testing only under its
terms — fine for staging, not a licence to run registration on. Being cut off
during registration week is the worst version of that mistake.
`deploy/azure/README.md` lists the alternatives worth trying first.

**Two gaps follow production wherever it goes**, both in
[the backlog](../backlog.md) and neither fixed by this page:

- Postgres accepts connections from any Azure tenant (`0.0.0.0-0.0.0.0`
  firewall rule), with only the password in the way. The fix is VNet
  integration, which means building a new Container Apps environment — so it is
  much cheaper to do *before* production exists than after.
- Every per-IP rate limit is bypassable with a forged `X-Forwarded-For`, because
  `Network__KnownProxies` and `Network__KnownNetworks` are set in neither
  environment.

The first one is the reason to think about this now rather than later: once
production holds real applicant data, rebuilding its environment stops being
free.

---

**Escalate to:** the tech lead. For anything touching the production database
password or a role assignment, get a second person on the call first — a failed
deploy is recoverable, and a credential nobody can find is not.
