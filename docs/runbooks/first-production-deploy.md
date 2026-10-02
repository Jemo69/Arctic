# The first production deploy

**This used to be a forward-looking checklist** — settings to fill in before
production could exist at all, written on 2026-09-30. Production exists now:
it deployed on **2026-10-02**, on the fourth attempt, and none of the three
failures before it were the application. They were the deploy identity's
permissions, Azure capacity, and one Bicep parameter answering two different
questions with the same value.

Retelling that as a story would not help whoever reads this next, because two
of the three failures are already fixed in `main.bicep` and cannot happen to a
new environment the way they happened to this one. So this page is rewritten
as **the checklist for standing up the next environment**, with each failure
folded in at the point it would still bite — which, for two of them, is not at
all, and the page says so.

If you want the failures in the order production actually hit them rather than
the order a new deploy would, they are numbered below in that order too.

---

## Before you start

- **Pick a region, and check it before you commit to it** — see problem 2.
  `location` in a `.bicepparam` file is cheap to write and expensive to
  discover is wrong three deploys later.
- **Decide whether this environment shares the registry.** The default is
  yes: `registryName` defaults to the shared `crmharctic` in `rg-mh-shared`,
  `centralus`, regardless of where this environment's own resources live —
  see `sharedLocation` in `main.bicep`. It needs its own registry only if it
  cannot share that one at all, because a managed identity cannot be granted
  `AcrPull` across a tenant or subscription boundary.
- **Generate a `DB_PASSWORD` this environment does not share with any
  other.** `prod.bicepparam` reads it with no default on purpose, for exactly
  this reason: an environment must not inherit another one's database
  credential just because a secret with that name already exists somewhere.
- **Give the new GitHub environment a federated credential on
  `id-mh-deploy`**, and, if it should require a reviewer the way Production
  does, set that on the GitHub environment itself — not in any file here.

## 1. The deploy identity could not grant roles in a resource group that did not exist yet

`platform.bicep` assigns Storage Blob Data Contributor on the resumes storage
account, and the identity running the deploy — `id-mh-deploy` — needs **User
Access Administrator** to create that assignment. It held that role on
`rg-mh-staging` and `rg-mh-shared`, granted by hand when each was created, and
nothing on `rg-mh-prod`, because the group did not exist until this deploy
created it.

Stage 3 (Platform) failed with:

```
Authorization failed for template resource ... of type
'Microsoft.Authorization/roleAssignments'. The client ... does not have
permission to perform action 'Microsoft.Authorization/roleAssignments/write'
```

**`what-if` did not warn about this.** It does not evaluate role-assignment
authorization, so the plan step was clean and the apply failed anyway — the
same shape of surprise staging hit on 2026-09-02, recorded in
[the backlog](../backlog.md).

**Fixed for every environment after this one.** The grant is now at
**subscription scope** rather than per-resource-group, specifically so a new
environment's first deploy does not start by rediscovering this. Confirm it is
still there before trusting that:

```bash
az role assignment list --assignee id-mh-deploy \
  --query "[].{role:roleDefinitionName, scope:scope}" -o table
```

If `User Access Administrator` is not scoped to the subscription itself, grant
it there — not to the new environment's resource group. The per-group version
is exactly what problem 4 is about.

## 2. Central US would not create a new environment at all

With `location` still at its `centralus` default, the next run passed stage 3
and failed trying to create `cae-mh-prod`:

```
AKSCapacityHeavyUsage
```

Container Apps runs on AKS underneath, and Central US had no capacity left on
this subscription for a new environment. **This is not a configuration
mistake**, and `what-if` cannot see it either — capacity is only evaluated at
apply time, not at plan time. There is no setting that fixes it. The only move
is a different region, which is the same lesson staging's Postgres had already
taught in a smaller way: `eastus` refuses Postgres provisioning outright on
this subscription (see `deploy/azure/README.md`). Have a fallback region in
mind before you run anything, rather than discovering you need one mid-deploy.

## 3. One `location` parameter cannot answer two questions

Production moved to `eastus2` — the same metro as `iad1`, where Vercel serves
this project from and relays every API call through; `prod.bicepparam` has the
full reasoning. The very next run failed at **stage 1**, before anything
environment-specific had even been looked at:

```
InvalidResourceLocation
```

At the time, `location` was a single parameter handed to every module,
including the shared registry. The registry already existed in `centralus`
and a resource cannot change region, so pointing `location` at `eastus2` tried
to move it and failed immediately — at the cheapest possible stage to fail at,
which is the one consolation here.

**Already fixed.** `main.bicep` now takes a separate `sharedLocation`
parameter (default `centralus`) for the registry and anything else shared
across environments, independent of each environment's own `location`. A new
environment in a new region does not need to touch this at all — only adding a
new *shared* resource would.

## 4. The role grant from problem 1 did not survive the resource group it was on

The grant from problem 1 was first made directly on `rg-mh-prod`, scoped the
same way the old instructions for this page said to, before the subscription-
scope fix existed. Then problem 2 happened: Central US refused the
environment, so `rg-mh-prod` was deleted and recreated in `eastus2` — a
resource group's location is as immovable as the registry's — and the grant
made on the old group went with it. The new group started with nothing, and
the deploy failed on the exact same `roleAssignments/write` error as problem
1, for what looked like no reason the second time.

This is the actual reason problem 1's fix is at subscription scope rather than
per-group: a grant scoped to a resource group is only as durable as that
resource group is. If this environment's own resource group is ever deleted
and recreated — a region move, or anything else — re-check the subscription
grant is still there rather than assuming the next deploy will just work
because the last one did.

## 5. Run it

```bash
export DB_PASSWORD=...            # generated fresh, never copied from another environment
export SUPER_ADMIN_EMAIL=olola73@morgan.edu

./deploy/azure/deploy.sh <env>                 # what-if first — changes nothing
./deploy/azure/deploy.sh <env> --apply
```

Or through CI: Actions → **Deploy to Azure** → `workflow_dispatch`, with
`environment` pointed at whichever GitHub environment this one is wired to. A
push to `main` only ever reaches staging — a new environment needs its own
entry in that choice before anything will deploy to it at all.

**Budget for more than one run even with every lesson above already fixed.**
The role-assignment propagation delay documented in `deploy/azure/README.md`
("Pulling images") is still real and unrelated to any of the above: if the
apps fail to pull their first image, re-run — the grant is in place by then.

## 6. Before calling it done

```bash
curl -s -o /dev/null -w '%{http_code}\n' https://<harbor-fqdn>/api/health
```

**200 is the gate.** Then:

- [ ] Confirm the super admin was seeded, and that there are **two** of them —
      the migration runner warns when there is only one, because one
      graduation should not lock the organisation out.
- [ ] Check the budget. `arctic-monthly` is set at **$140/month**, with alerts
      at 80%, 100% and forecast 100% — a new environment is a new line on it,
      not a reason for a new budget.
- [ ] Remember the Azure side being up does not mean a browser can reach it.
      That is a Vercel-side question — `API_ORIGIN`, domains, Ignored Build
      Steps — covered in `docs/architecture/deployments.md`, not here.

---

**Escalate to:** the tech lead. For anything touching a database password or a
role assignment, get a second person on the call first. A failed deploy is
recoverable by re-running it. A role assignment that quietly disappeared with
a deleted resource group, or a password nobody wrote down, is the kind of
problem that is only obvious after you already knew to go looking for it.
